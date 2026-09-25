using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TokenGauge.Providers;

/// <summary>
/// Reads Claude plan limits from the endpoint behind Claude Code's /usage screen, using Claude Code's own login.
/// The token is only ever read, never refreshed: refreshing it here could sign Claude Code out.
/// </summary>
public sealed class ClaudeProvider(HttpClient http, ToolSettings settings) : IUsageProvider
{
    const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";

    static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    int _rateLimitedCount;

    public string Name => "Claude";
    public ToolSettings Settings => settings;
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(60, settings.IntervalSeconds));

    public async Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        if (!File.Exists(CredentialsPath)) return NotSignedIn();

        string? token;
        long expiresAtMs;
        // Only the claudeAiOauth section is read. The same file holds other services' tokens.
        using (var creds = JsonDocument.Parse(Json.ReadShared(CredentialsPath)))
        {
            var oauth = Json.Obj(creds.RootElement, "claudeAiOauth");
            token = oauth is { } o ? Json.Str(o, "accessToken") : null;
            expiresAtMs = oauth is { } o2 ? (long)(Json.Num(o2, "expiresAt") ?? 0) : 0;
        }

        if (string.IsNullOrEmpty(token)) return NotSignedIn();
        if (expiresAtMs > 0 && DateTimeOffset.FromUnixTimeMilliseconds(expiresAtMs) <= DateTimeOffset.UtcNow)
            return UsageSnapshot.Failed(Name, UsageStatus.Stale, "Login has expired. Using Claude Code will renew it.",
                SetupAction.SignInClaude);

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return UsageSnapshot.Failed(Name, UsageStatus.Stale, "Login was rejected. Using Claude Code will renew it.",
                SetupAction.SignInClaude);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return RateLimited(response);
        response.EnsureSuccessStatusCode();
        _rateLimitedCount = 0;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return new UsageSnapshot(Name, UsageStatus.Ok, Parse(doc.RootElement), DateTimeOffset.Now);
    }

    /// <summary>
    /// Waits as long as the server asks, or otherwise doubles the wait on each consecutive refusal (up to 30 minutes),
    /// so repeated checks don't keep the limit in place.
    /// </summary>
    UsageSnapshot RateLimited(HttpResponseMessage response)
    {
        _rateLimitedCount++;
        var now = DateTimeOffset.Now;
        var requested = response.Headers.RetryAfter switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - now,
            _ => (TimeSpan?)null,
        };
        var backoff = TimeSpan.FromTicks(Interval.Ticks << Math.Min(_rateLimitedCount, 4));
        var wait = TimeSpan.FromTicks(Math.Clamp((requested ?? backoff).Ticks, Interval.Ticks, MaxBackoff.Ticks));

        return UsageSnapshot.Failed(Name, UsageStatus.Stale,
            $"Claude's usage check is rate limited. Trying again at {Theme.When(now + wait, now)}.",
            retryAfter: wait);
    }

    UsageSnapshot NotSignedIn() => ToolLocator.ClaudeCode() is null
        ? UsageSnapshot.Failed(Name, UsageStatus.NotConfigured, "Claude Code isn't installed.", SetupAction.InstallClaudeCode)
        : UsageSnapshot.Failed(Name, UsageStatus.NotConfigured, "Claude Code isn't signed in.", SetupAction.SignInClaude);

    static List<UsageWindow> Parse(JsonElement root)
    {
        var windows = new List<UsageWindow>();

        if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var limit in limits.EnumerateArray())
            {
                if (Json.Num(limit, "percent") is not { } percent) continue;
                var model = Json.Obj(limit, "scope") is { } scope && Json.Obj(scope, "model") is { } m
                    ? Json.Str(m, "display_name")
                    : null;
                windows.Add(new UsageWindow(Label(Json.Str(limit, "kind"), model), percent, Json.Time(limit, "resets_at")));
            }
        }

        // Older response shape, in case "limits" goes away.
        if (windows.Count == 0)
        {
            foreach (var (key, label) in new[] { ("five_hour", "5-hour"), ("seven_day", "Weekly") })
            {
                if (Json.Obj(root, key) is { } w && Json.Num(w, "utilization") is { } pct)
                    windows.Add(new UsageWindow(label, pct, Json.Time(w, "resets_at")));
            }
        }

        return windows;
    }

    static string Label(string? kind, string? model) => kind switch
    {
        "session" => "5-hour",
        "weekly_all" => "Weekly",
        "weekly_scoped" => model is null ? "Weekly (scoped)" : $"Weekly ({model})",
        null => "Limit",
        _ => kind,
    };
}
