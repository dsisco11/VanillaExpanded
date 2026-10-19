using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Owns the native map extra composer and identifies the exact full-map rendering surface.</summary>
internal sealed class EcosystemMapControls : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly EcosystemMapSelection selection;
    private readonly string key;
    private GuiDialogWorldMap? dialog;
    private GuiComposer? composer, fullMapComposer;

    #region Public API
    #region Construction and composition
    /// <summary>Binds control ownership to one layer group and session selection model.</summary>
    internal EcosystemMapControls(ICoreClientAPI api, EcosystemMapSelection selection, string group)
    { this.api = api; this.selection = selection; key = "worldmap-layer-" + group; }

    /// <summary>Replaces the previous extra composer using the same native panel mechanism as prospecting.</summary>
    internal void Compose(GuiDialogWorldMap target, GuiComposer mapComposer)
    {
        Dispose(); dialog = target; fullMapComposer = mapComposer;
        var bounds = ElementStdBounds.AutosizedMainDialog.WithFixedPosition(
            (mapComposer.Bounds.renderX + mapComposer.Bounds.OuterWidth) / RuntimeEnv.GUIScale + 10,
            mapComposer.Bounds.renderY / RuntimeEnv.GUIScale).WithAlignment(EnumDialogArea.None);
        var background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        composer = api.Gui.CreateCompo(key, bounds);
        try
        {
            var builder = composer.AddShadedDialogBG(background, false).BeginChildElements(background);
            if (selection.Codes.Length != 0)
                builder.AddDropDown(selection.Codes, selection.Names, selection.SelectedIndex, selection.Select,
                    ElementBounds.Fixed(0, 0, 220, 35));
            builder.EndChildElements().Compose();
            target.Composers[key] = composer;
            composer.Enabled = false;
        }
        catch
        {
            // A partially composed panel still owns any resources allocated before composition failed.
            MapComposerOwnership.Detach(target.Composers, key, composer);
            composer.Dispose(); composer = null; fullMapComposer = null; dialog = null;
            throw;
        }
    }
    #endregion

    #region Eligibility and disposal
    /// <summary>Refreshes panel eligibility independently of which map element happens to render.</summary>
    internal bool Refresh(bool active)
    {
        bool eligible = active && dialog?.IsOpened() == true && dialog.DialogType == EnumDialogType.Dialog
            && ReferenceEquals(dialog.SingleComposer, fullMapComposer);
        if (composer != null) composer.Enabled = eligible;
        return eligible;
    }
    /// <summary>Requires the exact composed full-map element, rejecting stale composers and minimap surfaces.</summary>
    internal bool IsSurface(GuiElementMap map) => MatchesSurface(fullMapComposer?.GetElement("mapElem"), map);
    /// <summary>Compares surface identity without inferring it from the manager's current dialog mode.</summary>
    internal static bool MatchesSurface(object? fullMap, object? candidate) => fullMap != null && ReferenceEquals(fullMap, candidate);

    /// <summary>Detaches the owned panel before disposal, preserving any replacement owned by another component.</summary>
    public void Dispose()
    {
        if (composer != null) composer.Enabled = false;
        if (composer != null && dialog != null) MapComposerOwnership.Detach(dialog.Composers, key, composer);
        if (composer?.Composed == true) composer.Dispose();
        composer = null; fullMapComposer = null; dialog = null;
    }
    #endregion
    #endregion
}
