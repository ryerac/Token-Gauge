using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenGauge.Providers;

/// <summary>
/// Reads Copilot quotas from the endpoint VS Code uses, authenticating with the GitHub CLI's login
/// (<c>gh auth token</c>) so no credential file is ever read directly.
/// </summary>
public sealed class CopilotProvider(HttpClient http, ToolSettings settings) : IUsageProvider
{
    const string UserUrl = "https://api.github.com/copilot_internal/user";

    public string Name => "Copilot";
    public ToolSettings Settings => settings;
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(60, settings.IntervalSeconds));

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        var gh = ToolLocator.GitHubCli();
        if (gh is null)
            return UsageSnapshot.Failed(Name, UsageStatus.NotConfigured,
                "Needs the GitHub CLI, which isn't installed.", SetupAction.InstallGitHubCli);

        var token = await GetTokenAsync(gh, ct);
        if (token is null)
            return UsageSnapshot.Failed(Name, UsageStatus.NotConfigured,
                "The GitHub CLI isn't signed in.", SetupAction.SignInGitHub);

        using var request = new HttpRequestMessage(HttpMethod.Get, UserUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return UsageSnapshot.Failed(Name, UsageStatus.Stale, "GitHub rejected the login.", SetupAction.SignInGitHub);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var windows = Parse(root);
        var message = windows.Count == 0 ? "No limited quotas on this plan." : null;
        return new UsageSnapshot(Name, UsageStatus.Ok, windows, DateTimeOffset.Now, message);
    }

    static List<UsageWindow> Parse(JsonElement root)
    {
        var windows = new List<UsageWindow>();
        if (Json.Obj(root, "quota_snapshots") is not { } snapshots) return windows;

        var resetsAt = Json.Time(root, "quota_reset_date_utc");
        foreach (var quota in snapshots.EnumerateObject())
        {
            var q = quota.Value;
            if (q.ValueKind != JsonValueKind.Object) continue;
            if (q.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True) continue;
            if (Json.Num(q, "percent_remaining") is not { } remainingPct) continue;

            string? detail = null;
            if (Json.Num(q, "entitlement") is { } entitlement and > 0 && Json.Num(q, "remaining") is { } remaining)
                detail = $"{entitlement - remaining:N0} of {entitlement:N0} used";

            var label = quota.Name == "premium_interactions" ? "Premium requests" : quota.Name.Replace('_', ' ');
            windows.Add(new UsageWindow(label, 100 - remainingPct, resetsAt, detail));
        }
        return windows;
    }

    static async Task<string?> GetTokenAsync(string gh, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(gh)
        {
            ArgumentList = { "auth", "token", "--hostname", "github.com" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        if (process is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var token = (await stdout).Trim();
            return process.ExitCode == 0 && token.Length > 0 ? token : null;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch (InvalidOperationException) { }
            throw;
        }
    }
}
