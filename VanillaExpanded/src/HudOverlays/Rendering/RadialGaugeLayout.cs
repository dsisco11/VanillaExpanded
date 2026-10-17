using System.Drawing;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Defines the shared housing aperture and registration for circular HUD instruments.</summary>
internal static class RadialGaugeLayout
{
    public const string Backing = "radial/layer-0", Glass = "radial/layer-2";
    public const int Canvas = 512;
    public const float CenterX = 256, CenterY = 250, ApertureRadius = 176;

    #region Public API
    /// <summary>Fits a centered square mechanism into the housing while preserving its own source coordinates.</summary>
    public static RectangleF MechanismBounds(RectangleF housing, float scale)
    {
        float width = housing.Width * scale, height = housing.Height * scale;
        return new(housing.X + housing.Width * CenterX / Canvas - width / 2,
            housing.Y + housing.Height * CenterY / Canvas - height / 2, width, height);
    }
    #endregion
}
