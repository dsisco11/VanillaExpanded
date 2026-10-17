using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Samples synchronized player wetness without changing the native temperature simulation.</summary>
internal sealed class PlayerWetnessOverlay : IHudOverlay, IHudOverlayBackdropProvider, IHudOverlayVisibilityTransition
{
    private readonly PlayerWetnessPresentation presentation;
    private ICoreClientAPI? api;
    private bool enabled = true, disposed;

    private bool styleChanged;

    #region Public API
    /// <summary>Retains style invalidation until the next eligible content and layout refresh.</summary>
    public void SetStyle(HudIndicatorStyle style) => styleChanged |= presentation.SetStyle(style);

    #region Lifecycle and configuration
    /// <summary>Gets the last valid nonzero wetness sample.</summary>
    internal float? Wetness { get; private set; }
    /// <summary>Gets the shared visibility transition.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;
    /// <summary>Creates the owned presentation with optional headless dependencies.</summary>
    public PlayerWetnessOverlay(PlayerWetnessPresentation? presentation = null) => this.presentation = presentation ?? new();
    /// <summary>Binds one client world without retaining another world's player sample.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A wetness session is already bound.");
        this.api = api;
    }
    /// <summary>Starts an exit when live configuration disables the indicator.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) HidePresentation();
        return value;
    }
    #endregion
    #region Sampling
    /// <summary>Requires a living player in a game mode that displays survival status.</summary>
    public bool IsApplicable()
    {
        var player = api?.World.Player;
        bool applicable = enabled && player?.Entity?.Alive == true
            && player.WorldData.CurrentGameMode is not (EnumGameMode.Creative or EnumGameMode.Spectator);
        if (!applicable) HidePresentation();
        return applicable;
    }
    /// <summary>Reads the native watched attribute and rejects missing, dry, and nonfinite values.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var attributes = api!.World.Player.Entity.WatchedAttributes;
        float raw = attributes.GetFloat("wetness");
        float? next = attributes.HasAttribute("wetness") && float.IsFinite(raw) && raw > 0 ? Math.Clamp(raw, 0, 1) : null;
        if (next == Wetness && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        Wetness = next;
        if (next.HasValue) presentation.SetContent(next.Value);
        else presentation.Hide();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Prepares only sampled nonzero wetness outside the draw path.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Wetness.HasValue) presentation.Prepare(context);
    }
    /// <summary>Reports the prepared droplet footprint.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Draws cached layers without accessing gameplay state.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime) => presentation.Draw(renderer, bounds, clip);
    /// <summary>Supplies the cached backdrop to the shared batch.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds) => presentation.CollectBackdrops(renderer, bounds);
    /// <summary>Clears sampled eligibility while retaining artwork for an exit.</summary>
    public void HidePresentation()
    {
        Wetness = null;
        presentation.Hide();
    }
    #endregion
    #region Cleanup
    /// <summary>Releases borrowed world state and artwork bindings immediately.</summary>
    public void EndSession()
    {
        api = null; Wetness = null; presentation.Reset();
    }
    /// <summary>Ends ownership exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        presentation.Dispose();
    }
    #endregion
    #endregion
}

