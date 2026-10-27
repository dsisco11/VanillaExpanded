using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.TemporalStorm;

/// <summary>Prepares stable storm content while native elements own artwork and lettering.</summary>
internal sealed class TemporalStormPresentation : IndicatorPresentation
{
    private TemporalStormStage? value;
    private IconReadingContent? content;
    private IHudOverlayIconBackdrop? backdrop;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding>? load;
    private readonly TemporalStormPulse pulse = new();
    private bool pulsing;
    private float alpha = 1;

    #region Public API
    #region Content and effects
    /// <summary>Accepts headless measurement/resource factories while retaining controller data only.</summary>
    public TemporalStormPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
        : base(flash: true, measure: measure, createText: createText)
    {
        this.load = load; this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Updates stage and urgency without restarting a continuing pulse.</summary>
    public void SetContent(TemporalStormStage next, double remainingDays, bool waning = false)
    {
        bool nextPulsing = next == TemporalStormStage.Imminent || (next == TemporalStormStage.Active && waning);
        if (value != next || pulsing != nextPulsing) pulse.Reset();
        value = next; pulsing = nextPulsing; pulse.SetRemainingDays(remainingDays);
    }
    /// <summary>Advances shared visual timing exactly once per rendered feature frame.</summary>
    public void BeginFrame(float deltaTime)
    {
        float next = pulsing ? pulse.Advance(deltaTime) : 1;
        alpha = Visibility.Animating ? 1 : next;
    }
    #endregion
    #region Preparation and composition
    /// <summary>Prepares localized data and canonical metrics before host packing.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (!value.HasValue) return;
        if (Style == HudIndicatorStyle.Text)
        {
            PrepareText(context, "storm", value.Value.ToString().ToUpperInvariant()); return;
        }
        string[] names = ["approaching", "imminent", "active"];

        content = new("textures/hud/temporal-storm/" + names[(int)value.Value] + ".png",
            new(HudOverlaySizes.Standard, HudOverlaySizes.Standard), null, 0);
        Size = content.IconSize;
        backdrop = loadBackdrop(context);
        Visibility.Show();
    }
    /// <summary>Creates independent native components borrowing prepared feature data.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api, () => alpha)
        : new IconReadingView(api, () => content ?? throw new InvalidOperationException("storm content is not prepared."),
            _ => { }, CreateText, load, () => backdrop, () => alpha);
    #endregion
    #endregion

    #region Protected API
    /// <summary>Clears sampled and prepared state after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        value = null; content = null; backdrop = null; pulsing = false; alpha = 1; pulse.Reset();
    }
    #endregion
}
