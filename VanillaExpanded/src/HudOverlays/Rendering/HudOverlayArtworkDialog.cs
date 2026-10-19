using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Keeps passive HUD artwork in its native background slot when the menu owner takes focus.</summary>
internal sealed class HudOverlayArtworkDialog : HudElement
{
    private readonly Action<float> draw;

    #region Public API
    /// <summary>Borrows the owner's artwork callback without owning presentations or input state.</summary>
    public HudOverlayArtworkDialog(ICoreClientAPI api, Action<float> draw) : base(api) => this.draw = draw;
    /// <summary>Orders artwork with passive HUDs rather than focused dialogs.</summary>
    public override double DrawOrder => .1;
    /// <summary>Never participates in native focus reordering.</summary>
    public override bool Focusable => false;
    /// <summary>Retires native registration with the owning host.</summary>
    public override bool UnregisterOnClose => true;
    /// <summary>Leaves all pointer routing to the owning host.</summary>
    public override bool ShouldReceiveMouseEvents() => false;
    /// <summary>Leaves keyboard capture to the owning menu and drag controls.</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;
    /// <summary>Draws through the owner's exception-safe native HUD transform.</summary>
    public override void OnRenderGUI(float deltaTime) => draw(deltaTime);
    #endregion
}
