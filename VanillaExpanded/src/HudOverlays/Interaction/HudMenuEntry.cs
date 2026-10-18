namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Describes a localized native row; Selected represents either a toggle or the selected style.</summary>
internal sealed record HudMenuEntry(string Code, string Label, bool Selected, HudMenuEntryKind Kind = HudMenuEntryKind.Toggle);
