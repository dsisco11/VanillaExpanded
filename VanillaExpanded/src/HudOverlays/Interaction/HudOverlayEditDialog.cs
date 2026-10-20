using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Owns native free-cursor mode for explicitly requested HUD editing without an invisible input overlay.</summary>
internal sealed class HudOverlayEditDialog : GuiDialog
{
    #region Public API
    /// <summary>Creates an empty native dialog whose lifetime controls cursor release.</summary>
    public HudOverlayEditDialog(ICoreClientAPI api) : base(api) { }
    /// <summary>Leaves key registration and toggling with the owning input integration.</summary>
    public override string ToggleKeyCombinationCode => null!;
    /// <summary>Keeps focus with the ordinary foreground menu or HUD interaction owner.</summary>
    public override bool Focusable => false;
    /// <summary>Requests the native free cursor while explicit editing remains open.</summary>
    public override bool PrefersUngrabbedMouse => true;
    /// <summary>Prevents temporary mouse recapture while the edit toggle is active.</summary>
    public override bool DisableMouseGrab => true;
    /// <summary>Releases native registration whenever explicit editing closes.</summary>
    public override bool UnregisterOnClose => true;
    /// <summary>Leaves pointer routing to actual indicator hit targets.</summary>
    public override bool ShouldReceiveMouseEvents() => false;
    /// <summary>Receives Escape without requiring focus or capturing ordinary gameplay keys.</summary>
    public override bool ShouldReceiveKeyboardEvents() => IsOpened();
    /// <summary>Closes explicit editing on Escape after higher-priority HUD interactions can cancel first.</summary>
    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode == (int)GlKeys.Escape && TryClose()) args.Handled = true;
    }
    #endregion
}
