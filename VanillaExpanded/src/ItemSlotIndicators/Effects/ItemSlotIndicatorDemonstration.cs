using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Declares a reusable validation effect enabled only by an explicit development-session environment opt-in.</summary>
/// <remarks>VANILLAEXPANDED_INDICATOR_DEMO=1 applies the demonstration to existing provider presentations for that client session.
/// This does not install a diagnostic provider or select production item themes. Remove the opt-in after validation.
/// Parameters X is dimensionless deformation strength in [0,1]; Y/Z/W are unused and zero.</remarks>
internal static class ItemSlotIndicatorDemonstration
{
    #region Public API
    /// <summary>Creates the validation declaration with shader-specific parameter validation and bounded fixed geometry.</summary>
    internal static ItemSlotIndicatorEffectDefinition Create(float strength = 1, int segments = ItemSlotIndicatorEffectDefinition.DefaultSegmentCount)
    {
        if (!float.IsFinite(strength) || strength is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(strength));
        return new("vanillaexpanded:indicator-demonstration", Constants.ModId, "vanillaexpanded_itemslot_demonstration",
            segmentCount: segments, parameters: new Vector4(strength, 0, 0, 0), needsCameraMotion: true);
    }

    /// <summary>Reads the explicit startup-only opt-in; ordinary sessions register no demonstration effect.</summary>
    internal static ItemSlotIndicatorEffectDefinition? FromEnvironment() =>
        Environment.GetEnvironmentVariable("VANILLAEXPANDED_INDICATOR_DEMO") == "1" ? Create() : null;
    #endregion
}
