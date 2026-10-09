using System;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Copies a rendered camera position in double precision without borrowing mutable engine vectors.</summary>
internal readonly record struct ItemSlotIndicatorCameraPosition(double X, double Y, double Z)
{
    #region Public API
    /// <summary>Gets whether every copied coordinate is finite.</summary>
    internal bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    #endregion
}
