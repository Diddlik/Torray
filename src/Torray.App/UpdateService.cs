using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Torray.App;

public enum UpdateCheckResult
{
    NotInstalled,
    UpToDate,
    Ready,
    Failed,
}

// Velopack downloads releases over HTTPS from GitHub and verifies each package's checksum before it is applied.
public sealed class UpdateService(ILogger<UpdateService> logger)
{
    private const string RepositoryUrl = "https://github.com/Diddlik/Torray";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _ready;

    // Raised on a background thread; UI subscribers must marshal to their dispatcher.
    public event Action<string>? UpdateReady;

    public string? ReadyVersion => _ready?.TargetFullRelease.Version.ToString();

    public async Task<UpdateCheckResult> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        // Development builds run outside a Velopack install and cannot update themselves.
        if (!_manager.IsInstalled)
        {
            return UpdateCheckResult.NotInstalled;
        }

        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return UpdateCheckResult.UpToDate;
            }

            await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
            _ready = update;
            logger.LogInformation("Update {Version} downloaded", ReadyVersion);
            UpdateReady?.Invoke(ReadyVersion!);
            return UpdateCheckResult.Ready;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Update check failed");
            return UpdateCheckResult.Failed;
        }
    }

    public void ApplyAndRestart()
    {
        if (_ready is not null)
        {
            _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
        }
    }

    // A postponed update is installed silently once the app exits, so the next start runs the new version.
    public void ApplyOnExit()
    {
        if (_ready is not null)
        {
            _manager.WaitExitThenApplyUpdates(_ready.TargetFullRelease, silent: true, restart: false);
        }
    }
}
