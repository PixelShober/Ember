namespace Ember.App;

/// <summary>
/// One Ember per Windows session. The first instance owns a named mutex; a second start signals
/// an event so the first one comes to the front, and then exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    const string MutexName = @"Local\Ember.SingleInstance";
    const string EventName = @"Local\Ember.Activate";

    readonly Mutex _mutex;
    readonly EventWaitHandle _event;
    readonly RegisteredWaitHandle _registration;

    SingleInstance(Mutex mutex, EventWaitHandle ev)
    {
        _mutex = mutex;
        _event = ev;
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => Activated?.Invoke(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public event Action? Activated;

    /// <summary>Returns null when another instance runs (it has been asked to come to the front).</summary>
    public static SingleInstance? Acquire(int waitSeconds)
    {
        var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(waitSeconds));
        }
        catch (AbandonedMutexException)
        {
            owned = true;   // previous instance crashed
        }

        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (!owned)
        {
            ev.Set();
            ev.Dispose();
            mutex.Dispose();
            return null;
        }
        return new SingleInstance(mutex, ev);
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
        _event.Dispose();
    }
}
