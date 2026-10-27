# Transmission Flyout — Product Requirements & Technical Specification

**Version:** 1.0  
**Status:** Implementation-ready draft  
**Target:** Windows 11 desktop, C# / .NET 10 / WPF  
**Primary use case:** Manage a remote Transmission BitTorrent daemon running on a NAS or Docker host from a lightweight Windows tray flyout.

> This document is a specification for coding agents. Implement the MVP first, respect acceptance criteria, and do not silently expand scope. Validate Transmission's installed version and RPC compatibility against its official RPC specification during implementation.

## 1. Vision and product decisions

Build a small, resource-efficient Windows application that lives in the system tray. A click opens a compact flyout showing the Transmission queue, statuses, progress, total bandwidth and quick actions. Users can add `.torrent` files and magnet URLs, monitor local watch folders, and manage torrents without opening the web UI.

**Chosen UX:** tray flyout as primary surface; global hotkey and optional smart drop zone as enhancements; optional pin-able mini-widget later. **Not** an official Windows Widgets Board widget.

**Guiding principles:**
- Keep desktop uncluttered; no permanent window required.
- Remain useful when NAS is disconnected; show stale state clearly and retry safely.
- Never delete downloaded data without an explicit, separate confirmation.
- Operate over Transmission RPC, not direct Docker APIs or network share mounts.
- No torrent downloading, parsing of payload content, or BitTorrent networking in the Windows app itself.
- Work offline on the Windows machine except for communication with configured Transmission servers; do not introduce telemetry.

## 2. Target environment and boundaries

- Windows 11 x64 is the primary target; Windows 10 support is optional.
- .NET 10 LTS, WPF, CommunityToolkit.Mvvm, dependency injection and `HttpClient`.
- Transmission daemon installed remotely, e.g. Docker on NAS; detect server/RPC version and support it by explicit compatibility tests.
- Default RPC endpoint is derived from `http(s)://host:port/transmission/rpc` but must be editable.
- Server may require HTTP authentication and Transmission's session-id handshake.
- App should support one configured connection in MVP, with architecture prepared for multiple servers.
- Legal use only: application is a generic client for user-supplied torrent metadata; no torrent catalog, search index or bundled copyrighted material.

## 3. Scope / priorities

### P0 — MVP (required)

| ID | Requirement | Acceptance criteria |
|---|---|---|
| CON-01 | Manage one remote Transmission connection (host, port, scheme, RPC path, optional username/password). | User can save, edit and test connection; test displays success or actionable error. |
| CON-02 | Handle RPC session-id negotiation, auth, errors, version detection, cancellation and timeouts. | A session-id rejection is retried once using returned ID; wrong credentials and offline host are reported without crashing. |
| UI-01 | Run in system tray, click to toggle flyout, close flyout on focus loss (configurable), no taskbar entry in background. | Tray is visible after startup; reopening restores responsive UI. |
| UI-02 | Show queue list with name, percent complete, status, current down/up rate, downloaded/total size and ETA if available. | Display corresponds to latest successful server refresh; unknown metrics shown as `—`, never fabricated. |
| UI-03 | Show aggregate download/upload speed, connection health and counts (active, queued, paused, seeding). | Counts consistently reflect the fetched torrent list. |
| UI-04 | Filters: all, downloading, seeding, paused/stopped; sorting by queue position where supported. | Filter preserves selection and handles empty state. |
| TOR-01 | Add `.torrent` via file picker and drag-and-drop onto flyout. | Valid files are sent via RPC `torrent-add` / version-appropriate equivalent, with duplicates and errors shown clearly. |
| TOR-02 | Add magnet link through paste field; support drag-and-drop of text/URLs when recognized. | Valid magnet is accepted; invalid text is rejected before RPC call. |
| TOR-03 | Start/resume, pause/stop selected torrent. | Action updates server; UI reconciles state on next refresh. |
| TOR-04 | Remove torrent **without** deleting downloaded data. | Default action sends remove with delete-local-data false and removes item after server confirmation. |
| TOR-05 | Separate action to remove torrent **and** delete downloaded data. | Requires explicit warning and confirmation stating data loss; never default. |
| WATCH-01 | Configure one or more local watch folders for `.torrent` files. | New completed `.torrent` file gets uploaded once; duplicate watcher events do not cause duplicate action. |
| WATCH-02 | Reliable watch-folder processing. | Wait until file is stable/readable, persist queue state, retry on transient failure, and do not archive/delete source before confirmed success. |
| SET-01 | Persist nonsecret settings; protect credentials via Windows DPAPI or Windows Credential Manager. | Password is never stored in plaintext JSON or logs. |
| SYS-01 | Optional Windows sign-in autostart; start minimized to tray. | Enabled setting works without elevating privileges. |
| SYS-02 | Adaptive refresh and app shutdown. | Refresh faster when flyout visible (default 2 s), slower in background (default 20 s), suspend/coalesce redundant requests; clean cancellation on exit. |
| QUAL-01 | Logging, error reporting, crash resistance. | Serilog rolling local files; no secrets, torrent file payloads, magnet passkeys or credential-bearing URLs in logs. |

### P1 — Usability enhancements

| ID | Requirement | Acceptance criteria |
|---|---|---|
| UX-01 | Global configurable hotkey (`Ctrl+Alt+T` default) to show/hide flyout. | Conflict/registration failure is visible; configurable and can be disabled. |
| UX-02 | Drag activation / smart drop zone. | When a supported drag begins over the app's activation surface, a drop target is accessible; **do not** promise system-wide detection of arbitrary drags without a separately validated Windows shell mechanism. |
| UX-03 | Clipboard magnet helper, opt-in. | Only scan on explicit invocation or user-configured shortcut; avoid background clipboard collection by default. |
| UX-04 | Native Windows notifications for completion and errors. | Notification fires only for genuine state transitions; user can disable. |
| UX-05 | Pin button in the flyout header that keeps the flyout open. | While pinned, focus loss does not hide the flyout; unpinning or Esc restores normal behavior; the state is visible on the button. |
| TOR-06 | Add-dialog destination folder and start paused. | User choices map to server-supported RPC fields; invalid paths reported. |
| TOR-07 | Torrent file selection and priority. | Fetch files; toggle wanted/unwanted and priority using supported RPC APIs. |
| TOR-08 | Per-torrent speed limits and details (trackers, peers, ratio). | Values are correctly rendered, editable only when RPC supports them. |
| UI-05 | Search, additional sorting, keyboard navigation, context menu, multi-select. | Works with 1,000+ rows without freezing. |
| SERVER-01 | Multiple server profiles and quick switch. | Profiles maintain independent auth and state; no data bleed. |
| NAS-01 | Optional path mapping from daemon paths (e.g. `/downloads`) to Windows UNC paths. | `Open in Explorer` works only when mapping exists and share is reachable. |

### P2 — Optional extensions

- Pin-able mini-progress widget, always-on-top optionally, adjustable opacity and snap-to-edge.
- Desktop activity indicator / extended tray icon statuses.
- Queue reordering, alternate speed limits, labels and rules if exposed by installed Transmission version.
- Server-side watch-folder configuration guidance (not necessarily remote config mutation); compare reliability with local watcher.
- Free space display, session statistics, historical charts.
- Explorer `Send to Transmission` shell integration, portable/MSIX packaging, localization (DE/EN).

## 4. UX specification

### Tray and flyout

- Left-click tray: toggle compact flyout; right-click: menu (`Open`, `Add Torrent`, `Connection`, `Settings`, `Exit`).
- Default size roughly 420 × 600 device-independent pixels; responsive to scaling (100–200%) and multi-monitor taskbar placement.
- Anchor to the tray icon or taskbar edge; respect work area and DPI. The panel must stay onscreen.
- Dark and light theme following OS; clean Fluent-inspired layout, not dependent on Windows 11 Widgets Board.
- Header: server name and live connection status; global download/upload rates; action to add a magnet or `.torrent`.
- Main: filter tabs, scrollable virtualized list, status icons, progress bars and per-row pause/resume/remove actions. Destructive menu separate.
- Footer: drop target/help text and settings access. Provide visible drop state when file is dragged over.
- Empty state: “No torrents”; offline state: “Cannot connect” with last-refresh time and `Retry`.
- Use `INotifyPropertyChanged`/observable collections; UI updates marshaled to WPF dispatcher. Avoid blocking calls on UI thread.
- Keyboard support: Esc closes flyout (unless modal), Enter activates selected item, context actions accessible without mouse.
- UX accessibility: tooltip/accessible name for icon-only actions, reasonable minimum touch targets, high-contrast support.

### Add torrent

1. Accept file picker, `.torrent` file drag/drop, magnet text paste/drop.
2. Validate file existence / readable / configurable size limit, magnet scheme and URL syntax.
3. Optionally show add-dialog to choose destination and paused state; MVP may submit immediately using daemon defaults.
4. Upload `.torrent` as RPC metainfo (base64) and magnet as filename/URL parameter per protocol supported by server.
5. Show outcome including duplicate torrent ID and failures; never assume asynchronous update is successful solely due to UI response.

### Remove torrent

- Primary `Remove` means **remove from Transmission only**; preserve downloaded data.
- Separate `Remove and delete data` is explicit, with a confirmation dialog showing torrent name and irreversible consequence.
- Disable duplicate commands in flight; reconcile RPC result and list.

## 5. Watch folders — durable workflow

- Local watcher is a **Windows-client feature** and runs only while the Windows app is running. Server-side Transmission watch folders run independently of it.
- Local configuration per folder: path, enabled, default target directory (optional), start paused, success action (`keep`/`move to archive`), failure action (`keep and retry`).
- Combine `FileSystemWatcher` events with periodic directory scan to recover missed events/startup backlog.
- Accept only `.torrent`; ignore temporary and incomplete files, verify stable size and ability to open file before upload.
- Persist pending items by stable key (canonical path + fingerprint/content hash + modification time); ensure restart recovery and deduplication.
- Handle common duplicate response as a terminal processed status; archive only after the server confirms acceptance or a safely recognized duplicate.
- Retry transient network failures with bounded exponential backoff + jitter; expose queued failures in UI. Do not endlessly retry structurally invalid files.
- Avoid overwriting archive files, unsafe path traversal, and unexpected deletion.
- Never implement a local watch folder by exposing RPC publicly to the internet.

## 6. Transmission RPC protocol contract

Implement an explicit version-aware `ITransmissionRpcClient` using `HttpClient`. The exact method/field names must be validated against the target server's version, particularly the changes between older Transmission 4.0-compatible RPC and Transmission 4.1 JSON-RPC 2.0 / snake_case forms.

Core operations:
- Session info / connection probe / server capabilities.
- List torrents with necessary fields, including IDs, name, status, size, percent, rates, ETA, queue position, download directory.
- Add torrent via `metainfo` for file bytes or magnet URI; track duplicate return semantics.
- Start/stop, remove with explicit `delete_local_data` boolean or equivalent versioned parameter.
- Session stats (aggregate rates), torrent details and updates in P1.

Protocol requirements:
- Support `409` session handshake via `X-Transmission-Session-Id` and bounded retry.
- Honor HTTP Basic auth if configured; HTTPS with normal certificate validation (do not disable validation for convenience).
- Handle `401`/`403`, server errors, malformed JSON, schema mismatch, rate limits where applicable, connection drops, retryable transient errors and cancellation.
- Discover capability changes rather than hardcoding unchecked assumptions; fail gracefully for unsupported actions.
- Never log bearer/session credentials, full magnet URLs (may contain passkeys), usernames/passwords or raw torrent contents.
- Be compatible with Docker networking and custom RPC URL paths.

**Security constraint:** RPC should be reachable only via trusted LAN, VPN or TLS-protected reverse proxy with authentication. Do not expose it publicly by default.

## 7. Architecture and suggested repository

```text
TransmissionFlyout/
├── README.md
├── REQUIREMENTS.md
├── docs/
│   ├── ARCHITECTURE.md
│   ├── RPC_COMPATIBILITY.md
│   └── TEST_PLAN.md
├── src/
│   ├── TransmissionFlyout.App/              # WPF shell, tray, flyout, views & viewmodels
│   ├── TransmissionFlyout.Application/      # Commands, polling, watch-folder orchestration
│   ├── TransmissionFlyout.Domain/           # Entities, status normalization, interfaces
│   └── TransmissionFlyout.Infrastructure/   # HTTP RPC, settings, credentials, file watch, logging
├── tests/
│   ├── TransmissionFlyout.UnitTests/
│   └── TransmissionFlyout.IntegrationTests/
└── .github/workflows/build.yml
```

### Suggested abstractions

- `ITransmissionRpcClient`: `GetSessionAsync`, `GetTorrentsAsync`, `GetStatsAsync`, `AddTorrentAsync`, `StartAsync`, `StopAsync`, `RemoveAsync`.
- `IConnectionProfileStore`, `ISecretStore`.
- `ITorrentService`: normalize server state, coordinate UI commands and recovery.
- `IWatchFolderService`, `IWatchJobStore`.
- `IRefreshScheduler`: adaptive poll, cancellation, no concurrent overlapping refreshes.
- `INotificationService`, `ITrayService`, `IHotkeyService` (P1).
- `TorrentListViewModel`, `TorrentItemViewModel`, `SettingsViewModel`, `ConnectionViewModel`.

DI: `Microsoft.Extensions.Hosting`; `IHttpClientFactory` or typed clients, cancellation tokens throughout. Use async all the way. Prefer fewer external UI frameworks initially; use WPF stock controls plus styles where sufficient.

### Data handling

- Nonsecret app settings saved under `%LOCALAPPDATA%/TransmissionFlyout/`.
- Credentials stored using DPAPI (current-user scope) or Credential Manager; settings store only reference identifiers.
- Logs saved under `%LOCALAPPDATA%/TransmissionFlyout/logs/`, bounded retention and size.
- Watch job state stored durably (JSON atomic writes acceptable for MVP; SQLite is optional if complexity warrants).
- Avoid torrents being sent to any service other than the selected Transmission host.

## 8. Performance and reliability

- Application should remain responsive at 1,000+ torrents; virtualized items and incremental diff update required where practical.
- Do not fetch large files/peer/tracker detail collections in the background overview; load details on demand.
- Target idle CPU near zero and modest memory footprint; measure rather than claiming hard performance figures prematurely.
- Default polling: 2 s when visible, 20 s hidden, configurable; avoid overlapping requests. Back off while disconnected.
- Display last successful refresh time and clear connected/disconnected/degraded states.
- On restart, watch jobs resume without silent loss; user operations in progress are reported, not silently duplicated.
- Handle shutdown and Windows session ending cleanly.

## 9. Quality, testing and acceptance

### Unit tests

- RPC serialization and version adapter mappings, parsing, status normalization.
- Magnet validation and `.torrent` payload handling.
- Watch-folder stability, fingerprint deduplication, retry policy and archive behavior.
- ViewModel commands: pause/resume/delete safety, filters and empty/error states.
- Settings serialization and secret redaction.

### Integration tests

- Use a disposable test Transmission daemon container with isolated downloads and no live user torrents.
- Test connection, session-id handshake, authentication failure, torrent add/duplicate, pause/resume/remove and deletion flag separately.
- Test offline/reconnect, server version mismatch, malformed responses.
- Test file watch while disconnected, then reconnect, and app restart recovery.
- For destructive data deletion tests, use **only generated disposable fixtures**.

### Manual Windows checklist

- Windows 11 100/150/200% DPI, dark/light themes, taskbar left/center and different monitor/taskbar positions.
- Drag `.torrent` from Explorer, paste magnet, start/stop, remove and remove+delete confirmation.
- Sign-in autostart, hidden polling, tray context menu and quit.
- Keyboard navigation, screen reader labels, offline states, long filenames, large torrent queue.

### MVP definition of done

- All P0 acceptance criteria implemented.
- Build and test instructions in README; `dotnet build` and `dotnet test` pass.
- Portable or unpackaged Windows executable available; no admin requirement for everyday use.
- Tested with at least one specified modern Transmission release, and version support explicitly documented.
- No plaintext credentials in settings/logs; no unconfirmed deletion of local download data.

## 10. Delivery plan for coding agents

Do the following in order; create a small, buildable increment and tests at each milestone.

1. **Foundation:** solution/projects, MVVM, DI, logging, settings interfaces, basic WPF tray/flyout skeleton.
2. **RPC core:** protocol models, session handshake, version detection, tests with mocked HTTP, connection settings + test connection.
3. **Read-only dashboard:** queue listing, status mapping, aggregate speeds, filters and adaptive polling.
4. **Actions and upload:** start/stop, safe remove, explicit remove+delete confirmation, `.torrent` and magnet add.
5. **Local watch folders:** file stability check, durable job queue, retries, archive behavior, settings UI.
6. **Hardening:** disconnect handling, credential storage, performance tests, accessible UI, DPI, packaging.
7. **P1:** pin button (UX-05), hotkey, notifications, optional drop activation surface, torrent details/multi-server.
8. **P2:** optional pinned mini-widget and automation enhancements.

At the end of each milestone, supply: modified file summary, commands run, test results, remaining risks, next milestone. Do not claim tests pass unless executed.

## 11. Open design decisions (defaults)

| Topic | Recommended default |
|---|---|
| Application name | Transmission Flyout (working title) |
| Connection count | One in MVP; extensible architecture |
| Visibility | Tray-only at startup |
| UI width | ~420 DIP |
| Theme | Follow Windows |
| Poll | 2 s open / 20 s closed |
| Add flow | Immediate add using remote daemon defaults; advanced dialog later |
| Delete action | Preserve data by default |
| Local watch behavior | Keep source on failure; archive only on confirmed success |
| Hotkey | `Ctrl+Alt+T` (P1, configurable) |
| Network security | LAN/VPN preferred, HTTPS supported, certificate validation on |
| Packaging | Portable unpackaged app first |

## 12. References for implementation validation

- Transmission project: https://github.com/transmission/transmission
- Transmission RPC specification / documentation: https://github.com/transmission/transmission/blob/main/docs/rpc-spec.md (verify branch/tag and file path against installed server)
- .NET WPF documentation: https://learn.microsoft.com/dotnet/desktop/wpf/
- CommunityToolkit.Mvvm: https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/

---

## 13. Starter prompt for a local coding agent

> Implement **Milestone 1** from `REQUIREMENTS.md` for Transmission Flyout. Use C#/.NET 10 WPF, CommunityToolkit.Mvvm and a maintainable layered structure. Create the solution and projects, DI/hosting, settings interfaces, logging, and a working Windows tray icon that toggles a compact flyout window. Include unit-test infrastructure and README instructions. No fake Transmission connectivity. Preserve clean architecture and only implement Milestone 1. Run `dotnet build` and `dotnet test` if available; report actual results and blockers. Do not proceed to Milestone 2 until Milestone 1 is complete.
