using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Renders the cached winning stack through the game's item GUI renderer.</summary>
public sealed class QuickToolItemIcon(ItemStack stack, string entryId) : IRadialMenuIcon
{
    #region Rendering
    /// <summary>Draws the current snapshot item at the requested menu position.</summary>
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled)
    {
        // A detached slot keeps the selected stack reference separate from later slot replacement.
        (float x, float y) = GetHeadCenteringOffset(entryId, sizePixels);
        api.Render.RenderItemstackToGui(new DummySlot(stack), centerX + x, centerY + y, 100, sizePixels,
            unchecked((int)0xffffffff), showStackSize: false);
    }
    #endregion

    #region Layout
    /// <summary>Keeps the working end of long-handled tools near the wedge center instead of their full model bounds.</summary>
    private static (float X, float Y) GetHeadCenteringOffset(string entryId, float sizePixels)
    {
        if (!QuickToolLayout.TryGetTool(entryId, out EnumTool category)) return (0, 0);
        return category switch
        {
            EnumTool.Pickaxe or EnumTool.Axe or EnumTool.Shovel or EnumTool.Hammer or EnumTool.Hoe
                or EnumTool.Scythe or EnumTool.Warhammer or EnumTool.Poleaxe or EnumTool.Halberd
                or EnumTool.Polearm or EnumTool.Staff or EnumTool.Pike or EnumTool.Javelin
                => (0, sizePixels * 0.5f),
            _ => (0, 0)
        };
    }
    #endregion
}
