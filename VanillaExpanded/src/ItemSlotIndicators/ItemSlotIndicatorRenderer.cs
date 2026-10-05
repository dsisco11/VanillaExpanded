using System;
using System.Numerics;

using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.ItemSlotIndicators;

/// <summary>Submits the selected presentation and isolates effect failure without changing provider selection or samples.</summary>
internal sealed class ItemSlotIndicatorRenderer(ItemSlotIndicatorResources resources, IItemSlotIndicatorDrawBackend backend,
    Func<float>? slotSize = null, Func<float>? guiScale = null) : IDisposable
{
    private bool disposed;
    private readonly Func<float> scaledSlotSize = slotSize ?? (static () => (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize));
    private readonly Func<float> currentGuiScale = guiScale ?? (static () => (float)GuiElement.scaled(1));

    #region Public API
    /// <summary>Draws prepared effects or equivalent rectangles, restoring state before fallback and normal item rendering.</summary>
    internal void Render(double posX, double posY, ItemSlotIndicatorRenderSelection selection, ItemSlotIndicatorFrameSnapshot frame)
    {
        RenderLayer(posX, posY, selection, frame);
        if (selection.OverlayIndicator is { } overlay)
            RenderLayer(posX, posY, new(overlay, selection.OverlayEffect, selection.OverlayStyle), frame);
    }

    /// <summary>Releases draw-adapter resources once; prepared mesh/program lifetime remains with the resource owner.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        backend.Dispose();
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

    #region Private
    /// <summary>Submits one layer with isolated fallback and restoration before the next layer.</summary>
    private void RenderLayer(double posX, double posY, ItemSlotIndicatorRenderSelection selection, ItemSlotIndicatorFrameSnapshot frame)
    {
        try
        {
            if (disposed || !backend.Supported || !ItemSlotIndicatorDrawInput.TryCreate(posX, posY,
                scaledSlotSize(), selection.Indicator, out var input, selection.Style)) return;
            bool effectDrawn = false;
            bool backgroundDrawn = false;
            if (selection.Effect is { } effect && effect.SupportsStyle(selection.Style)
                && resources.TryGet(effect, out var program, out var mesh))
            {
                try
                {
                    backend.Begin();
                    if (effect.DrawBackground && resources.Rectangle is { } background)
                    {
                        DrawOrdinary(background, input, selection.Style);
                        backgroundDrawn = true;
                    }
                    backend.Effect(program!, mesh!, input, effect, frame);
                    effectDrawn = true;
                }
                catch (Exception exception) { resources.FailDraw(effect, exception.Message); }
                finally { backend.Restore(); }
            }
            // Fallback uses the same sampled fill/color after the effect scope has fully restored its caller.
            bool hasCue = input.TryCreateBoundaryCue(out var cue);
            if ((effectDrawn || backgroundDrawn) && !hasCue) return;
            if (resources.Rectangle is not { } rectangle) return;
            try
            {
                backend.Begin();
                if (!effectDrawn && !backgroundDrawn) DrawOrdinary(rectangle, input, selection.Style);
                // Cue rendering follows shader restoration and shares the fallback's state scope when possible.
                if (hasCue) backend.Rectangle(rectangle, cue);
            }
            finally { backend.Restore(); }
        }
        catch (Exception exception) { backend.ReportFailure(exception); }
    }

    /// <summary>Draws the selected ordinary presentation using one prepared rectangle and the inherited GUI state scope.</summary>
    private void DrawOrdinary(MeshRef rectangle, ItemSlotIndicatorDrawInput input, ItemSlotIndicatorRenderingStyle style)
    {
        if (style == ItemSlotIndicatorRenderingStyle.SlotBackground)
        {
            if (input.Fill > 0) backend.Rectangle(rectangle, input);
            return;
        }
        var layout = ItemSlotIndicatorStyleLayout.Create(input, currentGuiScale());
        if (style == ItemSlotIndicatorRenderingStyle.SlotOutline)
        {
            DrawBounds(rectangle, layout.OutlineTop, input.Color);
            DrawBounds(rectangle, layout.OutlineBottom, input.Color);
            DrawBounds(rectangle, layout.OutlineLeft, input.Color);
            DrawBounds(rectangle, layout.OutlineRight, input.Color);
        }
        else
        {
            backend.DurabilityBar(input, currentGuiScale());
        }
    }

    /// <summary>Skips zero-area pieces and submits explicit style bounds without legacy coordinate truncation.</summary>
    private void DrawBounds(MeshRef rectangle, Vector4 bounds, Vector4 color)
    {
        if (bounds.Z <= 0 || bounds.W <= 0) return;
        backend.Rectangle(rectangle, ItemSlotIndicatorStyleLayout.Rectangle(bounds, color));
    }

    #endregion
}
