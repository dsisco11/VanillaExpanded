using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementStyledScrollbar : GuiElementScrollbar
{
    private readonly double arrowStepUnits;
    private readonly GuiElementStyledArrowButton up;
    private readonly GuiElementStyledArrowButton down;
    private LoadedTexture track;
    private LoadedTexture hoverHandle;
    private LoadedTexture pressedHandle;
    private bool disposed;

    internal GuiElementStyledScrollbar(ICoreClientAPI api, Action<float> changed, ElementBounds bounds, double arrowStep)
        : base(api, changed, bounds)
    {
        arrowStepUnits = double.IsFinite(arrowStep) && arrowStep > 0
            ? arrowStep : throw new ArgumentOutOfRangeException(nameof(arrowStep));
        track = new(api);
        hoverHandle = new(api);
        pressedHandle = new(api);
        up = new(api, true, () => Step(-1), ElementBounds.Fixed(0, 0, 0, 0).WithParent(bounds));
        down = new(api, false, () => Step(1), ElementBounds.Fixed(0, 0, 0, 0).WithParent(bounds));
    }

    public override bool Enabled
    {
        get => base.Enabled;
        set
        {
            base.Enabled = value;
            if (!value) ClearPress();
            if (Bounds.Initialized) UpdateButtons();
        }
    }

    private bool CanScroll => Enabled && totalHeight > visibleHeight
        && Bounds.InnerHeight > currentHandleHeight + .001;

    private bool CanStep(int direction) => CanScroll && (direction < 0
        ? currentHandlePosition > .001
        : currentHandlePosition < Bounds.InnerHeight - currentHandleHeight - .001);

    private (int X, int Y, int Width, int Height, int Arrow) Measure()
    {
        int width = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.ScrollbarTrackWidth, scaled(1)));
        int height = Math.Max(1, (int)Math.Round(Bounds.InnerHeight, MidpointRounding.AwayFromZero));
        return (
            (int)Math.Round(Bounds.renderX + Bounds.absPaddingX + (Bounds.InnerWidth - width) / 2,
                MidpointRounding.AwayFromZero),
            (int)Math.Round(Bounds.renderY + Bounds.absPaddingY, MidpointRounding.AwayFromZero),
            width, height, Math.Min(width, (height - 1) / 2));
    }

    private int ThumbHeight(int trackHeight) => Math.Clamp(
        (int)Math.Round(currentHandleHeight / Bounds.InnerHeight * trackHeight, MidpointRounding.AwayFromZero),
        Math.Min(GuiControlMetrics.ScrollbarMinimumThumbPixels, trackHeight), trackHeight);

    private double ThumbOffset(int trackHeight) => Bounds.InnerHeight > currentHandleHeight
        ? currentHandlePosition / (Bounds.InnerHeight - currentHandleHeight) * (trackHeight - ThumbHeight(trackHeight))
        : 0;

    private void UpdateButtons()
    {
        var pixels = Measure();
        Update(up, pixels.Y, CanStep(-1));
        Update(down, pixels.Y + pixels.Height - pixels.Arrow, CanStep(1));

        void Update(GuiElementStyledArrowButton button, int y, bool enabled)
        {
            button.Bounds.fixedX = -Bounds.absPaddingX / scaled(1);
            button.Bounds.fixedY = (y - Bounds.renderY - Bounds.absPaddingY) / scaled(1);
            button.Bounds.fixedWidth = Bounds.OuterWidth / scaled(1);
            button.Bounds.fixedHeight = pixels.Arrow / scaled(1);
            button.Bounds.CalcWorldBounds();
            button.InsideClipBounds = InsideClipBounds;
            button.ZOffset = zOffset;
            button.Enabled = enabled;
        }
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        RecomposeHandle();
    }

    public override void RecomposeHandle()
    {
        Bounds.CalcWorldBounds();
        if (!CanScroll) ClearPress();
        var pixels = Measure();
        if (track.Width != pixels.Width || track.Height != pixels.Height)
        {
            using var image = new ImageSurface(Format.Argb32, pixels.Width, pixels.Height);
            using var ctx = new Context(image);
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.ScrollbarTrack));
            ctx.Paint();
            generateTexture(image, ref track, linearMag: false);
        }
        UpdateButtons();
        up.Recompose();
        down.Recompose();
        int width = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.ScrollbarThumbWidth, scaled(1)));
        int height = Math.Max(1, ThumbHeight(pixels.Height - 2 * pixels.Arrow));
        ComposeThumb(width, height, GuiControlPalette.ScrollbarThumb, ref handleTexture);
        ComposeThumb(width, height, GuiControlPalette.ScrollbarHover, ref hoverHandle);
        ComposeThumb(width, height, GuiControlPalette.ScrollbarPressed, ref pressedHandle);
    }

    private void ComposeThumb(int width, int height, int color, ref LoadedTexture texture)
    {
        using var surface = new ImageSurface(Format.Argb32, width, height);
        using var context = new Context(surface);
        context.SetSourceRGBA(GuiControlPalette.Color(color));
        RoundRectangle(context, 0, 0, width, height, Math.Min(width, height) / 2d);
        context.Fill();
        generateTexture(surface, ref texture, linearMag: false);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        var pixels = Measure();
        UpdateButtons();
        bool inside = IsPositionInside(api.Input.MouseX, api.Input.MouseY);
        float depth = 200 + zOffset;
        api.Render.Render2DTexturePremultipliedAlpha(track.TextureId, pixels.X, pixels.Y,
            pixels.Width, pixels.Height, depth);
        up.RenderInteractiveElements(deltaTime);
        down.RenderInteractiveElements(deltaTime);
        if (!CanScroll) return;
        int trackHeight = pixels.Height - 2 * pixels.Arrow;
        double thumbTop = pixels.Y + pixels.Arrow + Math.Round(ThumbOffset(trackHeight), MidpointRounding.AwayFromZero);
        bool hoveredThumb = inside && api.Input.MouseY >= thumbTop && api.Input.MouseY < thumbTop + ThumbHeight(trackHeight);
        var thumb = mouseDownOnScrollbarHandle ? pressedHandle : hoveredThumb ? hoverHandle : handleTexture;
        api.Render.Render2DTexturePremultipliedAlpha(thumb.TextureId,
            pixels.X + Math.Round((pixels.Width - handleTexture.Width) / 2d, MidpointRounding.AwayFromZero),
            thumbTop,
            handleTexture.Width, ThumbHeight(trackHeight), depth);
    }

    private void Step(int direction)
    {
        if (!CanStep(direction)) return;
        double maximum = Bounds.InnerHeight - currentHandleHeight;
        double next = Math.Clamp(currentHandlePosition
            + direction * scaled(arrowStepUnits) / ScrollConversionFactor, 0, maximum);
        // Native range conversions use floats; snap endpoint residue in handle space.
        currentHandlePosition = (float)(next < .001 ? 0 : maximum - next < .001 ? maximum : next);
        TriggerChanged();
        UpdateButtons();
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!CanScroll || !IsPositionInside(args.X, args.Y)) return;
        UpdateButtons();
        if (up.IsPositionInside(args.X, args.Y))
        {
            mouseDownOnScrollbarHandle = false;
            down.OnFocusLost();
            up.OnMouseDown(api, args);
            return;
        }
        if (down.IsPositionInside(args.X, args.Y))
        {
            mouseDownOnScrollbarHandle = false;
            up.OnFocusLost();
            down.OnMouseDown(api, args);
            return;
        }
        var pixels = Measure();
        ClearPress();
        int trackHeight = pixels.Height - 2 * pixels.Arrow;
        double thumbTop = pixels.Y + pixels.Arrow
            + Math.Round(ThumbOffset(trackHeight), MidpointRounding.AwayFromZero);
        bool onThumb = args.Y >= thumbTop && args.Y < thumbTop + ThumbHeight(trackHeight);
        mouseDownStartY = onThumb ? args.Y - (int)thumbTop : ThumbHeight(trackHeight) / 2;
        mouseDownOnScrollbarHandle = true;
        if (!onThumb) UpdateDrag(args.Y);
        args.Handled = true;
    }

    private void UpdateDrag(int mouseY)
    {
        var pixels = Measure();
        int trackHeight = pixels.Height - 2 * pixels.Arrow;
        int travel = trackHeight - ThumbHeight(trackHeight);
        if (travel <= 0) return;
        double position = Math.Clamp(mouseY - pixels.Y - pixels.Arrow - mouseDownStartY, 0, travel);
        currentHandlePosition = (float)(position / travel * (Bounds.InnerHeight - currentHandleHeight));
        TriggerChanged();
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (CanScroll && mouseDownOnScrollbarHandle) UpdateDrag(args.Y);
    }

    private void ClearPress()
    {
        up.OnFocusLost();
        down.OnFocusLost();
        mouseDownOnScrollbarHandle = false;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        base.OnMouseUp(api, args);
        up.OnMouseUp(api, args);
        down.OnMouseUp(api, args);
        ClearPress();
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        ClearPress();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (CanScroll) base.OnMouseWheel(api, args);
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (CanScroll) base.OnKeyDown(api, args);
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        track.Dispose();
        up.Dispose();
        down.Dispose();
        hoverHandle.Dispose();
        pressedHandle.Dispose();
        base.Dispose();
    }
}
