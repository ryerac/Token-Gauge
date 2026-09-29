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
        var (windows, payAsYouGo) = Parse(root);
        var message = windows.Count > 0 ? null : payAsYouGo ?? "No limited quotas on this plan.";
        return new UsageSnapshot(Name, UsageStatus.Ok, windows, DateTimeOffset.Now, message);
    }

    /// <summary>
    /// Since June 2026 Copilot bills in AI credits (1 credit = $0.01). <c>entitlement</c> is the seat's allowance,
    /// including any extra spend an admin has approved; <c>overage_entitlement</c> is a further budget on top of it.
    /// With overage allowed and no budget, usage can run past the allowance, which <c>overage_count</c> tracks.
    /// </summary>
    static (List<UsageWindow> Windows, string? PayAsYouGo) Parse(JsonElement root)
    {
        var windows = new List<UsageWindow>();
        string? payAsYouGo = null;
        if (Json.Obj(root, "quota_snapshots") is not { } snapshots) return (windows, null);

        var resetsAt = Json.Time(root, "quota_reset_date_utc");
        foreach (var quota in snapshots.EnumerateObject())
        {
            var q = quota.Value;
            if (q.ValueKind != JsonValueKind.Object) continue;
            if (Json.Bool(q, "unlimited") == true) continue;
            if (Json.Num(q, "percent_remaining") is not { } remainingPct) continue;

            var credits = Json.Bool(q, "token_based_billing") == true;
            var entitlement = Math.Max(0, Json.Num(q, "entitlement") ?? 0);
            var budget = Math.Max(0, Json.Num(q, "overage_entitlement") ?? 0);
            var overage = Math.Max(0, Json.Num(q, "overage_count") ?? 0);
            var remaining = Json.Num(q, "remaining");
            var includedUsed = remaining is { } r ? entitlement - Math.Max(0, r) : entitlement * (100 - remainingPct) / 100;
            // Whether credits_used already counts overage isn't documented, so take whichever is larger.
            var used = Math.Max(Json.Num(q, "credits_used") ?? 0, includedUsed + overage);
            var limit = entitlement + budget;
            string Amount(double v) => credits ? $"${v / 100:N2}" : $"{v:N0}";

            // Pay-as-you-go seats have no allowance: percent_remaining reads 0, which isn't "all used".
            if (limit <= 0)
            {
                if (used > 0) payAsYouGo = $"Pay as you go: {Amount(used)} used this month.";
                continue;
            }

            var detail = $"{Amount(Math.Min(used, limit))} of {Amount(limit)} used";
            if (budget > 0) detail += $" ({Amount(entitlement)} included + {Amount(budget)} budget)";
            if (used > limit) detail += $", +{Amount(used - limit)} over";
            else if (used >= limit && Json.Bool(q, "overage_permitted") != true) detail += ", limit reached";

            var label = quota.Name == "premium_interactions"
                ? credits ? "AI credits" : "Premium requests"
                : quota.Name.Replace('_', ' ');
            windows.Add(new UsageWindow(label, Math.Clamp(used / limit * 100, 0, 100), resetsAt, detail));
        }
        return (windows, payAsYouGo);
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
