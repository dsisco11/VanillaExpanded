using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Synchronizes this layer's native tab during composition, including the dialog's initial construction.</summary>
internal static class EcosystemTabState
{
    private static readonly FieldInfo names = typeof(GuiDialogWorldMap).GetField("tabnames", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("World map tab group names changed.");
    private static readonly FieldInfo tabs = typeof(GuiDialogWorldMap).GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("World map tab state changed.");

    #region Public API
    /// <summary>Copies only the ecosystem activation state without toggling another tab or invoking click handlers.</summary>
    internal static void Synchronize(GuiDialogWorldMap dialog, string group, bool active)
    {
        if (names.GetValue(dialog) is not List<string> groups || tabs.GetValue(dialog) is not List<GuiTab> states)
            throw new NotSupportedException("World map tab collections changed.");
        int index = groups.IndexOf(group);
        if (index < 0) return;
        if (index >= states.Count) throw new NotSupportedException("World map tab group/state alignment changed.");
        // The first full-map composer is built before SingleComposer exposes MapLayers, so vanilla defaults tabs to active.
        // Its widget shares these GuiTab objects; set the real layer state before native extra-panel activation runs.
        states[index].Active = active;
    }
    #endregion
}
