using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Resolves a centered reading-over-artwork stack through parented native bounds.</summary>
internal sealed class HudIndicatorStackLayout
{
    private readonly ElementBounds viewport = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
    private readonly ElementBounds root = ElementBounds.Fill.WithAlignment(EnumDialogArea.CenterMiddle);
    private SizeF readingSize, iconSize;
    private float gap;
    private bool dirty = true;
    private (double X, double Y, double Width, double Height, double Scale) geometry;

    #region Public API
    /// <summary>Gets the native reading row, available as a parent for feature-owned subcomponents.</summary>
    public ElementBounds Reading { get; } = ElementBounds.Fill.WithAlignment(EnumDialogArea.CenterTop);
    /// <summary>Gets the native artwork row, available as a parent for custom artwork bounds.</summary>
    public ElementBounds Icon { get; } = ElementBounds.Fill.WithAlignment(EnumDialogArea.CenterTop);
    /// <summary>Gets the measured stack footprint in unscaled GUI units.</summary>
    public SizeF Size { get; private set; }

    /// <summary>Creates one reusable native bounds tree without adding a composer or owning rendering resources.</summary>
    public HudIndicatorStackLayout()
    {
        root.WithParent(viewport).WithChildren(Reading, Icon);
    }

    /// <summary>Updates row measurements only when content changes, omitting spacing for absent rows.</summary>
    public void Configure(SizeF reading, SizeF icon, float spacing, float minimumHeight = 0)
    {
        float nextGap = reading.Height > 0 && icon.Height > 0 ? spacing : 0;
        var nextSize = new SizeF(Math.Max(reading.Width, icon.Width),
            Math.Max(minimumHeight, reading.Height + nextGap + icon.Height));
        if (reading == readingSize && icon == iconSize && nextGap == gap && nextSize == Size) return;
        readingSize = reading;
        iconSize = icon;
        gap = nextGap;
        Size = nextSize;
        dirty = true;
    }

    /// <summary>Recalculates native alignment only when measurements, host geometry, or GUI scale change.</summary>
    public void Resolve(ElementBounds host, double scale)
    {
        var next = (host.renderX, host.renderY, host.OuterWidth, host.OuterHeight, scale);
        if (!dirty && geometry == next) return;
        // The host supplies framebuffer bounds. Bind that boundary once; descendants use native proportional sizing.
        viewport.absFixedX = host.renderX;
        viewport.absFixedY = host.renderY;
        viewport.absInnerWidth = host.OuterWidth;
        viewport.absInnerHeight = host.OuterHeight;
        viewport.Initialized = true;
        root.percentWidth = host.OuterWidth > 0 ? Size.Width * scale / host.OuterWidth : 0;
        root.percentHeight = host.OuterHeight > 0 ? Size.Height * scale / host.OuterHeight : 0;
        SetRow(Reading, readingSize, 0);
        SetRow(Icon, iconSize, readingSize.Height + gap);
        root.MarkDirtyRecursive();
        root.CalcWorldBounds();
        geometry = next;
        dirty = false;
    }

    /// <summary>Invalidates feature-owned child bounds after their local geometry changes.</summary>
    public void Invalidate() => dirty = true;
    #endregion

    #region Private
    /// <summary>Expresses a row relative to its measured parent so native bounds own alignment and scaling.</summary>
    private void SetRow(ElementBounds row, SizeF size, float y)
    {
        row.percentWidth = Size.Width > 0 ? (double)size.Width / Size.Width : 0;
        row.percentHeight = Size.Height > 0 ? (double)size.Height / Size.Height : 0;
        row.percentY = Size.Height > 0 ? (double)y / Size.Height : 0;
    }
    #endregion
}
