using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Borrows cached temporal storm textures and draws one compact icon with a shared radial-gradient backdrop.</summary>
internal sealed class TemporalStormPresentation : IDisposable
{
    private readonly System.Func<HudOverlayPreparationContext, LoadedTexture[]> load;
    private readonly System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private LoadedTexture[]? icons;
    private IHudOverlayIconBackdrop? backdrop;
    private HudOverlayPreparationContext? context;
    private TemporalStormStage? stage;
    private bool pulsing;
    private readonly TemporalStormPulse pulse = new();
    private readonly Vec4f tint = new(1, 1, 1, 1);
    /// <summary>Controls visibility and retained resources during transitions.</summary>
    public HudOverlayVisibilityTransition Visibility { get; } = new(flash: true);
    public SizeF Size { get; private set; }

    #region Public API
    #region Content and rendering
    /// <summary>Uses native cached texture loading, with an injectable loader for headless checks.</summary>
    public TemporalStormPresentation(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null,
        System.Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
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
        if (icons == null || !ReferenceEquals(context?.Api, preparation.Api)) icons = load(preparation);
        if (backdrop == null || !ReferenceEquals(context?.Api, preparation.Api)) backdrop = loadBackdrop(preparation);
        context = preparation;
        Size = new SizeF(HudOverlaySizes.Standard, HudOverlaySizes.Standard);
        Visibility.Show();
    }
    /// <summary>Draws prepared artwork without storm sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, float deltaTime)
    {
        if (!stage.HasValue || icons == null || context == null) return;
        double pixels = HudOverlaySizes.Standard * context.GuiScale;
        // Imminent and waning storms pulse; the shared backdrop remains steady.
        tint.W = pulsing ? pulse.Advance(deltaTime) : 1;
        renderer.RenderTexture(icons[(int)stage.Value].TextureId, bounds.renderX,
            bounds.renderY + (bounds.OuterHeight - pixels) / 2, pixels, pixels, 50,
            pulsing && !Visibility.Animating ? HudOverlayDrawOpacity.Multiply(tint) : HudOverlayDrawOpacity.Tint);
    }
    #endregion
    /// <summary>Queues the prepared icon circle separately from foreground drawing.</summary>
    public void CollectBackdrops(IRenderAPI renderer, ElementBounds bounds)
    {
        if (!stage.HasValue || icons == null || context == null) return;
        double pixels = HudOverlaySizes.Standard * context.GuiScale;
        double y = bounds.renderY + (bounds.OuterHeight - pixels) / 2;
        backdrop?.Queue(renderer, bounds.renderX, y, context.GuiScale);
    }
    #region Lifetime
    /// <summary>Retains prepared content until the visibility exit finishes.</summary>
    public void Hide() => Visibility.Hide(Reset);
    /// <summary>Drops session bindings without disposing engine-owned cached textures.</summary>
    public void Reset()
    {
        Visibility.Reset();
        icons = null;
        backdrop = null;
        context = null;
        stage = null;
        pulsing = false;
        pulse.Reset();
        Size = SizeF.Empty;
    }
    /// <summary>Releases borrowed bindings on final removal.</summary>
    public void Dispose() => Reset();
    #endregion
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
