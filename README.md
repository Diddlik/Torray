# Torray

A Windows tray flyout for managing a remote [Transmission](https://transmissionbt.com/) daemon over RPC.
See [REQUIREMENTS.md](REQUIREMENTS.md) for the full specification.

Status: milestone 7 (usability). The flyout shows the torrent list with status, progress, rates, ETA, filters (including errors), and aggregate speeds.
It refreshes every 2 s while open and every 20 s while hidden, and backs off up to 60 s while the server is unreachable.

- Add torrents: paste a magnet link, use **Open .torrent…**, or drop `.torrent` files or magnet links onto the flyout.
  The **⋯** button next to them sets a download folder on the server and whether to add paused.
- Several servers: add them under **Servers → Add server…** in the tray menu or the arrow next to the server name,
  and switch between them there. Each server keeps its own password. Watch folders upload to the active server.
- Pin (top right) keeps the panel open while you work in other windows; Esc or hiding the panel unpins it.
- `Ctrl+Alt+T` shows or hides the panel from anywhere (change or turn off in Settings).
- Windows notifications when a download finishes or a torrent reports an error (can be turned off in Settings).
- Details (ⓘ on a row): location, size, ratio, dates, trackers, and connected peers.
- Pause or resume each torrent from its row.
- Watch folders (tray menu or flyout): while Torray runs, new `.torrent` files are uploaded once they are fully written.
  Optionally the file moves to an `Added` subfolder after the server confirmed it. Uploads that fail because the server
  is unreachable are retried with backoff (up to 10 attempts, immediately when the connection returns); invalid files are
  reported and kept. Upload state survives restarts (`%LOCALAPPDATA%\Torray\watch-jobs.json`).
- Settings (gear icon or tray menu): start with Windows, hide the panel on focus loss, notifications, keyboard shortcut,
  automatic update checks.
- Updates come from GitHub Releases. A downloaded update shows **Restart to update** in the flyout; if you ignore it, it installs when Torray exits.
- Keyboard: Esc hides the panel; in the list, Space or Enter pauses/resumes and Delete removes the selected torrent.
- Remove asks first: **Remove from list** keeps the downloaded files and is the default; **Delete with data** also deletes the files on the server.

## Transmission compatibility

Transmission 4.0 and newer. Torray detects the protocol automatically: legacy RPC for 4.0, JSON-RPC 2.0 for 4.1+.
Tested with Transmission 4.0.6 and 4.1.3, including a server with 1,200 torrents.

Keep RPC reachable only on a trusted LAN, a VPN, or behind an HTTPS reverse proxy with authentication.

## Requirements

- Windows 11 x64
- .NET 10 SDK

## Build, test, run

```powershell
dotnet build Torray.slnx
dotnet test Torray.slnx
dotnet run --project src/Torray.App
```

To publish a release, set `<Version>` in `Directory.Build.props`, commit, and push a matching tag (for example `v0.6.0`).
The release workflow builds the installer and publishes it as a GitHub release; installed copies update from there.

Build the installer locally with `./scripts/pack.ps1`. It writes `artifacts/releases/TorrayApp-win-Setup.exe`.
The installer is not code-signed yet, so Windows SmartScreen shows a warning.

Left-click the tray icon to toggle the flyout. Right-click it for the menu. Esc or a click elsewhere hides the flyout.
Set up the server under **Connection** in the tray menu or the flyout.

Not implemented yet (spec P1): a drop target on the taskbar or tray icon, a clipboard magnet helper, file selection
and priorities, editing per-torrent speed limits, search and multi-select in the list, and daemon-to-Windows path mapping.

## Data locations

- Settings and server profiles: `%LOCALAPPDATA%\Torray\settings.json`
- Passwords: `%LOCALAPPDATA%\Torray\secrets\` (one per server, encrypted with Windows DPAPI for the current user, never in settings or logs)
- Logs: `%LOCALAPPDATA%\Torray\logs\` (daily files, 14 kept, 10 MB maximum each)

## License

MIT. See [LICENSE](LICENSE).
