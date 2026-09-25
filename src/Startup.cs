using Microsoft.Win32;

namespace TokenGauge;

/// <summary>Start with Windows, via the current user's Run key. No admin rights needed.</summary>
internal static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "TokenGauge";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, Command);
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>If Start with Windows is on but points at an old location, points it at this exe instead.</summary>
    public static void UpdatePathIfMoved()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(RunValue) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
            key.SetValue(RunValue, Command);
    }
}
