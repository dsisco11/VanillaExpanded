using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using EnumMouseButton = Vintagestory.API.Common.EnumMouseButton;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Hosts the native titlebar and dropdown in a hover-only composition, independently of content measurements.</summary>
internal sealed class HudOverlayGroupControls : IDisposable
{
    private const double Width = 220;
    private readonly ICoreClientAPI api;
    private readonly HudOverlayGroupMovement movement;
    private readonly GuiDialog host;
    private readonly Action relayout;
    private readonly Func<HudOverlayRegistration, bool> drawable;
    private readonly GuiComposer composer;
    private readonly ElementBounds compositionBounds;
    private readonly HudOverlayNativeTitleBar titleBar;
    private readonly HudOverlayGroupHighlight highlight;
    private HudOverlayGroupLayout? group;
    private string? caption;
    private double preparedScale;
    private bool visible, pressed, menuPress, disposed;
    private RectangleF handle;
    private RectangleF groupBounds;
    private bool hovered, showOutline;
    private double scale = 1;
    private RectangleF content;
    private readonly Action reset;
    private readonly Func<HudOverlayGroupMovement, PointF, bool>? snap;
    public HudOverlayGroupMovement Movement => movement;
    public bool Active => !disposed && (movement.Dragging || pressed || titleBar.Menu.IsOpened);
    public ElementBounds Bounds => titleBar.Bounds;

    #region Public API
    #region Construction and synchronization
    /// <summary>Owns actual native titlebar graphics/menu while borrowing the group's content and position state.</summary>
    public HudOverlayGroupControls(ICoreClientAPI api, GuiDialog host, HudOverlayGroupMovement movement,
        Func<HudOverlayRegistration, bool> drawable, Action relayout, Action? reset = null,
        Func<HudOverlayGroupMovement, PointF, bool>? snap = null)
    {
        this.api = api;
        this.host = host;
        this.movement = movement;
        this.drawable = drawable;
        this.relayout = relayout;
        this.reset = reset ?? (() => { });
        this.snap = snap;
        highlight = new HudOverlayGroupHighlight(api);
        compositionBounds = ElementBounds.Fixed(0, 0, Width, HudOverlaySizes.TitleBarHeight).WithParent(api.Gui.WindowBounds);
        composer = api.Gui.CreateCompo(movement.Key, compositionBounds);
        titleBar = new HudOverlayNativeTitleBar(api, Lang.Get("vanillaexpanded:hud-group-" + movement.Id), composer,
            () => Cancel(), ElementBounds.Fixed(0, 0, Width, HudOverlaySizes.TitleBarHeight));
        titleBar.ConfigurePermission(value => movement.SetMovable(value), () => this.reset());
        composer.AddInteractiveElement(titleBar);
    }

    /// <summary>Tracks prepared group geometry and cancels controls when their target becomes unavailable.</summary>
    public void Synchronize(IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts)
    {
        if (movement.IndicatorId == null) layouts.TryGetValue(movement.Id, out group);
        else group = layouts.Values.FirstOrDefault(layout => layout.Members.Any(member => member.Registration.Id == movement.IndicatorId));
        Refresh(false);
    }

    /// <summary>Renders the actual native composition only while hovered, interacting, or displaying its dropdown.</summary>
    public void Draw(float deltaTime)
    {
        Refresh(true);
        if (visible) composer.Render(deltaTime);
    }

    /// <summary>Draws exact group bounds before indicator content, including outlines away from hover while movement is enabled.</summary>
    public void DrawBackground()
    {
        Refresh(true);
        if (!disposed) highlight.Draw(groupBounds, hovered, showOutline);
    }
    #endregion
    #region Pointer interaction
    /// <summary>Starts the existing cancellable movement from drawable clipped content, respecting permissions and foreground input.</summary>
    public void OnContentMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        Refresh(false);
        if (args.Handled || !visible || Active || !movement.Movable || args.Button != EnumMouseButton.Left) return;
        // A multi-member group's body belongs to its individual controls; its titlebar remains the whole-group grip.
        if (movement.IndicatorId == null && movement.HasMultipleMembers) return;
        if (group?.Members.Any(member => (movement.IndicatorId == null || member.Registration.Id == movement.IndicatorId)
            && drawable(member.Registration) && member.Clip.Contains(args.X, args.Y)) != true) return;
        movement.Begin(ContentOrigin(), new PointF(args.X, args.Y), scale);
        args.Handled = movement.Dragging;
        if (args.Handled) relayout();
    }

    /// <summary>Routes native dropdown selections and chrome presses separately from cancellable content dragging.</summary>
    public void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        Refresh(false);
        if (args.Handled || !visible || args.Button != EnumMouseButton.Left) return;
        if (titleBar.Menu.IsOpened)
        {
            menuPress = true;
            titleBar.Menu.OnMouseDown(api, args);
            if (!args.Handled) titleBar.Menu.OnFocusLost();

            pressed = true;
            args.Handled = true;
            relayout();
            return;
        }
        if (!handle.Contains(args.X, args.Y)) return;
        if (titleBar.IsChrome(args.X, args.Y)) pressed = true;
        else if (movement.Movable) movement.Begin(ContentOrigin(), new PointF(args.X, args.Y), scale);
        else return;
        args.Handled = true;
    }

    /// <summary>Retains native dropdown hover behavior while updating content preview from total pointer displacement.</summary>
    public void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Handled) { Cancel(); return; }
        Refresh(false);
        if (visible) titleBar.Menu.OnMouseMove(api, args);
        if (!Active) return;
        args.Handled = true;
        if (!movement.Dragging) return;
        movement.Move(new PointF(args.X, args.Y));
        relayout();
        snap?.Invoke(movement, new PointF(args.X, args.Y));
    }

    /// <summary>Persists a successful content drop or delegates menu/close icon release to the native titlebar.</summary>
    public void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Handled) { Cancel(); return; }
        Refresh(false);
        if (!Active) return;
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left) return;
        titleBar.Menu.OnMouseUp(api, args);
        if (movement.Dragging)
        {
            movement.Move(new PointF(args.X, args.Y));
            relayout();
            bool allowed = snap?.Invoke(movement, new PointF(args.X, args.Y)) ?? true;
            if (movement.Dragging)
            {
                if (allowed) movement.Commit(movement.ContainerOrigin);
                else movement.Cancel();
            }
        }
        else if (pressed && !menuPress && handle.Contains(args.X, args.Y))
            titleBar.OnMouseUpOnElement(api, args);
        if (titleBar.Menu.IsOpened) titleBar.Menu.OnFocusGained();
        pressed = menuPress = false;
        relayout();
        Refresh(false);
    }

    /// <summary>Preserves native scrolling only while the hover dropdown is open.</summary>
    public void OnMouseWheel(MouseWheelEventArgs args)
    {
        Refresh(false);
        if (visible && titleBar.Menu.IsOpened) titleBar.Menu.OnMouseWheel(api, args);
    }
    #endregion
    #region Keyboard and cancellation

    /// <summary>Preserves native dropdown keyboard navigation and observes its mode selection callbacks.</summary>
    public void OnKeyDown(KeyEvent args)
    {
        if (disposed || !titleBar.Menu.IsOpened) return;
        titleBar.OnKeyDown(api, args);
        titleBar.SynchronizePermission(movement.Movable);
        relayout();
    }

    /// <summary>Forwards native type-to-select behavior while the dropdown owns an active interaction.</summary>
    public void OnKeyPress(KeyEvent args)
    {
        if (!disposed && titleBar.Menu.IsOpened) titleBar.Menu.OnKeyPress(api, args);
    }

    /// <summary>Discards preview and closes the native menu without writing a dragged position.</summary>
    public bool Cancel()
    {
        if (disposed) return false;
        bool active = Active;
        movement.Cancel();
        pressed = menuPress = false;
        if (titleBar.Menu.IsOpened)
            titleBar.Menu.OnMouseUp(api, new MouseEvent(api.Input.MouseX, api.Input.MouseY, EnumMouseButton.Left, 0));
        titleBar.OnFocusLost();
        visible = false;
        hovered = false;
        if (active) relayout();
        return active;
    }
    #endregion
    #region Lifetime and geometry
    /// <summary>Disposes the native titlebar through its composer and detaches only control-owned bounds.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        visible = false;
        hovered = showOutline = false;
        movement.Cancel();
        pressed = menuPress = false;
        try { composer.Dispose(); }
        finally
        {
            try { highlight.Dispose(); }
            finally { compositionBounds.ParentBounds?.ChildBounds.Remove(compositionBounds); group = null; }
        }
    }

    /// <summary>Places the compact titlebar above or below content without shifting the content itself.</summary>
    internal static RectangleF PlaceHandle(RectangleF content, RectangleF viewport, double scale)
    {
        float width = (float)Math.Min(Width * scale, viewport.Width);
        float height = (float)Math.Min(HudOverlaySizes.TitleBarHeight * scale, viewport.Height);
        float y = content.Top - height - 2;
        if (y < viewport.Top) y = content.Bottom + 2;
        return new RectangleF(Math.Clamp(content.Left, viewport.Left, Math.Max(viewport.Left, viewport.Right - width)),
            Math.Clamp(y, viewport.Top, Math.Max(viewport.Top, viewport.Bottom - height)), width, height);
    }
    #endregion
    #endregion

    #region Private
    #region Target geometry and eligibility
    /// <summary>Uses visible content for initial targeting, retaining controls during pointer transit and open native menus.</summary>
    private void Refresh(bool checkButton)
    {
        if (disposed) return;
        var input = api.Input;
        var pointer = new PointF(input.MouseX, input.MouseY);
        double currentScale = RuntimeEnv.GUIScale;
        bool available = !api.HideGuis && !input.MouseGrabbed && group is { Available: true }
            && group.Members.Any(member => (movement.IndicatorId == null || member.Registration.Id == movement.IndicatorId) && drawable(member.Registration) && member.Clip.Width > 0 && member.Clip.Height > 0);
        // The outline is a global movement cue, independent of pointer hover over an unrelated window.
        // Modal input capture still disables movement; ordinary foreground windows only suppress hover controls.
        showOutline = available && movement.IndicatorId == null && movement.Movable && !(api.Gui.OpenedGuis?.Any(dialog =>
            !ReferenceEquals(dialog, host) && dialog.IsOpened() && dialog.CaptureAllInputs()) ?? false);
        if (available)
        {
            var bounds = group!.Root;
            groupBounds = new RectangleF((float)bounds.renderX, (float)bounds.renderY,
                (float)bounds.OuterWidth, (float)bounds.OuterHeight);
        }
        if (available)
        {
            var target = movement.IndicatorId == null ? null : group!.Members.First(member => member.Registration.Id == movement.IndicatorId);
            var targetBounds = target?.Bounds ?? group!.Root;
            content = new RectangleF((float)targetBounds.renderX, (float)targetBounds.renderY,
                (float)targetBounds.OuterWidth, (float)targetBounds.OuterHeight);
            var windowBounds = api.Gui.WindowBounds;
            double guiScale = currentScale;
            movement.ContainerOrigin = new PointF((float)((group!.Root.renderX - windowBounds.renderX) / guiScale),
                (float)((group.Root.renderY - windowBounds.renderY) / guiScale));
            movement.MemberOrigins = group.Members.ToDictionary(member => member.Registration.Id, member => new PointF(
                (float)((member.Bounds.renderX - windowBounds.renderX) / guiScale), (float)((member.Bounds.renderY - windowBounds.renderY) / guiScale)));
            if (movement.IndicatorId != null) groupBounds = content;
        }
        bool eligible = available && !Blocked(pointer);
        if (!eligible || (Active && currentScale != scale)
            || (checkButton && (movement.Dragging || pressed) && input.MouseButton is { Left: false }))
        {
            Cancel();
            return;
        }
        scale = currentScale;
        var root = group!.Root;

        var window = api.Gui.WindowBounds;
        var viewport = new RectangleF((float)window.renderX, (float)window.renderY,
            (float)window.OuterWidth, (float)window.OuterHeight);
        handle = PlaceHandle(content, viewport, scale);
        if (movement.IndicatorId == null && movement.HasMultipleMembers)
        {
            float above = content.Top - 2 * (handle.Height + 2);
            float below = content.Bottom + handle.Height + 4;
            handle.Y = Math.Clamp(above >= viewport.Top ? above : below,
                viewport.Top, Math.Max(viewport.Top, viewport.Bottom - handle.Height));
        }
        bool overContent = group!.Members.Any(member => drawable(member.Registration)
            && (movement.IndicatorId == null || member.Registration.Id == movement.IndicatorId) && member.Clip.Contains(pointer));
        hovered = overContent || Active;
        visible = Active || overContent || (visible && RectangleF.Union(content, handle).Contains(pointer));
        PositionComposition(viewport);
        if (visible)
        {
            titleBar.SynchronizePermission(movement.Movable);
            var indicator = movement.IndicatorId != null
                ? group!.Members.First(member => member.Registration.Id == movement.IndicatorId).Registration
                : !movement.HasMultipleMembers ? group!.Members.First().Registration : null;
            string key = indicator == null ? "vanillaexpanded:hud-group-" + movement.Id : indicator.HoverLabelKey ?? indicator.Id;
            string title = Lang.HasTranslation(key) ? Lang.Get(key) : indicator?.Id ?? Lang.Get("vanillaexpanded:hud-custom-group");
            if (movement.Dragging && caption != null) title = caption;
            if (!composer.Composed || preparedScale != scale || caption != title)
            {
                titleBar.Text = title;
                if (composer.Composed) composer.ReCompose(); else composer.Compose(false);
                preparedScale = scale;
                caption = title;
                // Native composition restores its parent from settings. Reapply the transient control origin;
                // successful drops still persist the content origin, never this above/below titlebar origin.
                PositionComposition(viewport);
            }
            titleBar.FitMenu(viewport);
        }
    }

    /// <summary>Updates the separate native composition without modifying the content layout hierarchy.</summary>
    private void PositionComposition(RectangleF viewport)
    {
        compositionBounds.fixedX = (handle.X - viewport.X) / scale;
        compositionBounds.fixedY = (handle.Y - viewport.Y) / scale;
        compositionBounds.fixedWidth = Bounds.fixedWidth = handle.Width / scale;
        compositionBounds.fixedHeight = Bounds.fixedHeight = handle.Height / scale;
        compositionBounds.MarkDirtyRecursive();
        compositionBounds.CalcWorldBounds();
    }

    /// <summary>Defers to foreground windows and modal input ownership before accepting HUD controls.</summary>
    private bool Blocked(PointF pointer)
    {
        foreach (var dialog in api.Gui.OpenedGuis ?? Enumerable.Empty<GuiDialog>())
        {
            if (ReferenceEquals(dialog, host) || !dialog.IsOpened()) continue;
            if (dialog.CaptureAllInputs()) return true;
            if (dialog.DialogType == EnumDialogType.HUD) continue;
            if (dialog.Composers.Values.Any(composition => composition.Enabled
                && composition.Bounds.PointInside(pointer.X, pointer.Y))) return true;
        }
        return false;
    }

    /// <summary>Maps displayed content, rather than titlebar coordinates, to native persisted GUI units.</summary>
    private PointF ContentOrigin() => new((float)((content.X - api.Gui.WindowBounds.renderX) / scale),
        (float)((content.Y - api.Gui.WindowBounds.renderY) / scale));
    #endregion
    #endregion
}
