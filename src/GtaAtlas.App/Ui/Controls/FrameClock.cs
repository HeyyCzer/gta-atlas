using System.Runtime.InteropServices;

namespace GtaAtlas.App.Ui.Controls;

/// <summary>
/// Calls subscribers on the UI thread once per display refresh (60/144/240 Hz alike).
/// A background thread waits on DwmFlush, which returns at each composition (vsync);
/// WinForms timers tick at ~64 Hz with jitter, which judders on high refresh monitors.
/// </summary>
internal static partial class FrameClock
{
    private static readonly List<Action> Subscribers = [];
    private static readonly AutoResetEvent Wake = new(false);
    private static SynchronizationContext? _ui;
    private static Thread? _thread;
    private static int _pending;

    /// <summary>Starts calling <paramref name="onFrame"/> every frame. Call from the UI thread.</summary>
    public static void Subscribe(Action onFrame)
    {
        _ui ??= SynchronizationContext.Current;
        lock (Subscribers)
        {
            if (!Subscribers.Contains(onFrame))
                Subscribers.Add(onFrame);
        }
        if (_thread is null)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "FrameClock", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        Wake.Set();
    }

    public static void Unsubscribe(Action onFrame)
    {
        lock (Subscribers)
            Subscribers.Remove(onFrame);
    }

    private static void Run()
    {
        while (true)
        {
            bool idle;
            lock (Subscribers)
                idle = Subscribers.Count == 0;
            if (idle)
            {
                Wake.WaitOne();
                continue;
            }

            if (DwmFlush() != 0)
                Thread.Sleep(4); // composition off (rare): fall back to ~240 Hz polling

            // Never queue more than one frame: if the UI is busy, frames drop instead of piling up.
            if (Interlocked.Exchange(ref _pending, 1) == 0)
                _ui?.Post(_ => Tick(), null);
        }
    }

    private static void Tick()
    {
        Interlocked.Exchange(ref _pending, 0);
        Action[] snapshot;
        lock (Subscribers)
            snapshot = [.. Subscribers];
        foreach (var s in snapshot)
            s();
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();
}
