# Torray

A Windows tray flyout for managing a remote [Transmission](https://transmissionbt.com/) daemon over RPC.
See [REQUIREMENTS.md](REQUIREMENTS.md) for the full specification.

Status: milestone 1 (foundation). The tray icon and flyout shell work. Transmission connectivity is not implemented yet.

## Requirements

- Windows 11 x64
- .NET 10 SDK

## Build, test, run

```powershell
dotnet build Torray.slnx
dotnet test Torray.slnx
dotnet run --project src/Torray.App
```

Left-click the tray icon to toggle the flyout. Right-click it for the menu. Esc or a click elsewhere hides the flyout.

## Data locations

- Settings: `%LOCALAPPDATA%\Torray\settings.json`
- Logs: `%LOCALAPPDATA%\Torray\logs\` (daily files, 14 kept, 10 MB maximum each)

## License

MIT. See [LICENSE](LICENSE).
