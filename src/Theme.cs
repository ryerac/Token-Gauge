using Microsoft.Win32;

namespace TokenGauge;

public enum Level { Normal, Amber, Red }

internal sealed record Palette(Color Background, Color Text, Color Dim, Color Track, Color Separator, Color Accent, Color Amber, Color Red)
{
    public static readonly Palette Dark = new(
        Background: Color.FromArgb(0x20, 0x20, 0x20),
        Text: Color.FromArgb(0xF3, 0xF3, 0xF3),
        Dim: Color.FromArgb(0xA8, 0xA8, 0xA8),
        Track: Color.FromArgb(0x3C, 0x3C, 0x3C),
        Separator: Color.FromArgb(0x33, 0x33, 0x33),
        Accent: Color.FromArgb(0x4C, 0xA3, 0xFF),
        Amber: Color.FromArgb(0xF2, 0xA3, 0x3A),
        Red: Color.FromArgb(0xFF, 0x63, 0x5A));

    public static readonly Palette Light = new(
        Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
        Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
        Dim: Color.FromArgb(0x5F, 0x5F, 0x5F),
        Track: Color.FromArgb(0xE0, 0xE0, 0xE0),
        Separator: Color.FromArgb(0xE5, 0xE5, 0xE5),
        Accent: Color.FromArgb(0x00, 0x67, 0xC0),
        Amber: Color.FromArgb(0xA8, 0x5F, 0x00),
        Red: Color.FromArgb(0xC4, 0x2B, 0x1C));

    public Color For(Level level) => level switch
    {
        Level.Red => Red,
        Level.Amber => Amber,
        _ => Text,
    };
}

internal static class Theme
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The taskbar follows the "Windows mode" setting; app windows follow "App mode".</summary>
    public static Palette Taskbar => ReadLight("SystemUsesLightTheme") ? Palette.Light : Palette.Dark;
    public static Palette Apps => ReadLight("AppsUseLightTheme") ? Palette.Light : Palette.Dark;

    static bool ReadLight(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is int v && v == 1;
    }

    public static Level LevelFor(double percent, Settings s) =>
        percent >= s.RedAtPercent ? Level.Red : percent >= s.AmberAtPercent ? Level.Amber : Level.Normal;

    public static string Until(DateTimeOffset target, DateTimeOffset now)
    {
        var d = target - now;
        if (d <= TimeSpan.Zero) return "now";
        if (d.TotalDays >= 1) return $"{(int)d.TotalDays}d {d.Hours}h";
        if (d.TotalHours >= 1) return $"{d.Hours}h {d.Minutes}m";
        return $"{Math.Max(1, d.Minutes)}m";
    }

    public static string When(DateTimeOffset t, DateTimeOffset now)
    {
        var local = t.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? local.ToString("HH:mm") : local.ToString("ddd d MMM HH:mm");
    }
}
