using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.CraftingMenu;

internal sealed class CraftingIconCache(ICoreClientAPI api) : IRenderer
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
    private readonly Queue<(Preview Preview, ItemStack Stack)> pending = new();
    private ITextureAtlasAPI? atlas;
    private bool subscribed;
    private bool disposed;

    public double RenderOrder => 0;
    public int RenderRange => int.MaxValue;

    internal TextureAtlasPosition? Get(CraftingRecipeEntry recipe, int size, bool allowQueue, out bool queued)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        queued = false;
        if (recipe.PreviewId is not int id) return null;
        var key = new PreviewKey(id, size);
        if (previews.TryGetValue(key, out var preview)) return preview.Position;
        if (!allowQueue || inFlight.Count >= BakesPerFrame) return null;
        if (!subscribed)
        {
            atlas = api.ItemTextureAtlas;
            api.Event.RegisterRenderer(this, EnumRenderStage.Before, "crafting-icon-requests");
            api.Event.ReloadTextures += Clear;
            subscribed = true;
        }
        var stack = recipe.GetPreview()!;
        stack.StackSize = 1;
        preview = new Preview(key);
        previews.Add(key, preview);
        inFlight.Add(preview);
        pending.Enqueue((preview, stack));
        queued = true;
        return null;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || stage != EnumRenderStage.Before) return;
        // Outside Ortho the native API queues atlas work instead of baking inside a GUI draw.
        int budget = BakesPerFrame;
        while (budget-- > 0 && pending.TryDequeue(out var request))
            Bake(request.Preview, request.Stack);
    }

    private void Bake(Preview preview, ItemStack stack)
    {
        var targetAtlas = atlas!;
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
            if (previews.TryGetValue(preview.Key, out var current) && ReferenceEquals(current, preview))
                previews.Remove(preview.Key);
            throw;
        }
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
        pending.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed)
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            api.Event.ReloadTextures -= Clear;
        }
        Clear();
    }
}
