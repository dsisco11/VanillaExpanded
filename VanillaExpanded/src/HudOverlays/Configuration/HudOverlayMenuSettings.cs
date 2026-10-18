namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Optional feature-owned settings offered independently of effective gameplay visibility.</summary>
public sealed record HudOverlayMenuSettings(HudOverlaySetting<bool>? Enabled = null, IHudOverlayStyleSetting? Style = null);

