using System.Collections.Generic;
using System.Linq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Gui;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.ModSystems;

/// <summary>
/// Client-side mod system that auto-opens the Alloy Calculator when a crucible dialog is opened.
/// </summary>
public sealed class AlloyCalculatorModSystem : ModSystem
{
    #region Fields
    private ICoreClientAPI? capi;
    private readonly Dictionary<BlockPos, GuiDialogAlloyCalculator> openDialogs = [];
    #endregion

    #region ModSystem Lifecycle
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        RegisterFirepitEvents();
    }

    public override void Dispose()
    {
        UnregisterFirepitEvents();
        foreach (var dialog in openDialogs.Values)
        {
            dialog.Dispose();
        }
        openDialogs.Clear();
        capi = null;
        base.Dispose();
    }
    #endregion

    #region Firepit Dialog Events
    private void RegisterFirepitEvents()
    {
        FirepitGuiPatch.FirepitDialogOpened += OnFirepitDialogOpened;
        FirepitGuiPatch.FirepitDialogClosed += OnFirepitDialogClosed;
    }

    private void UnregisterFirepitEvents()
    {
        FirepitGuiPatch.FirepitDialogOpened -= OnFirepitDialogOpened;
        FirepitGuiPatch.FirepitDialogClosed -= OnFirepitDialogClosed;
    }

    private void OnFirepitDialogOpened(GuiDialogBlockEntityFirepit firepitDialog)
    {
        if (capi is null) return;
        
        EFirepitKind kind = GetFirepitKind(capi, firepitDialog);
        if (kind != EFirepitKind.Crucible) return;

        var pos = firepitDialog.BlockEntityPosition;
        
        // Check if dialog already exists for this position
        if (openDialogs.TryGetValue(pos, out var existingDialog) && existingDialog.IsOpened())
        {
            return;
        }

        var dialog = new GuiDialogAlloyCalculator(capi, pos);
        if (dialog.TryOpen())
        {
            openDialogs[pos] = dialog;
        }
    }

    private void OnFirepitDialogClosed(GuiDialogBlockEntityFirepit firepitDialog)
    {
        if (capi is null) return;

        EFirepitKind kind = GetFirepitKind(capi, firepitDialog);
        if (kind != EFirepitKind.Crucible) return;

        var pos = firepitDialog.BlockEntityPosition;
        
        if (openDialogs.TryGetValue(pos, out var dialog))
        {
            dialog.TryClose();
            dialog.Dispose();
            openDialogs.Remove(pos);
        }
    }
    #endregion

    #region Private Methods
    private static EFirepitKind GetFirepitKind(ICoreAPI api, GuiDialogBlockEntityFirepit dialog)
    {
        BlockEntityFirepit firepit = api.World.BlockAccessor.GetBlockEntity<BlockEntityFirepit>(dialog.BlockEntityPosition);
        if (firepit is null)
        {
            return EFirepitKind.None;
        }

        var inputItem = firepit.inputStack?.Collectible;
        if (inputItem is null)
        {
            return EFirepitKind.None;
        }

        string itemCode = inputItem.FirstCodePart(0);
        if (itemCode == "crucible")
        {
            return EFirepitKind.Crucible;
        }
        else if (itemCode == "claypot")
        {
            return EFirepitKind.CookingPot;
        }
        else
        {
            return EFirepitKind.None;
        }
    }
    #endregion
}

enum EFirepitKind
{
    None,
    Crucible,
    CookingPot
}