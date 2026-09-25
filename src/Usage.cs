namespace TokenGauge;

public enum UsageStatus { Ok, Stale, Error, NotConfigured }

/// <summary>A setup step the settings window can offer to run, based on what a check found missing.</summary>
public enum SetupAction { None, InstallGitHubCli, SignInGitHub, InstallClaudeCode, SignInClaude }

/// <summary>One limit window, e.g. Claude's 5-hour session or Copilot's monthly premium quota.</summary>
public sealed record UsageWindow(string Label, double Percent, DateTimeOffset? ResetsAt, string? Detail = null)
{
    /// <summary>Once a window's reset time has passed, its usage is back to zero even if nobody has told us yet.</summary>
    public double EffectivePercent(DateTimeOffset now) => ResetsAt is { } r && r <= now ? 0 : Percent;
}

public sealed record UsageSnapshot(
    string Service,
    UsageStatus Status,
    IReadOnlyList<UsageWindow> Windows,
    DateTimeOffset FetchedAt,
    string? Message = null,
    SetupAction Setup = SetupAction.None)
{
    public static UsageSnapshot Failed(string service, UsageStatus status, string message, SetupAction setup = SetupAction.None) =>
        new(service, status, [], DateTimeOffset.Now, message, setup);

    /// <summary>The window closest to its limit, which is what the taskbar shows.</summary>
    public UsageWindow? Headline(DateTimeOffset now) =>
        Windows.Count == 0 ? null : Windows.MaxBy(w => w.EffectivePercent(now));
}

public interface IUsageProvider
{
    string Name { get; }
    ToolSettings Settings { get; }

    /// <summary>Read on every cycle, so settings changes apply without a restart.</summary>
    TimeSpan Interval { get; }
    Task<UsageSnapshot> FetchAsync(CancellationToken ct);
}

public sealed class ServiceState(IUsageProvider provider)
{
    public IUsageProvider Provider { get; } = provider;
    public string Name => Provider.Name;

    /// <summary>Most recent successful reading, kept so a failed check still shows the last known figures.</summary>
    public UsageSnapshot? LastGood { get; internal set; }

    /// <summary>Result of the most recent check, successful or not.</summary>
    public UsageSnapshot? Latest { get; internal set; }

    public bool IsStale => Latest is { Status: not UsageStatus.Ok } && LastGood is not null;

    /// <summary>
    /// Shown only once a check has confirmed the tool is set up. A tool that isn't installed or signed in is hidden,
    /// and reappears on its own if a later check finds it.
    /// </summary>
    public bool IsAvailable => Latest is { Status: not UsageStatus.NotConfigured };
}
