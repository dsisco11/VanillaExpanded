using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Gui;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.ModSystems;

/// <summary>
/// Client-side mod system that auto-opens the Alloy Calculator when a crucible dialog is opened.
/// </summary>
public sealed class AlloyCalculatorModSystem : ModSystem
{
    #region Fields
    private ICoreClientAPI? capi;
    private GuiDialogAlloyCalculator? dialog;
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
        dialog?.Dispose();
        dialog = null;
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

        // Dispose old dialog if it exists (position may have changed)
        dialog?.Dispose();
        dialog = new GuiDialogAlloyCalculator(capi, firepitDialog.BlockEntityPosition);
        
        if (!dialog.IsOpened())
        {
            dialog.TryOpen();
        }
    }

    private void OnFirepitDialogClosed(GuiDialogBlockEntityFirepit firepitDialog)
    {
        if (capi is null) return;
        if (dialog is null) return;

        EFirepitKind kind = GetFirepitKind(capi, firepitDialog);
        if (kind != EFirepitKind.Crucible) return;

        if (dialog.IsOpened())
        {
            dialog.TryClose();
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