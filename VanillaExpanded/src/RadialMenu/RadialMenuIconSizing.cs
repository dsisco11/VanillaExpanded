namespace VanillaExpanded.RadialMenu;

/// <summary>Selects the square orientation used to fit icon artwork inside a radial wedge.</summary>
public enum RadialMenuIconSizing
{
    /// <summary>Uses a circle-inscribed square that fits independently of artwork rotation.</summary>
    RotationSafe,
    /// <summary>Fits an upright square against the boundaries of each screen-positioned wedge.</summary>
    ScreenAligned,
    /// <summary>Fits a square whose axes follow the wedge's radial and tangential directions.</summary>
    WedgeAligned
}
