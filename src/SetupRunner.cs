using System.Diagnostics;
using System.Text;

namespace TokenGauge;

/// <summary>
/// Runs a setup step in a visible PowerShell window, so the user sees every prompt, browser sign-in and error.
/// The window waits for Enter before closing; the caller re-checks the tool when the process exits.
/// </summary>
internal static class SetupRunner
{
    public static string Label(SetupAction action) => action switch
    {
        SetupAction.InstallGitHubCli => "Install GitHub CLI",
        SetupAction.SignInGitHub => "Sign in to GitHub",
        SetupAction.InstallClaudeCode => "Install Claude Code",
        SetupAction.SignInClaude => "Sign in to Claude",
        _ => "",
    };

    public static Process Start(SetupAction action)
    {
        var body = action switch
        {
            SetupAction.InstallGitHubCli => InstallGh + FindGh + SignInGh,
            SetupAction.SignInGitHub => FindGh + SignInGh,
            SetupAction.InstallClaudeCode => InstallClaude + FindClaude + SignInClaude,
            SetupAction.SignInClaude => FindClaude + SignInClaude,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        var script =
            $"$Host.UI.RawUI.WindowTitle = 'TokenGauge: {Label(action)}'\n" +
            body +
            "\nWrite-Host ''\nRead-Host 'Finished. Press Enter to close this window'\n";

        // A console program started from a windowed app gets its own new console window.
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return Process.Start(psi) ?? throw new InvalidOperationException("PowerShell didn't start.");
    }

    // The window inherits TokenGauge's PATH, which won't include a tool installed a moment ago,
    // so each script looks in the tool's install folder as well.

    const string InstallGh = """
        Write-Host 'Installing GitHub CLI with winget...' -ForegroundColor Cyan
        winget install --id GitHub.cli --exact --source winget --accept-package-agreements --accept-source-agreements

        """;

    const string FindGh = """
        $gh = @(
          (Get-Command gh -ErrorAction SilentlyContinue).Source,
          "$env:ProgramFiles\GitHub CLI\gh.exe",
          "$env:LOCALAPPDATA\Programs\GitHub CLI\gh.exe"
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

        """;

    const string SignInGh = """
        if ($gh) {
          Write-Host ''
          Write-Host 'Signing in to GitHub. Copy the one-time code shown below, then paste it into the browser page that opens.' -ForegroundColor Cyan
          & $gh auth login --hostname github.com --git-protocol https --web
        } else {
          Write-Host 'GitHub CLI was not found. Try installing it again.' -ForegroundColor Red
        }

        """;

    const string InstallClaude = """
        Write-Host 'Installing Claude Code...' -ForegroundColor Cyan
        Invoke-RestMethod https://claude.ai/install.ps1 | Invoke-Expression

        """;

    const string FindClaude = """
        $claude = @(
          (Get-Command claude -ErrorAction SilentlyContinue).Source,
          "$HOME\.local\bin\claude.exe"
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

        """;

    const string SignInClaude = """
        if ($claude) {
          Write-Host ''
          Write-Host 'Starting Claude Code. If it asks you to log in, follow the prompts. Otherwise type /login.' -ForegroundColor Cyan
          Write-Host 'Type /exit when you are done.' -ForegroundColor Cyan
          Set-Location $HOME
          & $claude
        } else {
          Write-Host 'Claude Code was not found. Try installing it again.' -ForegroundColor Red
        }

        """;
}
