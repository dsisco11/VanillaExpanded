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

/// <summary>Hosts container titlebars and direct member dragging independently of content measurements.</summary>
internal sealed class HudOverlayGroupControls : IDisposable
{
    private const double Width = 220;
    private readonly ICoreClientAPI api;
    private readonly HudOverlayGroupMovement movement;
    private readonly GuiDialog host;
    private readonly Action relayout;
    private readonly Func<HudOverlayRegistration, bool> drawable;
    private GuiComposer? composer;
    private ElementBounds? compositionBounds;
    private HudOverlayGroupTitleBar? titleBar;
    private readonly ElementBounds titleBounds = ElementBounds.Fixed(0, 0, Width, HudOverlaySizes.TitleBarHeight);
    private readonly HudOverlayGroupHighlight highlight;
    private HudOverlayGroupLayout? group;
    private string? caption;
    private double preparedScale;
    private bool visible, disposed;
    private RectangleF handle;
    private RectangleF groupBounds;
    private bool hovered, showOutline;
    private double scale = 1;
    private RectangleF content;
    private readonly Func<HudOverlayGroupMovement, PointF, bool>? snap;
    private bool HasTitleBar => movement.IndicatorId == null && movement.HasMultipleMembers;
    public HudOverlayGroupMovement Movement => movement;
    public bool Active => !disposed && movement.Dragging;
    public ElementBounds Bounds => titleBounds;

    #region Public API
    #region Construction and synchronization
    /// <summary>Owns movement cues and lazily composed group captions while borrowing container state.</summary>
    public HudOverlayGroupControls(ICoreClientAPI api, GuiDialog host, HudOverlayGroupMovement movement,
        Func<HudOverlayRegistration, bool> drawable, Action relayout,
        Func<HudOverlayGroupMovement, PointF, bool>? snap = null, IHudOverlayDashedBorder? dashedBorder = null)
    {
        this.api = api;
        this.host = host;
        this.movement = movement;
        this.drawable = drawable;
        this.relayout = relayout;
        this.snap = snap;
        highlight = new HudOverlayGroupHighlight(api, dashedBorder);
    }

    /// <summary>Tracks prepared group geometry and cancels controls when their target becomes unavailable.</summary>
    public void Synchronize(IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts)
    {
        if (movement.IndicatorId == null) layouts.TryGetValue(movement.Id, out group);
        else group = layouts.Values.FirstOrDefault(layout => layout.Members.Any(member => member.Registration.Id == movement.IndicatorId));
        Refresh(false);
    }

    /// <summary>Renders permission cues and the hovered multi-member group's drag caption.</summary>
    public void Draw(float deltaTime)
    {
        Refresh(true);
        if (!disposed && showOutline) highlight.DrawOutline(groupBounds, movement.Movable);
        if (visible && HasTitleBar) composer?.Render(deltaTime);
    }

    /// <summary>Draws exact group bounds before indicator content, including outlines away from hover while movement is enabled.</summary>
    public void DrawBackground()
    {
        Refresh(true);
        if (!disposed && movement.IndicatorId == null) highlight.Draw(groupBounds, hovered);
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
        BeginMovement(new PointF(args.X, args.Y));
        args.Handled = movement.Dragging;
        if (args.Handled) relayout();
    }

    /// <summary>Starts whole-group movement from its caption or cancels captured movement on right-click.</summary>
    public void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        // Cancellation belongs to the captured drag, including member bodies without a titlebar.
        if (movement.Dragging && args.Button == EnumMouseButton.Right)
        {
            Cancel();
            args.Handled = true;
            return;
        }
        if (!HasTitleBar) return;
        Refresh(false);
        if (args.Handled || !visible || args.Button != EnumMouseButton.Left) return;
        if (!handle.Contains(args.X, args.Y)) return;
        if (movement.Movable) BeginMovement(new PointF(args.X, args.Y));
        else return;
        args.Handled = true;
    }

    /// <summary>Updates content preview from total pointer displacement.</summary>
    public void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Handled) { Cancel(); return; }
        Refresh(false);
        if (!Active) return;
        args.Handled = true;
        if (!movement.Dragging) return;
        movement.Move(new PointF(args.X, args.Y));
        relayout();
        snap?.Invoke(movement, new PointF(args.X, args.Y));
    }

    /// <summary>Validates and persists a successful content drop or restores its source.</summary>
    public void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Handled) { Cancel(); return; }
        Refresh(false);
        if (!Active) return;
        args.Handled = true;
        if (args.Button != EnumMouseButton.Left) return;
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
        relayout();
        Refresh(false);
    }

    #endregion
    #region Keyboard and cancellation
    /// <summary>Discards preview without writing a dragged position.</summary>
    public bool Cancel()
    {
        if (disposed) return false;
        bool active = Active;
        movement.Cancel();
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
        try { ReleaseCaption(); }
        finally
        {
            try { highlight.Dispose(); }
            finally { group = null; }
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
    /// <summary>Captures source slots once, immediately before detachment can change native group geometry.</summary>
    private void BeginMovement(PointF pointer)
    {
        var window = api.Gui.WindowBounds;
        movement.SourceGeometry = HudOverlaySourceReservation.Capture(group!, new PointF((float)window.renderX, (float)window.renderY), scale);
        movement.Begin(ContentOrigin(), pointer, scale);
    }
    /// <summary>Uses visible content for targeting and retains the group grip during pointer transit.</summary>
    private void Refresh(bool checkButton)
    {
        if (disposed) return;
        var input = api.Input;
        var pointer = new PointF(input.MouseX, input.MouseY);
        double currentScale = RuntimeEnv.GUIScale;
        bool available = HudOverlayEditing.IsActive(api) && group is { Available: true }
            && group.Members.Any(member => (movement.IndicatorId == null || member.Registration.Id == movement.IndicatorId) && drawable(member.Registration) && member.Clip.Width > 0 && member.Clip.Height > 0);
        // The outline is a global movement cue, independent of pointer hover over an unrelated window.
        // Modal input capture still disables movement; ordinary foreground windows only suppress hover controls.
        showOutline = available && !(api.Gui.OpenedGuis?.Any(dialog =>
            !ReferenceEquals(dialog, host) && dialog.IsOpened() && HudOverlayEditing.BlocksAllInput(dialog)) ?? false);
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
            || (checkButton && movement.Dragging && input.MouseButton is { Left: false }))
        {
            Cancel();
            return;
        }
        scale = currentScale;
        bool overContent = group!.Members.Any(member => drawable(member.Registration)
            && (movement.IndicatorId == null || member.Registration.Id == movement.IndicatorId) && member.Clip.Contains(pointer));
        if (!HasTitleBar)
        {
            // Individual and singleton controls have no caption resources or invisible grip hit area.
            ReleaseCaption();
            handle = RectangleF.Empty;
            visible = Active || overContent;
            hovered = movement.IndicatorId == null && overContent;
            return;
        }

        var window = api.Gui.WindowBounds;
        var viewport = new RectangleF((float)window.renderX, (float)window.renderY,
            (float)window.OuterWidth, (float)window.OuterHeight);
        handle = PlaceHandle(content, viewport, scale);
        hovered = overContent || Active;
        visible = Active || overContent || (visible && RectangleF.Union(content, handle).Contains(pointer));
        if (visible)
        {
            if (composer == null)
            {
                compositionBounds = ElementBounds.Fixed(0, 0, Width, HudOverlaySizes.TitleBarHeight).WithParent(api.Gui.WindowBounds);
                composer = api.Gui.CreateCompo(movement.Key, compositionBounds);
                titleBar = new HudOverlayGroupTitleBar(api, titleBounds);
                composer.AddStaticElement(titleBar);
            }
            PositionComposition(viewport);
            string key = "vanillaexpanded:hud-group-" + movement.Id;
            string title = Lang.HasTranslation(key) ? Lang.Get(key) : Lang.Get("vanillaexpanded:hud-custom-group");
            // Retain the current label during transient snap identities rather than rebuilding chrome on each preview.
            if (movement.Dragging && caption != null) title = caption;
            if (!composer.Composed || preparedScale != scale || caption != title)
            {
                titleBar!.Text = title;
                if (composer.Composed) composer.ReCompose(); else composer.Compose(false);
                preparedScale = scale;
                caption = title;
                // Successful drops persist content origins, never the separate caption origin.
                PositionComposition(viewport);
            }
        }
    }

    /// <summary>Updates the separate native composition without modifying the content layout hierarchy.</summary>
    private void PositionComposition(RectangleF viewport)
    {
        compositionBounds!.fixedX = (handle.X - viewport.X) / scale;
        compositionBounds.fixedY = (handle.Y - viewport.Y) / scale;
        compositionBounds.fixedWidth = Bounds.fixedWidth = handle.Width / scale;
        compositionBounds.fixedHeight = Bounds.fixedHeight = handle.Height / scale;
        compositionBounds.MarkDirtyRecursive();
        compositionBounds.CalcWorldBounds();
    }

    /// <summary>Releases caption-only resources when a container becomes a singleton.</summary>
    private void ReleaseCaption()
    {
        var retired = composer;
        composer = null;
        titleBar = null;
        caption = null;
        try { retired?.Dispose(); }
        finally
        {
            // Native WithChild preserves a non-null parent. Clear both ends before reusing the
            // caption bounds after a singleton/group preview cycle, so the next composer owns them.
            titleBounds.ParentBounds?.ChildBounds.Remove(titleBounds);
            titleBounds.ParentBounds = null;
            compositionBounds?.ParentBounds?.ChildBounds.Remove(compositionBounds);
            compositionBounds = null;
        }
    }

    /// <summary>Defers to foreground windows and modal input ownership before accepting HUD controls.</summary>
    private bool Blocked(PointF pointer)
    {
        foreach (var dialog in api.Gui.OpenedGuis ?? Enumerable.Empty<GuiDialog>())
        {
            if (ReferenceEquals(dialog, host) || !dialog.IsOpened()) continue;
            if (HudOverlayEditing.BlocksAllInput(dialog)) return true;
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
