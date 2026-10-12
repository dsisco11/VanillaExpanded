using System;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Scopes main-thread HUD draw opacity across foreground layers, text, items, and backdrop collection.</summary>
internal readonly struct HudOverlayDrawOpacity : IDisposable
{
    private readonly float previous;
    /// <summary>Gets the current main-thread presentation opacity.</summary>
    public static float Current { get; private set; } = 1;
    /// <summary>Gets straight-alpha white tint, avoiding allocations outside transitions.</summary>
    public static Vec4f? Tint => Current == 1 ? null : new(1, 1, 1, Current);
    /// <summary>Gets premultiplied white tint for cached text and background textures.</summary>
    public static Vec4f? PremultipliedTint => Current == 1 ? null : new(Current, Current, Current, Current);
    /// <summary>Gets a packed white tint for native item rendering.</summary>
    public static int ItemColor => unchecked(((int)Math.Round(Current * 255) << 24) | 0xFFFFFF);

    #region Public API
    /// <summary>Captures the previous scope and applies this overlay's transition opacity.</summary>
    public HudOverlayDrawOpacity(object overlay)
    {
        previous = Current;
        Current = overlay is IHudOverlayVisibilityTransition animated ? animated.Visibility.Opacity : 1;
    }
    /// <summary>Multiplies an existing straight-alpha tint without altering its owning presentation.</summary>
    public static Vec4f Multiply(Vec4f tint) => Current == 1 ? tint : new(tint.X, tint.Y, tint.Z, tint.W * Current);
    /// <summary>Restores the caller's opacity even after an indicator draw fails.</summary>
    public void Dispose() => Current = previous;
    #endregion
}
