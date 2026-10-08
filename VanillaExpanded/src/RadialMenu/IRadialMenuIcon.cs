using Vintagestory.API.Client;

namespace VanillaExpanded.RadialMenu;

/// <summary>Draws caller-owned entry artwork without exposing its source or selection policy to the menu.</summary>
public interface IRadialMenuIcon
{
    /// <summary>Gets whether the artwork stays upright and can fit its screen-aligned square directly to each wedge.</summary>
    bool UsesScreenAlignedSizing => false;

    /// <summary>Draws an icon at a screen-space center using game GUI rendering APIs.</summary>
    void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled);
}
