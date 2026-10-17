using System;
using System.Collections.Generic;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Retains group preferences for one session and shares effective anchor selection with scheduling.</summary>
internal sealed class HudOverlayGroupPositions(IGuiAPI gui)
{
    private readonly Dictionary<string, HudOverlayGroupMovement> groups = new(StringComparer.Ordinal);

    #region Public API
    /// <summary>Restores a group's preference once, preserving it across configuration and geometry changes.</summary>
    public HudOverlayGroupMovement Get(string id)
    {
        if (!groups.TryGetValue(id, out var movement))
            groups.Add(id, movement = new HudOverlayGroupMovement(gui, id));
        return movement;
    }

    /// <summary>Uses screen availability for saved or preview origins instead of the former native anchor.</summary>
    public string TargetId(HudOverlayGroup group) => Get(group.Id).Position.HasValue
        ? HudOverlayAnchorContext.ScreenTargetId : group.Placement.TargetId;

    /// <summary>Discards every transient drag before releasing the session's borrowed GUI services.</summary>
    public void Cancel()
    {
        foreach (var movement in groups.Values) movement.Cancel();
    }
    #endregion
}
