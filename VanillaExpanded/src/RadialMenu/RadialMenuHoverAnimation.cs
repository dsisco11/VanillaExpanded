using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Retains independent hover transitions by stable entry identifier across layout updates.</summary>
internal sealed class RadialMenuHoverAnimation
{
    private readonly Dictionary<string, float> progress = new(StringComparer.Ordinal);

    #region Lifetime
    /// <summary>Starts a new menu interaction without carrying a prior hover animation into it.</summary>
    internal void Reset() => progress.Clear();

    /// <summary>Removes state for entries no longer present after a layout change.</summary>
    internal void Retain(RadialMenuLayout layout)
    {
        var current = new HashSet<string>(layout.WedgeIds, StringComparer.Ordinal);
        foreach (string id in new List<string>(progress.Keys))
            if (!current.Contains(id)) progress.Remove(id);
    }
    #endregion

    #region Animation
    /// <summary>Advances each wedge toward its hover target without depending on frame rate.</summary>
    internal void Advance(RadialMenuLayout layout, string? hoveredId, float elapsedSeconds)
    {
        float step = Math.Clamp(elapsedSeconds, 0f, 1f) / RadialMenuWedgeStyle.HoverDurationSeconds;
        foreach (string id in layout.WedgeIds)
        {
            progress.TryGetValue(id, out float current);
            float target = id == hoveredId ? 1f : 0f;
            progress[id] = Math.Clamp(current + Math.Clamp(target - current, -step, step), 0f, 1f);
        }
    }

    /// <summary>Gets a smooth visual fraction for one entry while preserving its linear transition state.</summary>
    internal float VisualProgress(string id)
    {
        progress.TryGetValue(id, out float current);
        return current * current * (3f - 2f * current);
    }
    #endregion
}
