using System;
using System.Collections.Generic;
using System.Globalization;
using Vintagestory.API.Client;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingIconRenderer(ICoreClientAPI api, CraftingIconCache cache,
    Func<ICraftingIconShaderProgram>? createShader = null) : IDisposable
{
    private readonly Dictionary<int, TextureAtlasPosition> visiblePreviews = new();
    private readonly HashSet<int> visible = new();
    private readonly Dictionary<(int Quantity, double Scale), LoadedTexture> quantities = new();
    private readonly HashSet<(int Quantity, double Scale)> visibleQuantities = new();
    private readonly List<(int Quantity, double Scale)> expiredQuantities = new();
    private ICraftingIconShaderProgram? shader;
    private MeshRef? quad;
    private LoadedTexture? alternatives;
    private bool subscribed;
    private bool disposed;

    internal void Prepare(IReadOnlyList<GuiElement> elements)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        visible.Clear();
        visiblePreviews.Clear();
        visibleQuantities.Clear();
        foreach (GuiElementCraftingTile tile in elements)
        {
            if (!tile.IsInViewport) continue;
            var recipe = tile.Selection!.Recipe;
            visible.Add(recipe.Id);
            if (recipe.OutputQuantity > 1)
                visibleQuantities.Add((recipe.OutputQuantity, GuiElement.scaled(1)));
        }
        expiredQuantities.Clear();
        foreach (var key in quantities.Keys)
            if (!visibleQuantities.Contains(key)) expiredQuantities.Add(key);
        foreach (var key in expiredQuantities)
        {
            quantities[key].Dispose();
            quantities.Remove(key);
        }
        if (visible.Count == 0) return;
        EnsureResources();
        foreach (var key in visibleQuantities)
            if (!quantities.ContainsKey(key))
                quantities.Add(key, api.Gui.TextTexture.GenTextTexture(
                    key.Quantity.ToString(CultureInfo.InvariantCulture), CairoFont.WhiteSmallText().WithFontSize(11)));
        int bakes = 0;
        foreach (GuiElementCraftingTile tile in elements)
        {
            if (!tile.IsInViewport) continue;
            var selection = tile.Selection!;
            if (selection.Output.Alternatives.Length > 1 && alternatives == null)
                alternatives = api.Gui.TextTexture.GenTextTexture("+", CairoFont.WhiteSmallText());
            int size = Math.Max(2, (int)Math.Ceiling(Math.Min(tile.Bounds.InnerWidth, tile.Bounds.InnerHeight) / 2) * 2);
            var position = cache.Get(selection.Recipe, size, bakes < CraftingIconCache.BakesPerFrame, out bool baked);
            if (baked) bakes++;
            if (position != null) visiblePreviews[selection.Recipe.Id] = position;
        }
    }

    internal void Render(IReadOnlyList<GuiElement> elements)
    {
        if (visible.Count == 0) return;
        var render = api.Render;
        var previous = render.CurrentActiveShader;
        previous?.Stop();
        try
        {
            shader!.Use();
            shader.ProjectionMatrix = render.CurrentProjectionMatrix;
            shader.ModelViewMatrix = render.CurrentModelviewMatrix;
            render.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
            int texture = -1;
            foreach (GuiElementCraftingTile tile in elements)
            {
                if (!tile.IsInViewport) continue;
                visiblePreviews.TryGetValue(tile.Selection!.Recipe.Id, out var position);
                if (position != null && texture != position.atlasTextureId)
                {
                    texture = position.atlasTextureId;
                    shader.IconTexture = texture;
                }
                var bounds = tile.Bounds;
                shader.TileBounds = new((float)bounds.renderX, (float)bounds.renderY,
                    (float)bounds.InnerWidth, (float)bounds.InnerHeight);
                shader.RecipeState = tile.Status;
                shader.HasIcon = position != null;
                if (position != null)
                    shader.TextureBounds = new(position.x1, position.y1, position.x2, position.y2);
                render.PushScissor(bounds, true);
                try { render.RenderMesh(quad!); }
                finally { render.PopScissor(); }
            }
        }
        finally
        {
            shader!.Stop();
            previous?.Use();
            render.GlToggleBlend(true, EnumBlendMode.Standard);
        }
        foreach (GuiElementCraftingTile tile in elements)
        {
            if (!tile.IsInViewport) continue;
            var bounds = tile.Bounds;
            render.PushScissor(bounds, true);
            try
            {
                if (quantities.TryGetValue((tile.Selection!.Recipe.OutputQuantity, GuiElement.scaled(1)), out var quantity))
                    render.Render2DTexturePremultipliedAlpha(quantity.TextureId,
                        bounds.renderX + bounds.InnerWidth - quantity.Width - GuiElement.scaled(2),
                        bounds.renderY + bounds.InnerHeight - quantity.Height - GuiElement.scaled(2),
                        quantity.Width, quantity.Height, 320);
                if (tile.Selection.Output.Alternatives.Length > 1 && alternatives != null)
                    render.Render2DTexturePremultipliedAlpha(alternatives.TextureId,
                        bounds.renderX + bounds.InnerWidth - alternatives.Width - GuiElement.scaled(2),
                        bounds.renderY, alternatives.Width, alternatives.Height, 320);
            }
            finally { render.PopScissor(); }
        }
    }

    private void EnsureResources()
    {
        if (!subscribed)
        {
            api.Event.ReloadTextures += ClearView;
            api.Event.ReloadShader += ReloadShader;
            subscribed = true;
        }
        if (shader == null && !ReloadShader())
            throw new InvalidOperationException("Could not compile the crafting icon shader.");
        quad ??= api.Render.UploadMesh(QuadMeshUtil.GetCustomQuadModelData(0, 0, 0, 1, 1));
    }

    private bool ReloadShader()
    {
        shader?.Dispose();
        shader = null;
        var replacement = createShader?.Invoke() ?? new CraftingIconShaderProgram(api);
        try
        {
            api.Shader.RegisterFileShaderProgram("crafting_icons", replacement);
            if (!replacement.Compile())
            {
                replacement.Dispose();
                api.Logger.Error("[VanillaExpanded] Could not compile the crafting icon shader.");
                return false;
            }
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
        shader = replacement;
        return true;
    }

    private void ClearView()
    {
        visiblePreviews.Clear();
        foreach (var quantity in quantities.Values) quantity.Dispose();
        quantities.Clear();
        alternatives?.Dispose();
        alternatives = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed)
        {
            api.Event.ReloadTextures -= ClearView;
            api.Event.ReloadShader -= ReloadShader;
        }
        ClearView();
        shader?.Dispose();
        quad?.Dispose();
    }
}
