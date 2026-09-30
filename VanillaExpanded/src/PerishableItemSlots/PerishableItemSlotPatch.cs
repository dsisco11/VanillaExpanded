using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.PerishableItemSlots;

[HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.RenderInteractiveElements))]
internal static class PerishableItemSlotPatch
{
    private const long RefreshIntervalMilliseconds = 1_000;
    private const float StaleOpacityMultiplier = 1.3f;
    private const float FullyStaleFreshness = 0.35f;
    private const float FullyFreshFreshness = 0.75f;

    private static readonly Vector4 StaleColor = new(0.88f, 0.08f, 0.05f, 1);
    private static readonly Vector4 FreshColor = new(0.18f, 0.48f, 0.24f, 1);

    private static readonly MethodInfo RenderItemstackMethod = AccessTools.Method(
        typeof(IRenderAPI),
        nameof(IRenderAPI.RenderItemstackToGui),
        [typeof(ItemSlot), typeof(double), typeof(double), typeof(double), typeof(float), typeof(int), typeof(float), typeof(bool), typeof(bool), typeof(bool)]);

    private static readonly MethodInfo RenderItemstackWithFreshnessMethod = AccessTools.Method(
        typeof(PerishableItemSlotPatch),
        nameof(RenderItemstackWithFreshness));

    private static readonly ConditionalWeakTable<ItemStack, FreshnessSample> FreshnessSamples = new();
    private static LoadedTexture? whiteTexture;

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> RenderInteractiveElementsTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(RenderItemstackMethod))
            {
                yield return new CodeInstruction(OpCodes.Call, RenderItemstackWithFreshnessMethod)
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
                continue;
            }

            yield return instruction;
        }
    }

    private static void RenderItemstackWithFreshness(
        IRenderAPI renderer,
        ItemSlot slot,
        double posX,
        double posY,
        double posZ,
        float size,
        int color,
        float deltaTime,
        bool shading = true,
        bool rotate = false,
        bool showStackSize = true)
    {
        if (TryGetFreshness(slot, out float freshness))
        {
            RenderFreshnessBackground(renderer, posX, posY, freshness);
        }

        renderer.RenderItemstackToGui(slot, posX, posY, posZ, size, color, deltaTime, shading, rotate, showStackSize);
    }

    private static bool TryGetFreshness(ItemSlot slot, out float freshness)
    {
        freshness = 0;
        ItemStack? stack = slot.Itemstack;
        ICoreAPI? api = slot.Inventory?.Api;
        if (stack is null || api is null) return false;

        FreshnessSample sample = FreshnessSamples.GetOrCreateValue(stack);
        long now = Environment.TickCount64;
        if (sample.LastUpdatedMilliseconds < 0 || now - sample.LastUpdatedMilliseconds >= RefreshIntervalMilliseconds)
        {
            TransitionState? state = ResolvePerishState(api.World, slot);
            sample.HasPerishState = state is not null;
            sample.Freshness = state is null ? 0 : CalculateFreshness(state);
            sample.LastUpdatedMilliseconds = now;
        }

        freshness = sample.Freshness;
        return sample.HasPerishState;
    }

    private static TransitionState? ResolvePerishState(IWorldAccessor world, ItemSlot slot)
    {
        ItemStack stack = slot.Itemstack!;
        TransitionState? state = stack.Collectible.UpdateAndGetTransitionState(world, slot, EnumTransitionType.Perish);
        if (state is not null) return state;

        IBlockMealContainer? mealContainer = stack.Collectible.GetCollectibleInterface<IBlockMealContainer>();
        ItemStack[]? contents = mealContainer?.GetNonEmptyContents(world, stack);
        if (contents is null || contents.Length == 0 || slot.Inventory?.Api is not ICoreAPI api) return null;

        var dummyInventory = new DummyInventory(api);
        dummyInventory.OnAcquireTransitionSpeed += (type, contentStack, multiplier) =>
            type == EnumTransitionType.Perish
                ? slot.Inventory.GetTransitionSpeedMul(type, contentStack)
                : 0;

        ItemSlot contentSlot = BlockCrock.GetDummySlotForFirstPerishableStack(world, contents, null, dummyInventory);
        return contentSlot.Itemstack?.Collectible.UpdateAndGetTransitionState(world, contentSlot, EnumTransitionType.Perish);
    }

    internal static float CalculateFreshness(TransitionState state)
    {
        float totalHours = state.FreshHours + state.TransitionHours;
        if (totalHours <= 0) return 0;

        float remainingTransitionHours = Math.Max(0, state.TransitionHours - Math.Max(0, state.TransitionedHours - state.FreshHours));
        return Math.Clamp((state.FreshHoursLeft + remainingTransitionHours) / totalHours, 0, 1);
    }

    private static void RenderFreshnessBackground(IRenderAPI renderer, double posX, double posY, float freshness)
    {
        if (whiteTexture is null) return;

        float slotSize = (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        float width = slotSize;
        float height = slotSize;
        float x = (float)(posX - slotSize / 2);
        float y = (float)(posY - slotSize / 2);
        float fillHeight = height * freshness;

        renderer.Render2DTexturePremultipliedAlpha(
            whiteTexture.TextureId,
            x,
            y + height - fillHeight,
            width,
            fillHeight,
            80,
            FreshnessColorVector(freshness));
    }

    internal static Vec4f FreshnessColorVector(float freshness)
    {
        float amount = Math.Clamp(
            (freshness - FullyStaleFreshness) / (FullyFreshFreshness - FullyStaleFreshness),
            0,
            1);
        amount = amount * amount * (3 - 2 * amount);
        Vector4 color = Vector4.Lerp(StaleColor, FreshColor, amount);
        float freshOpacity = Math.Clamp(VanillaExpandedModSystem.Config.PerishableItemFreshnessIndicatorIntensity, 0, 1);
        color.W = float.Lerp(Math.Min(1, freshOpacity * StaleOpacityMultiplier), freshOpacity, amount);
        return PremultipliedColor(color);
    }

    private static Vec4f PremultipliedColor(Vector4 color)
    {
        color *= new Vector4(color.W, color.W, color.W, 1);
        return new Vec4f(color.X, color.Y, color.Z, color.W);
    }

    internal static void InitializeTexture(ICoreClientAPI api)
    {
        whiteTexture?.Dispose();
        whiteTexture = new LoadedTexture(api) { Width = 1, Height = 1 };
        api.Render.LoadOrUpdateTextureFromRgba([unchecked((int)0xffffffff)], false, 0, ref whiteTexture);
    }

    internal static void DisposeTexture()
    {
        whiteTexture?.Dispose();
        whiteTexture = null;
    }

    private sealed class FreshnessSample
    {
        internal long LastUpdatedMilliseconds { get; set; } = -1;
        internal bool HasPerishState { get; set; }
        internal float Freshness { get; set; }
    }
}