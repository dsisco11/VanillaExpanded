using System;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Animates prepared content and defers its release until a visibility exit completes.</summary>
internal sealed class HudOverlayVisibilityTransition(bool flash = false, Func<long>? clockMs = null)
{
    private readonly Func<long> clock = clockMs ?? (() => Environment.TickCount64);
    private bool enabled, target, retained;
    private long started;
    private float from;
    private Action? release;
    private int Duration => flash ? 1000 : 250;

    #region Public API
    /// <summary>Gets whether prepared artwork remains available, completing expired exits on the owning thread.</summary>
    public bool Renderable { get { Complete(); return retained; } }
    /// <summary>Gets whether retained artwork is leaving rather than representing a current sample.</summary>
    public bool Exiting { get { Complete(); return retained && !target; } }
    /// <summary>Gets whether a visibility effect is still running.</summary>
    public bool Animating => enabled && retained && clock() - started < Duration;
    /// <summary>Gets the current fade or two-pulse flashing envelope.</summary>
    public float Opacity
    {
        get
        {
            Complete();
            if (!enabled) return 1;
            if (!retained) return 0;
            double t = Math.Clamp((clock() - started) / (double)Duration, 0, 1);
            double smooth = t * t * (3 - 2 * t);
            double value = from + ((target ? 1 : 0) - from) * smooth;
            // Two soft flashes finish at full opacity on entry and zero on exit; no endless transition blinking.
            if (flash && t < 1) value *= .25 + .75 * Math.Pow(Math.Cos(2 * Math.PI * t), 2);
            return (float)value;
        }
    }
    /// <summary>Enables runtime animation; isolated presentation owners retain immediate cleanup semantics.</summary>
    public void Enable() => enabled = true;
    /// <summary>Starts or reverses an entry without restarting a stable visible indicator.</summary>
    public void Show()
    {
        if (target && retained) return;
        // Preparation may already have replaced the old content; an expired exit must not release it.
        release = null;
        float current = retained ? Opacity : 0;
        from = current;
        started = clock();
        release = null;
        retained = target = true;
    }
    /// <summary>Retains prepared resources for an exit, or releases immediately when animation is not active.</summary>
    public void Hide(Action releaseContent)
    {
        if (!enabled || !retained) { releaseContent(); return; }
        if (!target) { Complete(); return; }
        from = Opacity;
        target = false;
        started = clock();
        release = releaseContent;
    }
    /// <summary>Cancels animation and releases its callback reference during immediate owner cleanup.</summary>
    public void Reset()
    {
        target = retained = false;
        release = null;
        from = 0;
    }
    #endregion

    #region Private
    /// <summary>Runs the owning release callback once, after clearing transition state to permit reentrant reset.</summary>
    private void Complete()
    {
        if (!enabled || target || !retained || clock() - started < Duration) return;
        var action = release;
        Reset();
        action?.Invoke();
    }
    #endregion
}
