using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.RiftActivity;

/// <summary>Prepares stable rift content while native elements own artwork and lettering.</summary>
internal sealed class RiftActivityPresentation : IndicatorPresentation
{
    private RiftActivityLevel? value;
    private IconReadingContent? content;
    private IHudOverlayIconBackdrop? backdrop;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private readonly Func<HudOverlayPreparationContext, string, TextureBinding>? load;


    #region Public API
    /// <summary>Accepts headless measurement/resource factories while retaining controller data only.</summary>
    public RiftActivityPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        Func<HudOverlayPreparationContext, string, TextureBinding>? load = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
        : base(measure: measure, createText: createText)
    {
        this.load = load; this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }
    /// <summary>Stores the synchronized native activity category.</summary>
    public void SetContent(RiftActivityLevel next) => value = next;
    /// <summary>Prepares localized data and canonical metrics before host packing.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (!value.HasValue) return;
        if (Style == HudIndicatorStyle.Text)
        {
            PrepareText(context, "rift", value.Value.ToString().ToUpperInvariant()); return;
        }
        string[] names = ["low", "medium", "high", "apocalyptic"];
        string reading = Lang.GetL(context.Locale, "vanillaexpanded:rift-activity-short-" + value.Value.ToString().ToLowerInvariant());
        SizeF text = MeasureText(context, reading, 1.5);
        content = new("textures/hud/rift-activity/" + names[(int)value.Value] + ".png",
            new(HudOverlaySizes.Standard, HudOverlaySizes.Standard), reading, HudOverlaySizes.ReadingGap, ReadingSize: text);
        Size = new(Math.Max(text.Width, content.IconSize.Width), text.Height + content.Gap + content.IconSize.Height);
        backdrop = loadBackdrop(context);
        Visibility.Show();
    }
    /// <summary>Creates independent native components borrowing prepared feature data.</summary>
    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api)
        : new IconReadingView(api, () => content ?? throw new InvalidOperationException("rift content is not prepared."),
            _ => { }, CreateText, load, () => backdrop, strokeWidth: 1.5);
    #endregion

    #region Protected API
    /// <summary>Clears sampled and prepared state after a completed exit or world change.</summary>
    protected override void ClearContent()
    {
        value = null; content = null; backdrop = null;
    }
    #endregion
}
