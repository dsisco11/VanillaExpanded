using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Creates native popup surfaces while retaining ownership through preparation failures.</summary>
internal sealed class HudMenuPopupFactory
{
    private readonly ICoreClientAPI api;
    #region Public API
    /// <summary>Borrows the current client graphics API.</summary>
    public HudMenuPopupFactory(ICoreClientAPI api) { this.api = api; }
    /// <summary>Prepares a switch composer or single-selection native list.</summary>
    public IHudMenuPopup? Create(HudMenuPage page, IReadOnlyList<HudMenuEntry> entries, Action<string, bool> select,
        PointF pointer, HudMenuEnvironment environment)
    {
        if (page == HudMenuPage.Toggles) return new HudOverlayTogglePopup(api, entries, select, pointer, environment);
        var menu = new HudOverlayListMenu(api, entries.Select(row => row.Code).ToArray(),
            entries.Select(row => row.Label).ToArray(), Math.Max(0, entries.ToList().FindIndex(row => row.Selected)),
            (code, selected) => select(code, selected));
        try
        {
            if (menu.Prepare(pointer, environment.Viewport)) return menu;
            menu.Dispose();
            return null;
        }
        catch { menu.Dispose(); throw; }
    }
    #endregion
}
