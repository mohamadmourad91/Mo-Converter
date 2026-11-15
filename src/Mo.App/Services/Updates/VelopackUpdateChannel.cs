using Velopack;
using Velopack.Sources;

namespace Mo.App.Services.Updates;

/// <summary>Updates from the GitHub releases of the project, packed with Velopack (see build/release.ps1).</summary>
internal sealed class VelopackUpdateChannel : IUpdateChannel
{
    public const string RepositoryUrl = "https://github.com/mohamadmourad91/Mo-Converter";

    private readonly UpdateManager? _manager;
    private UpdateInfo? _found;

    public VelopackUpdateChannel()
    {
        try
        {
            // For testing updates without publishing: MO_UPDATE_SOURCE names a local folder made by "vpk pack".
            var local = Environment.GetEnvironmentVariable("MO_UPDATE_SOURCE");
            var manager = !string.IsNullOrWhiteSpace(local) && Directory.Exists(local)
                ? new UpdateManager(local)
                : new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
            // A build started from bin\ (not installed by Velopack) has nothing to update.
            if (manager.IsInstalled) _manager = manager;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    public bool IsSupported => _manager is not null;

    public string? PendingVersion => _manager?.UpdatePendingRestart?.Version.ToString();

    public async Task<string?> CheckAsync(CancellationToken cancellation)
    {
        if (_manager is null) return null;
        _found = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
        cancellation.ThrowIfCancellationRequested();
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellation) =>
        _manager is null || _found is null
            ? Task.CompletedTask
            : _manager.DownloadUpdatesAsync(_found, progress.Report, cancellation);

    public void ApplyAndRestart()
    {
        if (_manager is null) return;
        var target = _found?.TargetFullRelease ?? _manager.UpdatePendingRestart;
        if (target is not null) _manager.ApplyUpdatesAndRestart(target);
    }
}
