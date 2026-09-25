using System.Text.Json;

namespace TokenGauge;

public sealed class ToolSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; }

    public ToolSettings() { }
    public ToolSettings(int intervalSeconds) => IntervalSeconds = intervalSeconds;
}

public sealed class Settings
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenGauge", "settings.json");

    /// <summary>The 5-hour window moves quickly, and it's one small request.</summary>
    public ToolSettings Claude { get; set; } = new(120);

    /// <summary>The quota is monthly, and each check also runs gh, so slower is fine.</summary>
    public ToolSettings Copilot { get; set; } = new(300);

    /// <summary>Local files only, so it can be frequent.</summary>
    public ToolSettings Codex { get; set; } = new(60);

    /// <summary>Usage percentage at which a figure turns amber.</summary>
    public double AmberAtPercent { get; set; } = 75;

    /// <summary>Usage percentage at which a figure turns red.</summary>
    public double RedAtPercent { get; set; } = 90;

    /// <summary>Gap between the strip and the tray icons, at 100% scaling.</summary>
    public int TrayGapPx { get; set; } = 8;

    /// <summary>If set, places the strip this far from the right edge of the taskbar instead of next to the tray.</summary>
    public int? OffsetFromRightPx { get; set; }

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Settings();
        }

        var defaults = new Settings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings still apply for this session; they just won't survive a restart.
        }
    }
}
