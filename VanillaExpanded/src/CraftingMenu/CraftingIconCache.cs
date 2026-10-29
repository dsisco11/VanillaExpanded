using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingIconCache(ICoreClientAPI api) : IDisposable
{
    internal const int BakesPerFrame = 4;

    private readonly record struct PreviewKey(int Id, int Size);

    private sealed class Preview(PreviewKey key)
    {
        internal readonly PreviewKey Key = key;
        internal int SubId = -1;
        internal TextureAtlasPosition? Position;
        internal short ReloadIteration;
        internal bool Released;
    }

    private readonly Dictionary<PreviewKey, Preview> previews = new();
    private readonly HashSet<Preview> inFlight = new();
    private ITextureAtlasAPI? atlas;
    private bool subscribed;
    private bool disposed;

    internal TextureAtlasPosition? Get(CraftingRecipeEntry recipe, int size, bool allowBake, out bool baked)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        baked = false;
        if (recipe.PreviewId is not int id) return null;
        var key = new PreviewKey(id, size);
        if (previews.TryGetValue(key, out var preview)) return preview.Position;
        if (!allowBake || inFlight.Count >= BakesPerFrame) return null;
        if (!subscribed)
        {
            atlas = api.ItemTextureAtlas;
            api.Event.ReloadTextures += Clear;
            subscribed = true;
        }
        var stack = recipe.GetPreview()!;
        stack.StackSize = 1;
        preview = new Preview(key);
        previews.Add(key, preview);
        baked = true;
        Bake(preview, stack);
        return preview.Position;
    }

    private void Bake(Preview preview, ItemStack stack)
    {
        var targetAtlas = atlas!;
        bool clipped = api.Render.ScissorStack.Count > 0;
        api.Render.GlScissorFlag(false);
        inFlight.Add(preview);
        try
        {
            api.Render.RenderItemStackToAtlas(stack, targetAtlas, preview.Key.Size, subId =>
            {
                inFlight.Remove(preview);
                // Native work may complete after a texture reload or world exit.
                if (preview.Released)
                {
                    targetAtlas.FreeTextureSpace(subId);
                    return;
                }
                preview.SubId = subId;
                preview.Position = targetAtlas.Positions[subId];
                preview.ReloadIteration = preview.Position.reloadIteration;
            }, scale: (float)(2 * GuiElementPassiveItemSlot.unscaledItemSize / GuiElementPassiveItemSlot.unscaledSlotSize));
        }
        catch
        {
            inFlight.Remove(preview);
            Release(preview);
            previews.Remove(preview.Key);
            throw;
        }
        finally { api.Render.GlScissorFlag(clipped); }
    }

    private void Release(Preview preview)
    {
        preview.Released = true;
        // Atlas reloads can reuse sub-IDs for textures owned by other callers.
        if (preview.Position != null && preview.SubId < atlas!.Positions.Length
            && ReferenceEquals(atlas.Positions[preview.SubId], preview.Position)
            && preview.Position.reloadIteration == preview.ReloadIteration)
            atlas.FreeTextureSpace(preview.SubId);
        preview.Position = null;
    }

    private void Clear()
    {
        foreach (var preview in previews.Values) Release(preview);
        previews.Clear();
        inFlight.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed) api.Event.ReloadTextures -= Clear;
        Clear();
    }
}
