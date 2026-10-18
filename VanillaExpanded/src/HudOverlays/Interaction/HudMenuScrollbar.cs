using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Keeps native scrollbar handles clipped to the popup even below the engine's minimum handle height.</summary>
internal sealed class HudMenuScrollbar : GuiElementCompactScrollbar
{
    private readonly Func<ElementBounds?> clip;
    #region Public API
    /// <summary>Uses a continuous native scroll offset and borrows the current visible popup bounds.</summary>
    public HudMenuScrollbar(ICoreClientAPI api, Action<float> changed, Func<ElementBounds?> clip)
        : base(api, changed, ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent())
    {
        this.clip = clip;
        zOffset = 300;
    }
    /// <summary>Clips the native handle because the list renders scrollbars after popping its own content scissor.</summary>
    public override void RenderInteractiveElements(float deltaTime)
    {
        var bounds = clip();
        if (bounds == null) return;
        api.Render.PushScissor(bounds);
        try { base.RenderInteractiveElements(deltaTime); }
        finally { api.Render.PopScissor(); }
    }
    /// <summary>Retains a nonnegative scroll range when a tiny viewport is shorter than the native minimum handle.</summary>
    public override void RecomposeHandle()
    {
        Bounds.CalcWorldBounds();
        float available = (float)Math.Max(0, Bounds.InnerHeight);
        // Native height updates clamp the position before this virtual composition hook. Repair both values
        // and publish the corrected offset so partial-row viewports can still reach the final entry.
        float maximum = totalHeight > visibleHeight && currentHandleHeight >= available ? available / 2 : available;
        currentHandleHeight = Math.Clamp(currentHandleHeight, 0, maximum);
        currentHandlePosition = Math.Clamp(currentHandlePosition, 0, Math.Max(0, available - currentHandleHeight));
        TriggerChanged();
        base.RecomposeHandle();
    }
    #endregion
}
