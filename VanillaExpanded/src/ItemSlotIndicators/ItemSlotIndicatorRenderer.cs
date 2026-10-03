using System;
using System.Numerics;

using OpenTK.Graphics.OpenGL4;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Owns the shared texture and draws feature-neutral slot backgrounds.</summary>
internal static class ItemSlotIndicatorRenderer
{
    private static LoadedTexture? whiteTexture;

    #region Public API
    /// <summary>Creates the shared white texture on the client.</summary>
    internal static void InitializeTexture(ICoreClientAPI api)
    {
        DisposeTexture();
        whiteTexture = new LoadedTexture(api) { Width = 1, Height = 1 };
        api.Render.LoadOrUpdateTextureFromRgba([unchecked((int)0xffffffff)], false, 0, ref whiteTexture);
    }

    /// <summary>Releases the shared texture when the mod shuts down.</summary>
    internal static void DisposeTexture()
    {
        whiteTexture?.Dispose();
        whiteTexture = null;
    }

    /// <summary>Draws the selected presentation as a rectangle behind the item without writing to the GUI depth buffer.</summary>
    internal static void Render(IRenderAPI renderer, double posX, double posY, ItemSlotIndicatorRenderSelection selection)
    {
        if (whiteTexture is null) return;

        // The ordinary rectangle remains available regardless of the selected effect's resource availability.
        ItemSlotIndicator indicator = selection.Indicator;
        float slotSize = (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        var bounds = CalculateBounds(posX, posY, slotSize, indicator.Fill);
        try
        {
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);
            renderer.Render2DTexturePremultipliedAlpha(
                whiteTexture.TextureId,
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                80,
                PremultiplyColor(indicator.Color));
        }
        finally
        {
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }
    }

    /// <summary>Calculates a bottom-aligned fill using the slot's center and scaled size.</summary>
    internal static (float X, float Y, float Width, float Height) CalculateBounds(
        double posX, double posY, float slotSize, float fill)
    {
        float fillHeight = slotSize * Math.Clamp(float.IsFinite(fill) ? fill : 0, 0, 1);
        return ((float)(posX - slotSize / 2), (float)(posY - slotSize / 2) + slotSize - fillHeight, slotSize, fillHeight);
    }

    /// <summary>Converts straight-alpha color to the format required by the render API.</summary>
    internal static Vec4f PremultiplyColor(Vector4 color)
    {
        color *= new Vector4(color.W, color.W, color.W, 1);
        return new Vec4f(color.X, color.Y, color.Z, color.W);
    }
    #endregion
}
