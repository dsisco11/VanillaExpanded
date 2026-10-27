using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Identifies native HUD font and contrast changes for controller and view preparation.</summary>
internal static class FontRevision
{
    #region Public API
    /// <summary>Hashes the native reusable font settings without creating GUI resources.</summary>
    public static long Calculate()
    {
        var hash = new HashCode();
        hash.Add(GuiStyle.StandardFontName);
        hash.Add(GuiStyle.SmallFontSize);
        foreach (double component in GuiStyle.DialogDefaultTextColor) hash.Add(component);
        return hash.ToHashCode();
    }
    #endregion
}
