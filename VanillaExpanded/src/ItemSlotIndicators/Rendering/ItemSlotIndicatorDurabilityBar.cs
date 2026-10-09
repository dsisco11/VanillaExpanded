using System.Collections.Generic;
using System.Numerics;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Composes the engine's rounded, shaded durability-bar appearance and caches bounded reusable textures.</summary>
internal sealed class ItemSlotIndicatorDurabilityBar(ICoreClientAPI client) : GuiElement(client, ElementBounds.Empty)
{
    private readonly Dictionary<(float Size, float Scale, float Fill, Vector4 Color), LoadedTexture> textures = [];
    private readonly Queue<(float Size, float Scale, float Fill, Vector4 Color)> insertionOrder = [];

    #region Public API
    /// <summary>Reuses cached compositions; a new sampled value, color, or GUI scale creates one bounded cache entry.</summary>
    internal void Draw(ItemSlotIndicatorDrawInput input, float scale)
    {
        var key = (input.SlotBounds.Z, scale, input.ResourceFill, input.Color);
        if (!textures.TryGetValue(key, out var texture))
        {
            texture = Compose(input, scale);
            if (textures.Count == 128 && textures.Remove(insertionOrder.Dequeue(), out var oldest)) oldest.Dispose();
            textures.Add(key, texture);
            insertionOrder.Enqueue(key);
        }
        api.Render.Render2DTexturePremultipliedAlpha(texture.TextureId, input.SlotBounds.X, input.SlotBounds.Y,
            texture.Width, texture.Height, 80);
    }

    /// <summary>Releases all owned textures when the indicator backend is disposed.</summary>
    public override void Dispose()
    {
        foreach (var texture in textures.Values) texture.Dispose();
        textures.Clear();
        insertionOrder.Clear();
        base.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Uses the same rounded paths, background color, and embossed shading as the base-game slot durability bar.</summary>
    private LoadedTexture Compose(ItemSlotIndicatorDrawInput input, float scale)
    {
        int size = System.Math.Max(1, (int)input.SlotBounds.Z);
        using var surface = new ImageSurface(Format.Argb32, size, size);
        using var context = new Context(surface);
        double width = System.Math.Max(0, input.SlotBounds.Z - 8 * scale);
        double x = 4 * scale, y = size - 5 * scale, height = 2 * scale;
        var background = GuiStyle.DialogStrongBgColor;
        context.SetSourceRGBA(background[0], background[1], background[2], 1);
        RoundRectangle(context, x, y, width, height, 1);
        context.FillPreserve();
        ShadePath(context, 2);
        if (input.ResourceFill > 0)
        {
            var color = input.Color;
            context.SetSourceRGBA(color.X, color.Y, color.Z, 1);
            RoundRectangle(context, x, y, width * input.ResourceFill, height, 1);
            context.FillPreserve();
            ShadePath(context, 2);
        }
        var texture = new LoadedTexture(api);
        api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
        return texture;
    }
    #endregion
}
