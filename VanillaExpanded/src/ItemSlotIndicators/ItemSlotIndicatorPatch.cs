using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Inserts shared indicator rendering before the game's normal item rendering.</summary>
[HarmonyPatch(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.RenderInteractiveElements))]
internal static class ItemSlotIndicatorPatch
{
    private static readonly MethodInfo RenderItemstackMethod = AccessTools.Method(
        typeof(IRenderAPI),
        nameof(IRenderAPI.RenderItemstackToGui),
        [typeof(ItemSlot), typeof(double), typeof(double), typeof(double), typeof(float), typeof(int), typeof(float), typeof(bool), typeof(bool), typeof(bool)]);

    private static readonly MethodInfo RenderItemstackWithIndicatorMethod = AccessTools.Method(
        typeof(ItemSlotIndicatorPatch), nameof(RenderItemstackWithIndicator));
    private static readonly MethodInfo PopScissorMethod = AccessTools.Method(typeof(IRenderAPI), nameof(IRenderAPI.PopScissor));
    private static readonly MethodInfo PopScissorWithOutlineMethod = AccessTools.Method(typeof(ItemSlotIndicatorPatch), nameof(PopScissorWithOutline));
    private static ItemSlotIndicatorRenderSelection? pendingOutline;
    private static double outlineX, outlineY;

    #region Private
    /// <summary>Replaces only the slot-based render calls while preserving branch and exception metadata.</summary>
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> RenderInteractiveElementsTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(RenderItemstackMethod))
            {
                yield return new CodeInstruction(OpCodes.Call, RenderItemstackWithIndicatorMethod)
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
                continue;
            }
            if (instruction.Calls(PopScissorMethod))
            {
                yield return new CodeInstruction(OpCodes.Call, PopScissorWithOutlineMethod)
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                continue;
            }

            yield return instruction;
        }
    }

    /// <summary>Draws fills before the item and UI outlines and bars afterward, preserving original item-render arguments.</summary>
    private static void RenderItemstackWithIndicator(
        IRenderAPI renderer, ItemSlot slot, double posX, double posY, double posZ,
        float size, int color, float deltaTime,
        bool shading = true, bool rotate = false, bool showStackSize = true)
    {
        var system = ItemSlotIndicatorSystem.Active;
        pendingOutline = null;
        ItemSlotIndicatorRenderSelection selection = default;
        bool selected = system is not null && system.TryGetRenderSelection(slot, out selection);
        if (selected)
        {
            // Outlines and durability bars are UI overlays; particles and background fills retain their pre-item ordering.
            if (selection.Style == ItemSlotIndicatorRenderingStyle.SlotBackground)
                system!.Renderer?.Render(posX, posY, new(selection.Indicator, selection.Effect, selection.Style), system.FrameSnapshot);
            if (selection.OverlayIndicator is { } overlay && selection.OverlayStyle == ItemSlotIndicatorRenderingStyle.SlotBackground)
                system!.Renderer?.Render(posX, posY, new(overlay, selection.OverlayEffect, selection.OverlayStyle), system.FrameSnapshot);
        }

        renderer.RenderItemstackToGui(slot, posX, posY, posZ, size, color, deltaTime, shading, rotate, showStackSize);
        if (!selected) return;
        if (selection.Style == ItemSlotIndicatorRenderingStyle.HorizontalBar)
            system!.Renderer?.Render(posX, posY, new(selection.Indicator, selection.Effect, selection.Style), system.FrameSnapshot);
        if (selection.OverlayIndicator is { } bar && selection.OverlayStyle == ItemSlotIndicatorRenderingStyle.HorizontalBar)
            system!.Renderer?.Render(posX, posY, new(bar, selection.OverlayEffect, selection.OverlayStyle), system.FrameSnapshot);
        if (selection.Style == ItemSlotIndicatorRenderingStyle.SlotOutline || selection.OverlayStyle == ItemSlotIndicatorRenderingStyle.SlotOutline)
        {
            pendingOutline = selection;
            outlineX = posX;
            outlineY = posY;
        }
    }

    /// <summary>Draws perimeter outlines after the inset item clip is removed, retaining the enclosing GUI clip.</summary>
    private static void PopScissorWithOutline(IRenderAPI renderer)
    {
        renderer.PopScissor();
        var selected = pendingOutline;
        pendingOutline = null;
        if (selected is not { } selection || ItemSlotIndicatorSystem.Active is not { } system) return;
        if (selection.Style == ItemSlotIndicatorRenderingStyle.SlotOutline)
            system.Renderer?.Render(outlineX, outlineY, new(selection.Indicator, selection.Effect, selection.Style), system.FrameSnapshot);
        if (selection.OverlayIndicator is { } overlay && selection.OverlayStyle == ItemSlotIndicatorRenderingStyle.SlotOutline)
            system.Renderer?.Render(outlineX, outlineY, new(overlay, selection.OverlayEffect, selection.OverlayStyle), system.FrameSnapshot);
    }
    #endregion
}
