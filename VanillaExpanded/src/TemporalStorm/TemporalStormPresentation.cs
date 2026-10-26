using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Borrows cached temporal storm textures and draws one compact icon with a shared radial-gradient backdrop.</summary>
internal sealed class TemporalStormPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private TemporalStormStage? stage;
    private bool pulsing;
    private readonly TemporalStormPulse pulse = new();
    private readonly Vec4f tint = new(1, 1, 1, 1);

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public TemporalStormPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null,
        HudIndicatorTextPresentation? textStyle = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, textStyle, flash: true)
    {
        this.load = load ?? LoadIcons;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Stores the native storm stage and updates urgency without restarting an existing pulse.</summary>
    public void SetContent(TemporalStormStage value, double remainingDays, bool waning = false)
    {
        bool nextPulsing = value == TemporalStormStage.Imminent || (value == TemporalStormStage.Active && waning);
        if (stage != value || pulsing != nextPulsing) pulse.Reset();
        stage = value;
        pulsing = nextPulsing;
        pulse.SetRemainingDays(remainingDays);
    }
    /// <summary>Loads borrowed textures once per client API and reserves a fixed HudOverlaySizes.Standard-unit icon slot.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (!stage.HasValue) return;
        if (IsTextStyle)
        {
            TryPrepareText(preparation, "storm", stage.Value.ToString().ToUpperInvariant());
            return;
        }
        if (icons == null || !ReferenceEquals(Context?.Api, preparation.Api)) icons = load(preparation);
        if (backdrop == null || !ReferenceEquals(Context?.Api, preparation.Api)) backdrop = loadBackdrop(preparation);
        Context = preparation;
        Size = new SizeF(HudOverlaySizes.Standard, HudOverlaySizes.Standard);
        Visibility.Show();
    }
    /// <summary>Draws prepared artwork without storm sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, float deltaTime)
    {
        if (!stage.HasValue || Context == null) return;
        if (IsTextStyle)
        {
            float alpha = pulsing ? pulse.Advance(deltaTime) : 1;
            TryDrawText(renderer, bounds, Visibility.Animating ? 1 : alpha);
            return;
        }
        if (icons == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        // Imminent and waning storms pulse; the shared backdrop remains steady.
        tint.W = pulsing ? pulse.Advance(deltaTime) : 1;
        renderer.RenderTexture(icons[(int)stage.Value].TextureId, bounds.renderX,
            bounds.renderY + (bounds.OuterHeight - pixels) / 2, pixels, pixels, 50,
            pulsing && !Visibility.Animating ? DrawOpacity.Multiply(tint) : DrawOpacity.Tint);
    }
    #endregion
    /// <summary>Queues the prepared icon circle separately from foreground drawing.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!stage.HasValue || icons == null || Context == null) return;
        double pixels = HudOverlaySizes.Standard * Context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        backdrop?.Queue(renderer, bounds.renderX, y, Context.GuiScale);
    }
    #endregion
    #region Protected API
    /// <summary>Releases feature artwork without changing sampled content or feature timers.</summary>
    protected override void ReleaseArtwork()
    {
        icons = null; backdrop = null;
    }

    /// <summary>Clears feature contents after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        stage = null; pulsing = false; pulse.Reset();
    }
    #endregion

    #region Private
    /// <summary>Loads transparent temporal storm assets in storm progression order with engine cache ownership.</summary>
    private static LoadedTexture[] LoadIcons(HudOverlayPreparationContext preparation)
    {
        string[] names = ["approaching", "imminent", "active"];
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(preparation.Api);
            preparation.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/temporal-storm/" + names[index] + ".png"), ref texture);
            HudOverlayTextureFiltering.Prepare(texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion

}
