namespace Mo.App.Services.Updates;

/// <summary>Where updates come from. One implementation per distribution channel (GitHub now, Store later).</summary>
public interface IUpdateChannel
{
    /// <summary>False when the app was not installed by this channel (for example a debug build), so nothing is checked.</summary>
    bool IsSupported { get; }

    /// <summary>Version that was downloaded earlier and still waits to be installed, if any.</summary>
    string? PendingVersion { get; }

    /// <summary>Returns the newer version that is available, or null when the app is up to date.</summary>
    Task<string?> CheckAsync(CancellationToken cancellation);

    /// <summary>Downloads the version found by the last <see cref="CheckAsync"/>. Progress is 0–100.</summary>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellation);

    /// <summary>Installs the downloaded version and restarts the app. Does not return when it succeeds.</summary>
    void ApplyAndRestart();
}
