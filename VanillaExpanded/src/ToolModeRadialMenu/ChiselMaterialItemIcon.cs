using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Uses ordinary native stack presentation for a detached chisel material snapshot.</summary>
internal sealed class ChiselMaterialItemIcon(ItemStack stack) : IRadialMenuIcon
{
    #region Public API
    /// <inheritdoc />
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled)
    {
        var slot = new DummySlot(stack);
        api.Render.RenderItemstackToGui(slot, centerX, centerY, 100, sizePixels,
            unchecked((int)0xffffffff), rotate: false, showStackSize: false);
    }
    #endregion
}
