using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns native group composers, optional group controls, and prepared presentation dispatch.</summary>
internal sealed class HudOverlayHost : HudElement, IHudOverlayHost
{
    private readonly HudOverlayRegistry registry;
    private readonly Func<HudOverlayRegistration, bool> isDrawable;
    private readonly Action<HudOverlayRegistration, Exception> failure;
    private readonly Action<Exception>? boundaryFailure;
    private readonly HudOverlayHoverLabels hoverLabels;
    private readonly IHudOverlayBackdropBatch? backdropBatch;
    private readonly Dictionary<string, HudOverlayGroupControls> groupControls = new(StringComparer.Ordinal);
    private readonly HudOverlayContainers? containers;
    private readonly HudOverlaySnapping? snapping;
    private readonly HudOverlayNativeBoundaries nativeBoundaries;
    private readonly HudSnapHighlight snapHighlight;
    private readonly HudOverlayEditPreviews? editPreviews;
    private readonly HudOverlayMenuInput? menuInput;
    private readonly HudOverlayArtworkDialog artworkDialog;
    private readonly HudOverlayDragPreview dragPreview = new();
    private bool updatingSnap;
    private HudOverlayGroupControls? ActiveControls => groupControls.Values.FirstOrDefault(control => control.Active);
    private readonly Dictionary<string, HudOverlayGroupLayout> groups = new(StringComparer.Ordinal);
    private HashSet<string> visibleGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<HudOverlayMemberLayout, ElementBounds> clips = new();
    private HashSet<HudOverlayRegistration> active = new();
    private bool disposed, rendering, synchronizing;
    public Action? BeforeRender { get; set; }
    public override bool Focusable => menuInput?.Capturing == true;
    public override bool UnregisterOnClose => true;
    /// <summary>Places targeted HUD popups above the pause menu while artwork stays in its passive slot.</summary>
    public override double DrawOrder => .9;
    /// <summary>Routes targeted edits before the escape menu consumes pointer input.</summary>
    public override double InputOrder => -.1;
    #region Public API
    #region Composition
    /// <summary>Borrows registry and prepared-state decisions; owns only composers allocated for this session.</summary>
    public HudOverlayHost(ICoreClientAPI api, HudOverlayRegistry registry, Func<HudOverlayRegistration, bool> isDrawable,
        Action<HudOverlayRegistration, Exception> failure, Action<Exception>? boundaryFailure = null,
        IHudOverlayBackdropBatch? backdropBatch = null, HudOverlayContainers? containers = null,
        HudOverlayEditPreviews? editPreviews = null, HudOverlayContextMenu? contextMenu = null) : base(api)
    {
        this.registry = registry;
        this.isDrawable = isDrawable;
        this.failure = failure;
        this.boundaryFailure = boundaryFailure;
        hoverLabels = new HudOverlayHoverLabels(api);
        this.backdropBatch = backdropBatch ?? api.ModLoader?.GetModSystem<HudOverlaySystem>()?.Backdrop;
        this.containers = containers;
        this.editPreviews = editPreviews;
        menuInput = containers == null ? null : new HudOverlayMenuInput(api, this, containers, isDrawable, contextMenu);
        snapping = containers == null ? null : new HudOverlaySnapping(containers);
        nativeBoundaries = new HudOverlayNativeBoundaries(api);
        snapHighlight = new HudSnapHighlight(api);
        artworkDialog = new HudOverlayArtworkDialog(api, DrawArtwork);

    }
    /// <summary>Registers a live session host through the native GUI lifecycle without taking focus.</summary>
    public override bool TryOpen(bool withFocus)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        artworkDialog.TryOpen(false);
        return base.TryOpen(false);
    }
    /// <summary>Reuses existing native compositions; allocates a composer only for a newly drawable group.</summary>
    public void Synchronize(IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        HashSet<string>? visibleControls = null;
        visibleGroups = layouts.Keys.ToHashSet(StringComparer.Ordinal);
        if (containers?.ActiveContainer == null) RetireContent(layouts);
        foreach (var pair in layouts)
        {
            if (!pair.Value.Available || groups.ContainsKey(pair.Key)) continue;
            var dispatchBounds = ElementBounds.Fill.WithParent(pair.Value.Root);
            var element = new HudOverlayGroupElement(capi, dispatchBounds, dt => DrawGroup(pair.Value.Members, dt));
            Composers[pair.Key] = capi.Gui.CreateCompo("vanillaexpanded:hud:content:group:" + pair.Key, pair.Value.Root)
                .OnlyDynamic().AddInteractiveElement(element).Compose(false);
            groups.Add(pair.Key, pair.Value);
        }
        var retained = (containers?.ActiveContainer != null ? groups.Values : layouts.Values)
            .SelectMany(group => group.Members).Concat(dragPreview.Originals).ToHashSet();
        foreach (var member in clips.Keys.Where(member => !retained.Contains(member)).ToArray()) clips.Remove(member);
        if (containers != null)
        {
            var targets = new Dictionary<string, HudOverlayGroupMovement>(StringComparer.Ordinal);
            foreach (var pair in layouts)
            {
                var captured = groupControls.FirstOrDefault(item => item.Value.Movement.Dragging
                    && item.Value.Movement.IndicatorId == null && item.Value.Movement.Id == pair.Key);
                if (captured.Value != null) targets[captured.Key] = captured.Value.Movement;
                else targets["container:" + pair.Key] = containers.Get(pair.Key);
                // A singleton's container grip already moves just that indicator; multi-member groups expose separate grips.
                bool multiple = containers.Resolved.FirstOrDefault(item => item.Id == pair.Key)?.Tree.Leaves().Count() > 1;
                foreach (var member in pair.Value.Members.Where(member => isDrawable(member.Registration)))
                {
                    var individual = containers.Get(pair.Key, member.Registration.Id);
                    // Detachment makes this a singleton before release. Retain its captured member grip until commit/cancel.
                    if (multiple || individual.Dragging) targets["member:" + member.Registration.Id] = individual;
                }
            }
            foreach (string id in groupControls.Keys.Where(id => !targets.ContainsKey(id)).ToArray())
            {
                if (containers.ActiveContainer != null) continue;
                groupControls[id].Dispose(); groupControls.Remove(id);
            }
            foreach (var target in targets)
                if (!groupControls.ContainsKey(target.Key)) groupControls[target.Key] = new HudOverlayGroupControls(capi, this,
                    target.Value, isDrawable, RequestLayout, UpdateSnap);
            visibleControls = targets.Keys.ToHashSet(StringComparer.Ordinal);
        }
        synchronizing = true;
        try
        {
            var empty = new Dictionary<string, HudOverlayGroupLayout>();
            foreach (var pair in groupControls.ToArray())
                pair.Value.Synchronize(visibleControls?.Contains(pair.Key) != false ? layouts : empty);
        }
        finally { synchronizing = false; }
        // Synchronizing an unavailable capture can cancel it. Release its caches in this same boundary.
        if (containers?.ActiveContainer == null)
        {
            RetireContent(layouts);
            foreach (string id in groupControls.Keys.Where(id => visibleControls?.Contains(id) == false).ToArray())
            {
                groupControls[id].Dispose(); groupControls.Remove(id);
            }
        }
        menuInput?.Synchronize(Composers.Where(pair => layouts.ContainsKey(pair.Key)).Select(pair => layouts[pair.Key]).ToArray());
    }
    #endregion
    #region Input
    /// <summary>Receives keyboard events during captured movement or context-menu interaction.</summary>
    public override bool ShouldReceiveKeyboardEvents() => menuInput?.Capturing == true || ActiveControls?.Active == true;
    /// <summary>Allows selective handle routing while retaining passive behavior for hosts without controls.</summary>
    public override bool ShouldReceiveMouseEvents() => groupControls.Count > 0 && IsOpened() && HudOverlayEditing.IsActive(capi);
    /// <summary>Prioritizes menu navigation and drag cancellation over the pause menu's keyboard capture.</summary>
    public override bool CaptureAllInputs() => menuInput?.Capturing == true || ActiveControls?.Active == true;
    /// <summary>Leaves raw mouse ownership with the engine.</summary>
    public override bool CaptureRawMouse() => false;
    /// <summary>Cancels active dragging instead of closing the HUD or saving its preview.</summary>
    public override bool OnEscapePressed()
    {
        if (menuInput?.Capturing == true) { menuInput.Escape(); return true; }
        return ActiveControls?.Cancel() == true;
    }
    /// <summary>Routes menu navigation and consumes Escape when it cancels an active interaction.</summary>
    public override void OnKeyDown(KeyEvent args)
    {
        if (menuInput?.Capturing == true) { menuInput.KeyDown(args); return; }
        if (args.KeyCode == (int)GlKeys.Escape && OnEscapePressed()) args.Handled = true;
        RefreshSnap();
    }
    /// <summary>Routes characters only to an open native dropdown.</summary>
    public override void OnKeyPress(KeyEvent args)
    {
        if (menuInput?.Capturing == true) menuInput.KeyPress(args);
    }
    /// <summary>Rechecks Shift bypass without requiring additional pointer movement.</summary>
    public override void OnKeyUp(KeyEvent args)
    {
        if (menuInput?.Capturing == true) menuInput.KeyUp(args);
        RefreshSnap();
    }
    /// <summary>Prioritizes native chrome, then allows direct dragging of visible indicators without capturing group gaps.</summary>
    public override void OnMouseDown(MouseEvent args)
    {
        var activeControl = ActiveControls;
        // A captured drag owns cancellation before popup targeting. An already open popup owns the next
        // press before chrome or body dragging, and opening never forwards that same press into a row.
        if (activeControl?.Movement.Dragging == true && args.Button == Vintagestory.API.Common.EnumMouseButton.Right)
        {
            activeControl.OnMouseDown(capi, args);
            return;
        }
        if (menuInput?.Capturing == true) { menuInput.MouseDown(args); return; }
        if (activeControl == null && menuInput?.TryOpen(args) == true) return;
        var starting = activeControl == null && args.Button == Vintagestory.API.Common.EnumMouseButton.Left && !args.Handled
            && groupControls.Count > 0 && HudOverlayEditing.IsActive(capi) ? HudOverlayDragPreview.Capture(groups.Where(pair => visibleGroups.Contains(pair.Key)).Select(pair => pair.Value)) : null;
        try
        {
            if (activeControl != null) activeControl.OnMouseDown(capi, args);
            else foreach (var control in groupControls.Values.Reverse().ToArray())
            {
                control.OnMouseDown(capi, args);
                if (args.Handled) break;
            }
            if (args.Handled || activeControl != null) return;
            // Member bodies detach one item; the container titlebar continues to move the complete group.
            foreach (var control in groupControls.Values.Reverse().OrderByDescending(control => control.Movement.IndicatorId != null).ToArray())
            {
                control.OnContentMouseDown(capi, args);
                if (args.Handled) break;
            }
        }
        finally
        {
            if (starting != null && ActiveControls?.Movement is { Dragging: true } movement) dragPreview.Begin(movement, starting);
        }
    }
    /// <summary>Routes active release delivery even after the pointer leaves the starting bounds.</summary>
    public override void OnMouseUp(MouseEvent args)
    {
        if (menuInput?.Capturing == true) menuInput.MouseUp(args);
        else ActiveControls?.OnMouseUp(capi, args);
    }
    /// <summary>Routes active pointer movement without taking ordinary gameplay mouse ownership.</summary>
    public override void OnMouseMove(MouseEvent args)
    {
        if (menuInput?.Capturing == true) menuInput.MouseMove(args);
        else ActiveControls?.OnMouseMove(capi, args);
    }
    /// <summary>Routes wheel input only while the context menu owns input.</summary>
    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (menuInput?.Capturing == true) menuInput.MouseWheel(args);
    }
    /// <summary>Discards active interaction when native dialog focus is transferred elsewhere.</summary>
    public override void UnFocus()
    {
        menuInput?.Close(false);
        foreach (var control in groupControls.Values.ToArray()) control.Cancel();
        base.UnFocus();
    }
    /// <summary>Cancels transient positioning before native close and deregistration.</summary>
    public override bool TryClose()
    {
        menuInput?.Close();
        foreach (var control in groupControls.Values.ToArray()) control.Cancel();
        artworkDialog.TryClose();
        return base.TryClose();
    }
    #endregion
    #region Rendering and lifecycle
    /// <summary>Draws the focused popup in its native dialog slot, independently of passive HUD artwork.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        if (disposed || !IsOpened()) return;
        menuInput?.Draw(deltaTime);
    }

    /// <summary>No post-render work is needed by these dynamic-only presentations.</summary>
    public override void OnFinalizeFrame(float dt) { }
    /// <summary>Closes through native registration before disposing owned composers exactly once.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        BeforeRender = null;
        var failures = new List<Exception>();
        // A failing native release must not strand the other controls or compositions on session replacement.
        foreach (var control in groupControls.Values) TryRelease(control.Dispose, failures);
        groupControls.Clear();
        if (menuInput != null) TryRelease(menuInput.Dispose, failures);
        TryRelease(artworkDialog.Dispose, failures);
        TryRelease(hoverLabels.Dispose, failures);
        TryRelease(snapHighlight.Dispose, failures);
        TryRelease(() => TryClose(), failures);
        foreach (var composer in Composers.Values.ToArray()) TryRelease(composer.Dispose, failures);
        foreach (var pair in ((IEnumerable<KeyValuePair<string, GuiComposer>>)Composers).ToArray()) Composers.Remove(pair.Key);
        TryRelease(() => base.Dispose(), failures);
        dragPreview.Clear();
        groups.Clear(); clips.Clear(); active.Clear();
        if (failures.Count > 0) throw new AggregateException(failures);
    }
    #endregion
    #endregion
    #region Private
    #region Interaction and layout lifetime
    /// <summary>Releases only compositions absent from the effective layout after a transaction has ended.</summary>
    private void RetireContent(IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts)
    {
        foreach (string id in groups.Keys.Where(id => !layouts.ContainsKey(id)).ToArray())
        {
            Composers[id].Dispose(); Composers.Remove(id); groups.Remove(id);
        }
    }
    /// <summary>Rechecks targets during stationary drags, including Shift, visibility, and size changes.</summary>
    private void RefreshSnap()
    {
        var movement = ActiveControls?.Movement;
        if (movement?.Dragging == true) UpdateSnap(movement, new PointF(capi.Input.MouseX, capi.Input.MouseY));
        else snapping?.Clear();
    }

    /// <summary>Resolves against unsnapped native bounds before publishing a no-write membership preview.</summary>
    private bool UpdateSnap(HudOverlayGroupMovement movement, PointF pointer)
    {
        if (updatingSnap || containers == null || snapping == null || !movement.Dragging) return false;
        updatingSnap = true;
        try
        {
            containers.ClearSnap(movement.UnsnappedOrigin);
            RequestLayout();
            var window = capi.Gui.WindowBounds;
            var viewport = new RectangleF((float)window.renderX, (float)window.renderY, (float)window.OuterWidth, (float)window.OuterHeight);
            var keys = capi.Input.KeyboardKeyStateRaw;
            bool shift = keys != null && (keys[(int)GlKeys.ShiftLeft] || keys[(int)GlKeys.ShiftRight]);
            snapping.Update(movement, pointer, shift, Vintagestory.API.Config.RuntimeEnv.GUIScale, viewport,
                groups.Where(item => visibleGroups.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value), isDrawable,
                nativeBoundaries.Resolve());
            RequestLayout();
            return snapping.DropAllowed;
        }
        finally { updatingSnap = false; }
    }
    /// <summary>Collects native cleanup failures while attempting every independent owned resource.</summary>
    private static void TryRelease(Action release, List<Exception> failures)
    {
        try { release(); } catch (Exception failure) { failures.Add(failure); }
    }
    /// <summary>Defers render-time cancellation reflow to the next frame, avoiding nested registry passes.</summary>
    private void RequestLayout()
    {
        if (!rendering && !synchronizing && !disposed) BeforeRender?.Invoke();
    }
    #endregion
    #region Prepared drawing
    /// <summary>Draws passive content in a stable HUD slot even while its separate menu owner is focused.</summary>
    private void DrawArtwork(float deltaTime)
    {
        if (disposed) return;
        BeforeRender?.Invoke();
        // Lifecycle invalidation also runs when hidden GUI suppresses normal drawing.
        _ = menuInput?.IsOpened;
        RefreshSnap();
        dragPreview.Refresh(snapping?.Selected?.Grouping == true);
        if (!IsOpened() || capi.HideGuis) return;
        // Keep consumers alive until all composer callbacks finish, including removals requested by drawing.
        rendering = true;
        try { registry.RunSnapshotPass(snapshot =>
        {
            active.UnionWith(snapshot);
            capi.Render.GlPushMatrix();
            try
            {
                capi.Render.GlTranslate(0, 0, -150);
                CollectBackdrops();
                try { backdropBatch?.Flush(capi.Render); }
                catch (Exception error) { boundaryFailure?.Invoke(error); }
                foreach (var control in groupControls.Values.ToArray()) control.DrawBackground();
                foreach (var pair in Composers) if (visibleGroups.Contains(pair.Key) && groups[pair.Key].Available) pair.Value.Render(deltaTime);
                if (dragPreview.Originals.Count > 0)
                {
                    CollectBackdrops(true);
                    try { backdropBatch?.Flush(capi.Render); }
                    catch (Exception error) { boundaryFailure?.Invoke(error); }
                }
                // Source traces remain available during free dragging; a group preview displays only the destination.
                DrawGroup(dragPreview.Originals, 0);
                hoverLabels.Draw(groups.Where(pair => visibleGroups.Contains(pair.Key)).Select(pair => pair.Value),
                    registration => active.Contains(registration) && isDrawable(registration), deltaTime,
                    registration => editPreviews?.IsDrawable(registration) == true ? editPreviews.Label(registration)
                        : Vintagestory.API.Config.Lang.Get(registration.HoverLabelKey!));
                // Foreground editing cues occupy a later sublayer of this HUD slot. Their shaders
                // retain depth testing, so foreground native dialogs still occlude every cue uniformly.
                capi.Render.GlPushMatrix();
                try
                {
                    capi.Render.GlTranslate(0, 0, 100);
                    foreach (var control in groupControls.Values.ToArray()) control.Draw(deltaTime);
                    if (menuInput?.HoveredMember() is { } hovered)
                        HudOverlayHoverBorder.Draw(capi.Render, hovered.Clip);
                    if (ActiveControls?.Movement.Dragging == true && snapping?.Selected is { } selected)
                    {
                        if (selected.Grouping) snapHighlight.DrawConnection(snapping.ConnectionTarget, snapping.Insertion, selected.Side);
                        else snapHighlight.DrawGuide(selected.Guide);
                    }
                }
                finally { capi.Render.GlPopMatrix(); }
            }
            finally { backdropBatch?.Clear(); capi.Render.GlPopMatrix(); active.Clear(); }
        }, boundaryFailure); }
        finally { rendering = false; }
    }
    /// <summary>Collects only visible prepared providers, applying the same membership and clipping gates as foreground drawing.</summary>
    private void CollectBackdrops(bool original = false)
    {
        if (backdropBatch == null) return;
        backdropBatch.Clear();
        IEnumerable<HudOverlayMemberLayout> members = original ? dragPreview.Originals
            : groups.Where(pair => visibleGroups.Contains(pair.Key) && pair.Value.Available && Composers[pair.Key].Enabled)
                .SelectMany(pair => pair.Value.Members);
        foreach (var member in members)
        {
            if (!active.Contains(member.Registration) || !isDrawable(member.Registration)
                || editPreviews?.IsDrawable(member.Registration) == true
                || member.Clip.Width <= 0 || member.Clip.Height <= 0
                || member.Registration.Overlay is not IHudOverlayBackdropProvider provider) continue;
            using var opacity = new DrawOpacity(member.Registration.Overlay,
                dragPreview.Contains(member.Registration) ? HudOverlayDragPreview.Opacity : 1);
            try { backdropBatch.Collect(member.Clip, () => provider.CollectBackdrops(capi.Render, member.Bounds)); }
            catch (Exception error) { failure(member.Registration, error); }
        }
    }
    /// <summary>Applies the engine scissor stack around each bounded presentation, restoring it even after a consumer error.</summary>
    private void DrawGroup(IEnumerable<HudOverlayMemberLayout> members, float deltaTime)
    {
        foreach (var member in members)
        {
            if (!active.Contains(member.Registration) || !isDrawable(member.Registration) || member.Clip.Width <= 0 || member.Clip.Height <= 0) continue;
            if (!clips.TryGetValue(member, out var clip)) clips[member] = clip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
            // Scissor receives initialized absolute pixel bounds, avoiding a second scale conversion.
            clip.absFixedX = member.Clip.X;
            clip.absFixedY = member.Clip.Y;
            clip.absInnerWidth = member.Clip.Width;
            clip.absInnerHeight = member.Clip.Height;
            clip.Initialized = true;
            bool preview = editPreviews?.IsDrawable(member.Registration) == true;
            capi.Render.PushScissor(clip, true);
            try
            {
                using var opacity = new DrawOpacity(preview ? (object)editPreviews! : member.Registration.Overlay,
                    dragPreview.Contains(member.Registration) ? HudOverlayDragPreview.Opacity : 1);
                if (preview) editPreviews!.Draw(member.Registration, capi.Render, member.Bounds);
                else
                {
                    member.Registration.Overlay.Draw(capi.Render, member.Bounds, member.Clip, deltaTime);
                }
            }
            catch (Exception error)
            {
                // Placeholder rendering is host-owned; its failure must not mark a healthy feature sample as failed.
                if (preview) boundaryFailure?.Invoke(error); else failure(member.Registration, error);
            }
            finally { capi.Render.PopScissor(); }
        }
    }
    #endregion
    #endregion
}
