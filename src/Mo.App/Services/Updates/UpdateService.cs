namespace Mo.App.Services.Updates;

/// <summary>
/// Mandatory updates. Checks shortly after start and then every few hours; a newer version is downloaded
/// in the background and, once it is ready, <see cref="Ready"/> fires so the app can stop the wheels and
/// install it. A version left over from an earlier run is installed before anything else starts.
/// Without a network connection the app keeps working and simply tries again later.
/// </summary>
public sealed class UpdateService
{
    public static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Regular = TimeSpan.FromHours(4);
    public static readonly TimeSpan AfterFailure = TimeSpan.FromMinutes(30);

    /// <summary>How long the "update required" window waits before it installs on its own.</summary>
    public static readonly TimeSpan Countdown = TimeSpan.FromSeconds(60);

    private readonly IUpdateChannel _channel;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<Exception> _log;

    public UpdateService(IUpdateChannel channel, Func<TimeSpan, CancellationToken, Task>? delay = null, Action<Exception>? log = null)
    {
        _channel = channel;
        _delay = delay ?? Task.Delay;
        _log = log ?? CrashLog.Write;
    }

    /// <summary>A newer version was found and is being downloaded.</summary>
    public event Action<string>? Downloading;

    /// <summary>The newer version is downloaded and must now be installed.</summary>
    public event Action<string>? Ready;

    public bool IsSupported => _channel.IsSupported;

    /// <summary>The downloaded version waiting to be installed, or null.</summary>
    public string? ReadyVersion { get; private set; }

    /// <summary>
    /// Installs a version downloaded in an earlier run (the user quit instead of updating).
    /// Returns true when the app is being replaced and must not start.
    /// </summary>
    public bool TryApplyPendingAtStartup()
    {
        try
        {
            if (!_channel.IsSupported || _channel.PendingVersion is null) return false;
            _channel.ApplyAndRestart();
            return true;
        }
        catch (Exception ex)
        {
            _log(ex);
            return false;
        }
    }

    /// <summary>One check (and download when needed). Returns how long to wait before the next check.</summary>
    public async Task<TimeSpan> CheckOnceAsync(CancellationToken cancellation)
    {
        if (!_channel.IsSupported || ReadyVersion is not null) return Regular;
        try
        {
            var version = await _channel.CheckAsync(cancellation);
            if (version is null) return Regular;

            Downloading?.Invoke(version);
            await _channel.DownloadAsync(new Progress<int>(), cancellation);

            ReadyVersion = version;
            Ready?.Invoke(version);
            return Regular;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Offline, GitHub unreachable, broken download: keep working and try again later.
            _log(ex);
            return AfterFailure;
        }
    }

    /// <summary>Checks until an update is ready or the app exits.</summary>
    public async Task RunAsync(CancellationToken cancellation)
    {
        if (!_channel.IsSupported) return;
        var wait = FirstCheck;
        try
        {
            while (ReadyVersion is null)
            {
                await _delay(wait, cancellation);
                wait = await CheckOnceAsync(cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // App is closing.
        }
    }

    /// <summary>Installs the ready version and restarts.</summary>
    public void ApplyNow() => _channel.ApplyAndRestart();
}
