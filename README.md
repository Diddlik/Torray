# Torray

A Windows tray flyout for managing a remote [Transmission](https://transmissionbt.com/) daemon over RPC.
See [REQUIREMENTS.md](REQUIREMENTS.md) for the full specification.

Status: milestone 2 (RPC core). You can configure and test a Transmission connection. The torrent list is not implemented yet.

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
