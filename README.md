# Torray

A Windows tray flyout for managing a remote [Transmission](https://transmissionbt.com/) daemon over RPC.
See [REQUIREMENTS.md](REQUIREMENTS.md) for the full specification.

Status: milestone 4 (actions). The flyout shows the torrent list with status, progress, rates, ETA, filters (including errors), and aggregate speeds.
It refreshes every 2 s while open and every 20 s while hidden, and backs off up to 60 s while the server is unreachable.

- Add torrents: paste a magnet link, use **Open .torrent…**, or drop `.torrent` files or magnet links onto the flyout.
- Pause or resume each torrent from its row.
- Remove asks first: **Remove from list** keeps the downloaded files and is the default; **Delete with data** also deletes the files on the server.

## Transmission compatibility

Transmission 4.0 and newer. Torray detects the protocol automatically: legacy RPC for 4.0, JSON-RPC 2.0 for 4.1+.
Tested with Transmission 4.0.6 and 4.1.3.

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

Build the installer with `./scripts/pack.ps1`. It writes `artifacts/releases/TorrayApp-win-Setup.exe`.
The installer is not code-signed yet, so Windows SmartScreen shows a warning.

Left-click the tray icon to toggle the flyout. Right-click it for the menu. Esc or a click elsewhere hides the flyout.
Set up the server under **Connection** in the tray menu or the flyout.

## Data locations

- Settings: `%LOCALAPPDATA%\Torray\settings.json`
- Password: `%LOCALAPPDATA%\Torray\secrets\` (encrypted with Windows DPAPI for the current user, never in settings or logs)
- Logs: `%LOCALAPPDATA%\Torray\logs\` (daily files, 14 kept, 10 MB maximum each)

## License

MIT. See [LICENSE](LICENSE).
