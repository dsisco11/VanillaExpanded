using System;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Owns the namespaced persisted and synchronized sunlight-observation age on player entities.</summary>
internal static class ApproximateTimeState
{
    internal const string AttributePath = "vanillaexpanded:approximateTime";
    internal const string TimeSinceSunObserved = "timeSinceSunObserved";

    #region Public API
    /// <summary>Reads elapsed game hours from a borrowed entity snapshot without writing on clients.</summary>
    public static double Read(Entity entity)
        => entity.WatchedAttributes.GetTreeAttribute(AttributePath)?.GetDouble(TimeSinceSunObserved, 0) ?? 0;

    /// <summary>Accumulates online game hours or resets on sunlight, marking server changes for synchronization.</summary>
    public static void Update(Entity entity, double elapsedHours, bool sunlight)
    {
        if (!double.IsFinite(elapsedHours) || elapsedHours < 0) return;
        var attributes = entity.WatchedAttributes;
        var tree = attributes.GetOrAddTreeAttribute(AttributePath);
        double previous = tree.GetDouble(TimeSinceSunObserved, 0);
        if (!double.IsFinite(previous) || previous < 0) previous = 0;
        double next = sunlight ? 0 : Math.Min(double.MaxValue, previous + elapsedHours);
        if (tree.HasAttribute(TimeSinceSunObserved) && next == tree.GetDouble(TimeSinceSunObserved)) return;
        tree.SetDouble(TimeSinceSunObserved, next);
        attributes.MarkPathDirty(AttributePath);
    }
    #endregion
}
