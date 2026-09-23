using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Renders the cached winning stack through the game's item GUI renderer.</summary>
public sealed class QuickToolItemIcon(ItemStack stack) : IRadialMenuIcon
{
    #region Rendering
    /// <summary>Draws the current snapshot item at the requested menu position.</summary>
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled)
    {
        // A detached slot keeps the selected stack reference separate from later slot replacement.
        api.Render.RenderItemstackToGui(new DummySlot(stack), centerX, centerY, 100, sizePixels, unchecked((int)0xffffffff), showStackSize: false);
    }
    #endregion
}
