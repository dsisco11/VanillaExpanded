using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Renders either form of artwork supported by a vanilla skill item.</summary>
internal sealed class ToolModeSkillIcon(SkillItem skillItem) : IRadialMenuIcon
{
    /// <inheritdoc />
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled)
    {
        float offset = sizePixels / 2f;
        float x = (float)centerX - offset;
        float y = (float)centerY - offset;
        if (skillItem.Texture is not null)
        {
            if (skillItem.TexturePremultipliedAlpha)
                api.Render.Render2DTexturePremultipliedAlpha(skillItem.Texture.TextureId, x, y, sizePixels, sizePixels, 100f);
            else
                api.Render.Render2DTexture(skillItem.Texture.TextureId, x, y, sizePixels, sizePixels, 100f);
        }

        if (skillItem.RenderHandler is not null)
        {
            double renderedSize = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
            skillItem.RenderHandler(skillItem.Code, 0, centerX - renderedSize / 2d, centerY - renderedSize / 2d);
        }
    }
}