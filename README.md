# TokenGauge

Shows Claude, GitHub Copilot and Codex usage limits as text on the Windows 11 taskbar, just left of the tray:

```
Claude 6%  ·  Copilot 72%  ·  Codex 2%
```

A tool only appears once a check confirms it's installed and signed in, and it's picked up automatically if you set it up later. Each figure is the limit closest to running out. It turns amber at 75% and red at 90%, and a `?` means the reading is stale. Left-click for details (every limit, with reset times), right-click for Refresh / Settings / Exit. Running `TokenGauge.exe` again while it's already running also opens Settings.

## Where the numbers come from

| Service | Source | Login used |
|---|---|---|
| Claude | `api.anthropic.com/api/oauth/usage` (the endpoint behind `/usage`) | Claude Code's, read from `~/.claude/.credentials.json`. Only the `claudeAiOauth` section is read. |
| Copilot | `api.github.com/copilot_internal/user` (the endpoint VS Code uses) | GitHub CLI's, via `gh auth token` |
| Codex | Latest `rate_limits` entry in `~/.codex/sessions/**/*.jsonl` | None: local files only |

Both endpoints are undocumented and may change. Each service has its own reader in [src/Providers](src/Providers), so a change only breaks that one reader.

Logins are only ever read, never refreshed. If Claude Code's token has expired, Claude shows as stale until you next use Claude Code. Codex figures only update while you use Codex; once a window's reset time passes, it shows 0%.

## Build and run

Requires the .NET 10 SDK. Copilot also needs the GitHub CLI; the Settings window offers to install it and sign in.

```powershell
dotnet build src -c Release
.\src\bin\Release\net10.0-windows\TokenGauge.exe
```

Released versions are a single self-contained `TokenGauge.exe` on the [Releases](https://github.com/ryerac/Token-Gauge/releases) page; no .NET install needed. See [docs/releasing.md](docs/releasing.md) for single-file builds, CI and publishing a release.

## Settings

Right-click → **Settings…**. Changes apply immediately and are saved to `%APPDATA%\TokenGauge\settings.json`.

- **Per tool:** current status, "Show on the taskbar", check interval, and a setup button when something is missing.
  Setup buttons open a PowerShell window (Install GitHub CLI, Sign in to GitHub, Install Claude Code, Sign in to Claude); the tool is re-checked when that window closes.
- **Display:** amber/red thresholds, and whether the strip sits next to the tray or at a fixed distance from the right edge.
- **General:** Start with Windows.

Defaults: Claude every 2 minutes, Copilot every 5 (the quota is monthly), Codex every minute (local files). `TrayGapPx` (default 8) is only in the JSON file.
