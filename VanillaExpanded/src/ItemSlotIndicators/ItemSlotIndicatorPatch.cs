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

            yield return instruction;
        }
    }

    /// <summary>Draws the selected background and forwards all original item-render arguments unchanged.</summary>
    private static void RenderItemstackWithIndicator(
        IRenderAPI renderer, ItemSlot slot, double posX, double posY, double posZ,
        float size, int color, float deltaTime,
        bool shading = true, bool rotate = false, bool showStackSize = true)
    {
        if (ItemSlotIndicatorSystem.Active is { } system && system.TryGetIndicator(slot, out ItemSlotIndicator indicator))
        {
            ItemSlotIndicatorRenderer.Render(renderer, posX, posY, indicator);
        }

        renderer.RenderItemstackToGui(slot, posX, posY, posZ, size, color, deltaTime, shading, rotate, showStackSize);
    }
    #endregion
}