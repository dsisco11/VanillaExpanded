using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Registers the configurable HUD editing toggle and owns its native cursor dialog.</summary>
internal sealed class HudOverlayEditInput : IDisposable
{
    internal const string HotkeyCode = "ve.editHud";
    private readonly ICoreClientAPI api;
    private readonly HudOverlayEditDialog dialog;
    private readonly ActionConsumable<KeyCombination> handler;
    private bool disposed;

    #region Public API
    /// <summary>Registers F9 in Controls and closes explicit editing when leaving the world.</summary>
    public HudOverlayEditInput(ICoreClientAPI api)
    {
        this.api = api;
        dialog = new HudOverlayEditDialog(api);
        api.Input.RegisterHotKey(HotkeyCode, Lang.GetL(Lang.CurrentLocale ?? Lang.DefaultLocale,
            Constants.ModId + ":hotkey-edit-hud"), GlKeys.F9, HotkeyType.GUIOrOtherControls);
        handler = _ => Toggle();
        api.Input.SetHotKeyHandler(HotkeyCode, handler);
        api.Event.LeaveWorld += Close;
    }

    /// <summary>Releases cursor ownership immediately when HUD overlays are globally disabled.</summary>
    public void Synchronize()
    {
        if (!VanillaExpandedModSystem.Config.EnableHudOverlays) Close();
    }

    /// <summary>Detaches input and world callbacks before disposing the cursor dialog.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.LeaveWorld -= Close;
        var hotkey = api.Input.GetHotKeyByCode(HotkeyCode);
        if (hotkey?.Handler == handler) hotkey.Handler = null;
        Close();
        dialog.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Toggles cursor ownership only in a ready world with enabled, visible HUD overlays.</summary>
    private bool Toggle()
    {
        if (disposed) return false;
        if (dialog.IsOpened()) { Close(); return true; }
        if (!VanillaExpandedModSystem.Config.EnableHudOverlays || api.HideGuis || api.World?.Player?.Entity == null) return false;
        return dialog.TryOpen(false);
    }

    /// <summary>Closes the native edit dialog without changing any layout or feature settings.</summary>
    private void Close() => dialog.TryClose();
    #endregion
}
