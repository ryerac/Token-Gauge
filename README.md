# TokenGauge

Shows your Claude, GitHub Copilot and Codex usage limits on the Windows 11 taskbar, next to the tray.

![TokenGauge on the taskbar, with the details popup (every limit, usage bar and reset time) and the Settings window open](docs/ss1.png)

## Install

Download `TokenGauge.exe` from [Releases](https://github.com/ryerac/Token-Gauge/releases) and run it. It's a single self-contained file; no installer or .NET needed. Tick **Start with Windows** in Settings to keep it running.

Requires Windows 11. Each tool appears once it's set up:

- **Claude:** signed in to Claude Code.
- **Copilot:** the [GitHub CLI](https://cli.github.com/), signed in. Settings can install it and sign in for you.
- **Codex:** used at least once (CLI or VS Code extension).

## Use

Each figure is the limit closest to running out. It turns amber at 75% and red at 90%, and `?` means the last check failed so you're seeing the previous reading.

- **Left-click:** every limit, with reset times.
- **Right-click:** Refresh now, Settings, Exit. Running `TokenGauge.exe` again also opens Settings.

Settings apply immediately and are saved to `%APPDATA%\TokenGauge\settings.json`. They cover, per tool: show or hide, check interval (defaults: Claude 2 min, Copilot 5, Codex 1) and setup buttons. Also the colour thresholds, position and Start with Windows.

## How it works

| Tool | Source | Login |
| --- | --- | --- |
| Claude | `api.anthropic.com/api/oauth/usage`, the endpoint behind Claude Code's `/usage` | Claude Code's, from `~/.claude/.credentials.json` (only the `claudeAiOauth` section) |
| Copilot | `api.github.com/copilot_internal/user`, the endpoint VS Code uses | GitHub CLI's, via `gh auth token` |
| Codex | Latest `rate_limits` entry in `~/.codex/sessions` | None: local files only |

Logins are read, never refreshed or stored, and only sent to the service they belong to. If Claude Code's login expires, Claude shows its last reading until you next use Claude Code.

Codex figures come from the logs Codex writes on this PC, so they only update when you use Codex here. Usage on another machine or in the browser won't show until your next Codex request on this PC. The details popup shows when the figures were last updated.

TokenGauge is unofficial and not affiliated with Anthropic, GitHub or OpenAI. Both endpoints are undocumented and may change; each tool has its own reader in [src/Providers](src/Providers), so a change only breaks that one.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet build src -c Release
.\src\bin\Release\net10.0-windows\TokenGauge.exe
```

See [docs/releasing.md](docs/releasing.md) for the single-file build, CI and publishing a release.

## License

[MIT](LICENSE)
