using VanillaExpanded.Gui;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.ModSystems;

/// <summary>
/// Client-side mod system that registers the Alloy Calculator dialog and hotkey.
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
    #endregion

    #region ModSystem Lifecycle
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        RegisterHotkey();
    }

    public override void Dispose()
    {
        dialog?.Dispose();
        dialog = null;
        capi = null;
        base.Dispose();
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
}
