namespace TokenGauge;

/// <summary>
/// Finds command-line tools. Checks the current user and machine PATH from the registry as well as this process's PATH,
/// so a tool installed after TokenGauge started is still found.
/// </summary>
internal static class ToolLocator
{
    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? GitHubCli() => Find("gh.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GitHub CLI", "gh.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GitHub CLI", "gh.exe"));

    public static string? ClaudeCode() => Find("claude.exe",
        Path.Combine(Home, ".local", "bin", "claude.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd"));

    static string? Find(string exe, params string[] knownLocations)
    {
        var path = string.Join(';',
            Environment.GetEnvironmentVariable("PATH") ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "",
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "");

        var onPath = path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Path.Combine(Environment.ExpandEnvironmentVariables(dir), exe));

        return onPath.Concat(knownLocations).FirstOrDefault(File.Exists);
    }
}
