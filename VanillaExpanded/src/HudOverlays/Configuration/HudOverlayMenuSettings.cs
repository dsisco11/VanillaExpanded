namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Optional feature-owned settings offered independently of effective gameplay visibility.</summary>
public sealed record HudOverlayMenuSettings(HudOverlaySetting<bool>? Enabled = null, IHudOverlayStyleSetting? Style = null,
    HudOverlaySizeSetting? Size = null)
{
    /// <summary>Retains the constructor used by integrations compiled before sizing was available.</summary>
    public HudOverlayMenuSettings(HudOverlaySetting<bool>? Enabled, IHudOverlayStyleSetting? Style) : this(Enabled, Style, null) { }

    public void Deconstruct(out HudOverlaySetting<bool>? enabled, out IHudOverlayStyleSetting? style)
    {
        enabled = Enabled; style = Style;
    }
}
