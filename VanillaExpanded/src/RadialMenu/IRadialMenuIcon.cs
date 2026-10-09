using Vintagestory.API.Client;

namespace VanillaExpanded.RadialMenu;

/// <summary>Draws caller-owned entry artwork without exposing its source or selection policy to the menu.</summary>
public interface IRadialMenuIcon
{
    /// <summary>Gets the square orientation used to calculate the icon's wedge allowance.</summary>
    RadialMenuIconSizing Sizing => RadialMenuIconSizing.RotationSafe;

    /// <summary>Draws an icon at a screen-space center using game GUI rendering APIs.</summary>
    void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled);
}
