using System;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Renders either form of artwork supported by a vanilla skill item.</summary>
internal sealed class ToolModeSkillIcon(SkillItem skillItem) : IRadialMenuIcon
{
    #region Public API

    /// <inheritdoc />
    public bool UsesScreenAlignedSizing => true;

    /// <inheritdoc />
    public void Render(ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled)
    {
        // Read live configuration and apply the same centered scale to both artwork paths.
        float iconScale = VanillaExpandedModSystem.Config.ToolModeIconSize;
        sizePixels *= float.IsFinite(iconScale) ? Math.Clamp(iconScale, 0.15f, 2.5f) : 0.75f;
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
            float scale = sizePixels / (float)renderedSize;
            api.Render.GlPushMatrix();
            try
            {
                api.Render.GlTranslate(centerX, centerY, 0);
                api.Render.GlScale(scale, scale, 1);
                api.Render.GlTranslate(-centerX, -centerY, 0);
                skillItem.RenderHandler(skillItem.Code, 0, centerX - renderedSize / 2d, centerY - renderedSize / 2d);
            }
            finally
            {
                api.Render.GlPopMatrix();
            }
        }
    }

    #endregion
}
