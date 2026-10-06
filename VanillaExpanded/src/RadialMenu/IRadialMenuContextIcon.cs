using Vintagestory.API.Client;

namespace VanillaExpanded.RadialMenu;

/// <summary>Optionally accepts radial direction while retaining the ordinary artwork interface.</summary>
public interface IRadialMenuContextIcon : IRadialMenuIcon
{
    /// <summary>Draws with an actual clockwise wedge angle; size and placement already include hover.</summary>
    void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled, double wedgeDegrees);
}
