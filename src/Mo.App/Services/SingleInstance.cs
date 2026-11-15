namespace Mo.App.Services;

/// <summary>
/// One running copy per user session. A second launch signals the first one (which then opens Settings) and exits.
/// The GUID keeps the names unique to this project, so other programs called "Mo Converter" are unaffected.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string Id = "MoConverter.App.3b8f6c1e-4d27-4a9b-9c55-7e2a1f0d64b3";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _signal;
    private RegisteredWaitHandle? _wait;
    private bool _owned;

    private SingleInstance(Mutex mutex, bool owned)
    {
        _mutex = mutex;
        _owned = owned;
        _signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + Id + ".activate");
    }

    public bool IsFirst => _owned;

    /// <param name="waitForPrevious">Wait a few seconds for an exiting instance (used when the app restarts itself).</param>
    public static SingleInstance Acquire(bool waitForPrevious)
    {
        var mutex = new Mutex(true, @"Local\" + Id, out var createdNew);
        var owned = createdNew;
        if (!owned && waitForPrevious)
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { owned = true; }
        }
        return new SingleInstance(mutex, owned);
    }

    /// <summary>Asks the running instance to show itself.</summary>
    public void SignalFirst() => _signal.Set();

    /// <summary>Calls <paramref name="onSignal"/> (on a pool thread) whenever another launch signals.</summary>
    public void Listen(Action onSignal) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => onSignal(), null, Timeout.Infinite, executeOnlyOnce: false);

    public void Dispose()
    {
        _wait?.Unregister(null);
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _owned = false;
        }
        _mutex.Dispose();
        _signal.Dispose();
    }
}
