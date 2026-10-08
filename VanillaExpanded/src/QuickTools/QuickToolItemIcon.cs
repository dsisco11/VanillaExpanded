using VanillaExpanded.RadialMenu;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Renders configured winning stacks directly and delegates ordinary presentation to the game.</summary>
public sealed class QuickToolItemIcon(ItemStack stack, string entryId) : IRadialMenuContextIcon
{
    #region Rendering
    /// <inheritdoc />
    public RadialMenuIconSizing Sizing => RadialMenuIconSizing.WedgeAligned;

    /// <summary>Draws the current snapshot item at the requested menu position.</summary>
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled)
        => Render(api, centerX, centerY, sizePixels, enabled, 0);

    /// <summary>Uses independent presentation or ordinary category positioning selected before preparation.</summary>
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled, double wedgeDegrees)
    {
        // A detached slot keeps the selected stack reference separate from later slot replacement.
        var slot = new DummySlot(stack);
        if (ToolHeadPresentationSystem.Renderer?.TryRender(api, slot, centerX, centerY, sizePixels, wedgeDegrees) == true) return;
        (float x, float y) = GetHeadCenteringOffset(entryId, sizePixels);
        api.Render.RenderItemstackToGui(slot, centerX + x, centerY + y, 100, sizePixels,
            unchecked((int)0xffffffff), rotate: false, showStackSize: false);
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
