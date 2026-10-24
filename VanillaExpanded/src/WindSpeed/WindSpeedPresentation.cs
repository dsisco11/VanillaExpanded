using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.WindSpeed;

/// <summary>Pairs the approved double-hook wind icon with a localized strength reading.</summary>
internal sealed class WindSpeedPresentation : HudIndicatorPresentation<HudIndicatorStyle>
{
    private readonly HudOverlayIconReading compact;
    private int intensity;

    #region Public API
    /// <summary>Accepts optional owned icon and text helpers for headless lifecycle checks.</summary>
    public WindSpeedPresentation(HudOverlayIconReading? compact = null, HudIndicatorTextPresentation? text = null)
        : base(HudIndicatorStyle.Icon, HudIndicatorStyle.Text, text)
    {
        this.compact = compact ?? new();
    }

    /// <summary>Stores the classified local wind strength outside drawing.</summary>
    public void SetContent(int value) => intensity = value;

    /// <summary>Prepares the selected mode using the session locale and cached SVG artwork.</summary>
    public void Prepare(HudOverlayPreparationContext preparation)
    {
        if (intensity <= 0) return;
        string band = intensity switch { 1 => "light", 2 => "moderate", _ => "strong" };
        string reading = Lang.GetL(preparation.Locale, "vanillaexpanded:wind-speed-" + band);
        if (TryPrepareText(preparation, "wind-speed", reading)) return;
        compact.Prepare(preparation, "textures/hud/wind-speed/icon.svg", reading);
        Context = preparation;
        Size = compact.Size;
        Visibility.Show();
    }

    /// <summary>Draws prepared wind content without querying world state.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds)
    {
        if (Context == null || Size.IsEmpty) return;
        if (TryDrawText(renderer, bounds)) return;
        compact.Draw(renderer, bounds);
    }

    /// <summary>Ends ownership of the compact icon and shared text resources.</summary>
    public override void Dispose()
    {
        base.Dispose();
        compact.Dispose();
    }
    #endregion

    #region Protected API
    /// <summary>Releases SVG and reading resources while retaining the sampled band across style changes.</summary>
    protected override void ReleaseArtwork() => compact.Reset();

    /// <summary>Clears the wind band after a completed exit or session reset.</summary>
    protected override void ClearContent() => intensity = 0;
    #endregion
}
