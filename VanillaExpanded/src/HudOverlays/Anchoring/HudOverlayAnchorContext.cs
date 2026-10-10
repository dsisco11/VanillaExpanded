using System;
using System.Collections.Generic;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Owns session window bounds and shares one target read per visible frame among groups.</summary>
internal sealed class HudOverlayAnchorContext
{
    public const string ScreenTargetId = "screen";
    public const string HotbarTargetId = "hotbar";
    public const double SafeInset = 12;
    private readonly int threadId = Environment.CurrentManagedThreadId;
    private readonly Func<double> readScale;
    private readonly Func<RectangleF?> readHotbar;
    private readonly Func<bool> isHidden;
    private readonly Dictionary<string, RectangleF?> frameTargets = new(StringComparer.Ordinal);
    private RectangleF viewport;
    private int leftMargin, rightMargin;
    private bool visible;
    private bool frameStarted;

    public ElementBounds WindowBounds { get; }
    public double GuiScale { get; private set; }
    public RectangleF SafeRectangle { get; private set; }
    /// <summary>Gets the revision including native screen-alignment margins.</summary>
    public long Revision { get; private set; }
    /// <summary>Gets window/scale changes affecting both screen and named attachments.</summary>
    public long GeometryRevision { get; private set; }

    #region Public API
    #region Construction
    /// <summary>Acquires the native window parent once; all engine services and target bounds remain borrowed.</summary>
    public HudOverlayAnchorContext(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        WindowBounds = api.Gui.WindowBounds;
        readScale = () => RuntimeEnv.GUIScale;
        readHotbar = new HudOverlayHotbarAnchor(api).Resolve;
        isHidden = () => api.HideGuis;
    }

    /// <summary>Accepts native bounds and target readers for headless integration without a graphics context.</summary>
    internal HudOverlayAnchorContext(ElementBounds windowBounds, Func<double> readScale,
        Func<RectangleF?> readHotbar, Func<bool>? isHidden = null)
    {
        ArgumentNullException.ThrowIfNull(windowBounds);
        ArgumentNullException.ThrowIfNull(readScale);
        ArgumentNullException.ThrowIfNull(readHotbar);
        WindowBounds = windowBounds;
        this.readScale = readScale;
        this.readHotbar = readHotbar;
        this.isHidden = isHidden ?? (() => false);
    }
    #endregion

    #region Frame and targets
    /// <summary>Refreshes native window/scale/margins and clears only frame-local target samples.</summary>
    public void BeginFrame(bool visible = true)
    {
        CheckThread();
        double scale = readScale();
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException("GUI scale must be finite and positive.");
        if (!WindowBounds.Initialized || WindowBounds.RequiresRecalculation) WindowBounds.CalcWorldBounds();
        var current = new RectangleF((float)WindowBounds.renderX, (float)WindowBounds.renderY,
            (float)WindowBounds.OuterWidth, (float)WindowBounds.OuterHeight);
        ValidateRectangle(current);
        int left = GuiStyle.LeftDialogMargin, right = GuiStyle.RightDialogMargin;
        bool geometryChanged = !frameStarted || current != viewport || scale != GuiScale;
        if (geometryChanged) GeometryRevision++;
        if (geometryChanged || left != leftMargin || right != rightMargin) Revision++;
        viewport = current;
        GuiScale = scale;
        leftMargin = left;
        rightMargin = right;
        float inset = (float)(SafeInset * scale);
        // Tiny viewports still yield a finite empty safe area rather than negative dimensions.
        float insetX = Math.Min(inset, current.Width / 2), insetY = Math.Min(inset, current.Height / 2);
        SafeRectangle = new RectangleF(current.X + insetX, current.Y + insetY,
            Math.Max(0, current.Width - 2 * insetX), Math.Max(0, current.Height - 2 * insetY));
        this.visible = visible && !isHidden();
        frameTargets.Clear();
        frameStarted = true;
    }

    /// <summary>Reads each requested target once this frame; unavailable targets hide without fallback.</summary>
    public RectangleF? Resolve(string targetId)
    {
        CheckThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        if (!frameStarted) throw new InvalidOperationException("Begin a frame before resolving anchors.");
        if (frameTargets.TryGetValue(targetId, out var rectangle)) return rectangle;
        rectangle = !visible ? null : targetId switch
        {
            ScreenTargetId => viewport,
            HotbarTargetId => readHotbar(),
            _ => null
        };
        if (rectangle is { } value && !IsAvailable(value)) rectangle = null;
        frameTargets.Add(targetId, rectangle);
        return rectangle;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Rejects invalid window geometry rather than publishing nonfinite layout inputs.</summary>
    private static void ValidateRectangle(RectangleF rectangle)
    {
        if (!float.IsFinite(rectangle.X) || !float.IsFinite(rectangle.Y)
            || !float.IsFinite(rectangle.Width) || !float.IsFinite(rectangle.Height)
            || rectangle.Width < 0 || rectangle.Height < 0)
            throw new InvalidOperationException("Native window bounds must be finite and nonnegative.");
    }

    /// <summary>Filters target samples with empty or invalid dimensions without diagnostics.</summary>
    private static bool IsAvailable(RectangleF rectangle) => float.IsFinite(rectangle.X) && float.IsFinite(rectangle.Y)
        && float.IsFinite(rectangle.Width) && float.IsFinite(rectangle.Height)
        && rectangle.Width > 0 && rectangle.Height > 0;

    /// <summary>Enforces main-thread native GUI reads and frame mutation.</summary>
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != threadId)
            throw new InvalidOperationException("HUD anchor operations require the creating client main thread.");
    }
    #endregion
}
