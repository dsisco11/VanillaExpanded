using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Shelter;

internal sealed class ShelterPresentation : IndicatorPresentation
{
    private IconReadingContent? content;
    private IHudOverlayIconBackdrop? backdrop;
    private readonly Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?> loadBackdrop;
    private readonly TextureLoader? load;

    public ShelterPresentation(Func<HudOverlayPreparationContext, string, SizeF>? measure = null,
        Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null,
        TextureLoader? load = null,
        Func<HudOverlayPreparationContext, IHudOverlayIconBackdrop?>? loadBackdrop = null)
        : base(measure: measure, createText: createText)
    {
        this.load = load;
        this.loadBackdrop = loadBackdrop ?? HudOverlayIconBackdrop.Load;
    }

    public void Prepare(HudOverlayPreparationContext context)
    {
        HoverValue = Lang.GetL(context.Locale, "vanillaexpanded:shelter-reading");
        if (Style == HudIndicatorStyle.Text)
        {
            PrepareText(context, "shelter", HoverValue);
            return;
        }
        content = new("textures/hud/shelter.svg", new(HudOverlaySizes.Small, HudOverlaySizes.Small), null, 0);
        Size = content.IconSize;
        backdrop = loadBackdrop(context);
        Visibility.Show();
    }

    public INativeView CreateView(ICoreClientAPI api) => Style == HudIndicatorStyle.Text ? CreateTextView(api)
        : new IconReadingView(api, () => content ?? throw new InvalidOperationException("Shelter content is not prepared."),
            _ => { }, CreateText, load, () => backdrop);

    protected override void ClearContent()
    {
        content = null;
        backdrop = null;
    }
}
