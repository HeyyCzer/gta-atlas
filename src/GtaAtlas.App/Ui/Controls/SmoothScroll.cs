using System.Diagnostics;
using GtaAtlas.App.Motion;

namespace GtaAtlas.App.Ui.Controls;

/// <summary>
/// Drives a <see cref="ScrollAnimator"/> from the display refresh (<see cref="FrameClock"/>), clamps to the
/// owner's scroll range and paints the owner synchronously each frame so motion tracks vsync.
/// </summary>
internal sealed class SmoothScroll : IDisposable
{
    private readonly ScrollAnimator _animator = new();
    private readonly Stopwatch _clock = new();
    private readonly Control _owner;
    private readonly Func<int> _max;
    private readonly Action _onFrame;
    private bool _running;

    public SmoothScroll(Control owner, Func<int> max)
    {
        _owner = owner;
        _max = max;
        _onFrame = OnFrame;
    }

    /// <summary>Raised whenever <see cref="Value"/> moves; invalidate here (the owner is painted right after).</summary>
    public event EventHandler? Changed;

    public int Value => (int)Math.Round(_animator.Position);

    /// <summary>Where the animation is heading; use it for "is this visible" decisions while moving.</summary>
    public int Target => (int)Math.Round(_animator.Target);

    /// <summary>Scrolls by pixels relative to the current target (positive = down).</summary>
    public void By(int delta) => To(_animator.Target + delta);

    public void To(double target)
    {
        _animator.ScrollTo(Math.Clamp(target, 0, Math.Max(0, _max())));
        if (!_animator.IsAnimating || _running)
            return;
        _running = true;
        _clock.Restart();
        FrameClock.Subscribe(_onFrame);
    }

    public void Jump(double value)
    {
        Stop();
        _animator.Jump(Math.Clamp(value, 0, Math.Max(0, _max())));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-applies the range after a resize or content change.</summary>
    public void Clamp()
    {
        var max = Math.Max(0, _max());
        if (_animator.Target > max || _animator.Position > max)
            Jump(Math.Min(_animator.Position, max));
    }

    /// <summary>Scrolls just enough to show [top, bottom) in a viewport of the given height.</summary>
    public void Reveal(int top, int bottom, int viewport)
    {
        if (top < Target)
            To(top);
        else if (bottom > Target + viewport)
            To(bottom - viewport);
    }

    private void OnFrame()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        _clock.Restart();
        if (_owner.IsDisposed || !_animator.Step(elapsed))
        {
            Stop();
            return;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        _owner.Update();
    }

    private void Stop()
    {
        if (!_running)
            return;
        _running = false;
        FrameClock.Unsubscribe(_onFrame);
    }

    public void Dispose() => Stop();
}
