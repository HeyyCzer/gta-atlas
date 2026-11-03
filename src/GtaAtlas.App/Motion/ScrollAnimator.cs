namespace GtaAtlas.App.Motion;

/// <summary>
/// Eases a scroll position toward a target. Exponential approach scaled by elapsed time, so the motion
/// feels the same at any frame rate; new wheel input moves the target, so fast spins accumulate.
/// </summary>
public sealed class ScrollAnimator
{
    /// <summary>Fraction of the remaining distance covered per 16 ms frame.</summary>
    public double Smoothing { get; init; } = 0.24;

    public double Position { get; private set; }

    public double Target { get; private set; }

    public bool IsAnimating => Position != Target;

    /// <summary>Moves instantly (scrollbar drag, new content).</summary>
    public void Jump(double value) => Position = Target = value;

    public void ScrollTo(double target) => Target = target;

    /// <summary>Advances the animation; returns false when already at rest.</summary>
    public bool Step(double elapsedMs)
    {
        if (!IsAnimating)
            return false;

        var k = 1 - Math.Pow(1 - Smoothing, Math.Max(0, elapsedMs) / 16.0);
        Position += (Target - Position) * k;
        if (Math.Abs(Target - Position) < 0.5)
            Position = Target;
        return true;
    }
}
