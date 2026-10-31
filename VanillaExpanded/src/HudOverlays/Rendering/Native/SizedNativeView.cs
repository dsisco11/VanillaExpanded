using System;
using System.Collections.Generic;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Scales native fixed geometry without changing canonical feature data or percentage-based layout.</summary>
internal sealed class SizedNativeView(INativeView view, HudOverlaySizeSetting size) : INativeView
{
    private readonly Dictionary<ElementBounds, FixedGeometry> canonical = new();
    private ElementBounds? root;
    private double scale = 1;

    public void AddTo(GuiComposer composer, ElementBounds memberBounds, string keyPrefix)
    {
        root = memberBounds;
        view.AddTo(composer, memberBounds, keyPrefix);
        foreach (var child in root.ChildBounds) Scale(child, scale);
    }

    public void Prepare(HudOverlayPreparationContext context)
    {
        foreach (var pair in canonical) pair.Value.Apply(pair.Key, 1);
        canonical.Clear();
        double next = size.Scale;
        try { view.Prepare(context.WithIndicatorScale(next)); }
        finally
        {
            if (root != null)
            {
                foreach (var child in root.ChildBounds) Scale(child, next);
                if (scale != next) root.MarkDirtyRecursive();
            }
            scale = next;
        }
    }

    public void LayoutCompleted(RectangleF clip) => view.LayoutCompleted(clip);
    public void SetOpacity(float opacity) => view.SetOpacity(opacity);
    public void CollectBackdrops(IRenderAPI renderer) => view.CollectBackdrops(renderer);
    public void Dispose() { canonical.Clear(); view.Dispose(); }

    private void Scale(ElementBounds bounds, double factor)
    {
        var geometry = new FixedGeometry(bounds);
        canonical.Add(bounds, geometry);
        geometry.Apply(bounds, factor);
        foreach (var child in bounds.ChildBounds) Scale(child, factor);
    }

    private readonly record struct FixedGeometry(double X, double Y, double Width, double Height,
        double MarginX, double MarginY, double PaddingX, double PaddingY, double OffsetX, double OffsetY)
    {
        public FixedGeometry(ElementBounds bounds) : this(bounds.fixedX, bounds.fixedY, bounds.fixedWidth, bounds.fixedHeight,
            bounds.fixedMarginX, bounds.fixedMarginY, bounds.fixedPaddingX, bounds.fixedPaddingY, bounds.fixedOffsetX, bounds.fixedOffsetY) { }

        public void Apply(ElementBounds bounds, double factor)
        {
            bounds.fixedX = X * factor; bounds.fixedY = Y * factor;
            bounds.fixedWidth = Width * factor; bounds.fixedHeight = Height * factor;
            bounds.fixedMarginX = MarginX * factor; bounds.fixedMarginY = MarginY * factor;
            bounds.fixedPaddingX = PaddingX * factor; bounds.fixedPaddingY = PaddingY * factor;
            bounds.fixedOffsetX = OffsetX * factor; bounds.fixedOffsetY = OffsetY * factor;
        }
    }
}
