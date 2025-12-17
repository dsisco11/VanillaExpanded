using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Gui;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.ModSystems;

/// <summary>
/// Client-side mod system that registers the Alloy Calculator dialog and hotkey.
/// Also auto-opens the calculator when a firepit/crucible dialog is opened.
/// </summary>
public sealed class AlloyCalculatorModSystem : ModSystem
{
    #region Constants
    private const string HotkeyCode = "ve.alloycalculator";
    private const GlKeys DefaultHotkey = GlKeys.K;
    #endregion

    #region Fields
    private ICoreClientAPI? capi;
    private GuiDialogAlloyCalculator? dialog;
    private bool autoOpened;
    #endregion

    #region ModSystem Lifecycle
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        RegisterHotkey();
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

        dialog ??= new GuiDialogAlloyCalculator(capi);
        if (!dialog.IsOpened())
        {
            dialog.SetAlignment(EnumDialogArea.RightMiddle);
            dialog.TryOpen();
            autoOpened = true;
        }
    }

    private void OnFirepitDialogClosed(GuiDialogBlockEntityFirepit firepitDialog)
    {
        if (capi is null) return;
        if (dialog is null) return;

        EFirepitKind kind = GetFirepitKind(capi, firepitDialog);
        if (kind != EFirepitKind.Crucible) return;

        if (autoOpened && dialog?.IsOpened() == true)
        {
            dialog.TryClose();
            autoOpened = false;
        }
    }
    #endregion

    #region Hotkey Registration
    private void RegisterHotkey()
    {
        if (capi is null) return;

        capi.Input.RegisterHotKey(
            HotkeyCode,
            Lang.Get($"{Mod.Info.ModID}:gui-alloycalculator-title"),
            DefaultHotkey,
            HotkeyType.GUIOrOtherControls,
            altPressed: true
        );

        capi.Input.SetHotKeyHandler(HotkeyCode, OnHotkeyPressed);
    }

    private bool OnHotkeyPressed(KeyCombination _)
    {
        if (capi is null) return false;

        dialog ??= new GuiDialogAlloyCalculator(capi);

        if (dialog.IsOpened())
        {
            dialog.TryClose();
        }
        else
        {
            dialog.TryOpen();
        }

        return true;
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