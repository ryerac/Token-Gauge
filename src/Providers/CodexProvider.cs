using System.Text.Json;

namespace TokenGauge.Providers;

/// <summary>
/// Reads Codex rate limits from the session logs the Codex CLI and VS Code extension write under ~/.codex/sessions.
/// No network or login needed; figures update whenever Codex is used.
/// </summary>
public sealed class CodexProvider(ToolSettings settings) : IUsageProvider
{
    const string NoDataMessage = "Use Codex once (CLI or VS Code extension) and its limits will appear here.";

    static readonly string SessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    (string Path, DateTime LastWrite, UsageSnapshot Snapshot)? _cache;

    public string Name => "Codex";
    public ToolSettings Settings => settings;
    public TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(10, settings.IntervalSeconds));

    public Task<UsageSnapshot> FetchAsync(CancellationToken ct)
    {
        if (!Directory.Exists(SessionsDir))
            return Task.FromResult(UsageSnapshot.Failed(Name, UsageStatus.NotConfigured, NoDataMessage));

        foreach (var file in RecentSessionFiles())
        {
            ct.ThrowIfCancellationRequested();
            var lastWrite = File.GetLastWriteTimeUtc(file);
            if (_cache is { } c && c.Path == file && c.LastWrite == lastWrite)
                return Task.FromResult(c.Snapshot);

            if (ReadLatest(file) is { } snapshot)
            {
                _cache = (file, lastWrite, snapshot);
                return Task.FromResult(snapshot);
            }
        }

        return Task.FromResult(UsageSnapshot.Failed(Name, UsageStatus.NotConfigured, NoDataMessage));
    }

    /// <summary>Newest session files first. Logs are stored as sessions/yyyy/MM/dd/rollout-*.jsonl.</summary>
    static IEnumerable<string> RecentSessionFiles()
    {
        static IEnumerable<string> Newest(string dir) =>
            Directory.EnumerateDirectories(dir).OrderByDescending(Path.GetFileName, StringComparer.Ordinal);

        var files =
            from year in Newest(SessionsDir)
            from month in Newest(year)
            from day in Newest(month)
            from file in Directory.EnumerateFiles(day, "*.jsonl")
            select file;

        // A session resumed later can be newer than its folder date suggests, so sort a handful by write time.
        return files.Take(20).OrderByDescending(File.GetLastWriteTimeUtc).Take(5).ToList();
    }

    UsageSnapshot? ReadLatest(string file)
    {
        var lines = Json.ReadShared(file).Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(lines[i]);
                if (Json.FindObject(doc.RootElement, "rate_limits") is not { } limits) continue;

                var windows = new List<UsageWindow>();
                foreach (var key in new[] { "primary", "secondary" })
                {
                    if (Json.Obj(limits, key) is { } w && Json.Num(w, "used_percent") is { } pct)
                        windows.Add(new UsageWindow(Label(Json.Num(w, "window_minutes")), pct, Json.Time(w, "resets_at")));
                }
                if (windows.Count == 0) continue;

                var seenAt = Json.Time(doc.RootElement, "timestamp") ?? new DateTimeOffset(File.GetLastWriteTimeUtc(file));
                var plan = Json.Str(limits, "plan_type");
                return new UsageSnapshot(Name, UsageStatus.Ok, windows, seenAt.ToLocalTime(),
                    plan is null ? "From Codex logs" : $"From Codex logs · {plan} plan");
            }
            catch (JsonException)
            {
                // Partially written last line; try the one before.
            }
        }
        return null;
    }

    static string Label(double? minutes) => minutes switch
    {
        300 => "5-hour",
        10080 => "Weekly",
        { } m when m % 1440 == 0 => $"{m / 1440:0}-day",
        { } m when m % 60 == 0 => $"{m / 60:0}-hour",
        { } m => $"{m:0}-minute",
        null => "Limit",
    };
}
