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
    private readonly HudOverlayGroupHighlight snapHighlight;
    private bool updatingSnap;
    private HudOverlayGroupControls? ActiveControls => groupControls.Values.FirstOrDefault(control => control.Active);
    private readonly Dictionary<string, HudOverlayGroupLayout> groups = new(StringComparer.Ordinal);
    private HashSet<string> visibleGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<HudOverlayMemberLayout, ElementBounds> clips = new();
    private HashSet<HudOverlayRegistration> active = new();
    private bool disposed, rendering, synchronizing;
    public Action? BeforeRender { get; set; }
    public override bool Focusable => false;
    public override bool UnregisterOnClose => true;
    public override double DrawOrder => .1;
    #region Public API
    #region Composition
    /// <summary>Borrows registry and prepared-state decisions; owns only composers allocated for this session.</summary>
    public HudOverlayHost(ICoreClientAPI api, HudOverlayRegistry registry, Func<HudOverlayRegistration, bool> isDrawable,
        Action<HudOverlayRegistration, Exception> failure, Action<Exception>? boundaryFailure = null,
        IHudOverlayBackdropBatch? backdropBatch = null, HudOverlayContainers? containers = null) : base(api)
    {
        this.registry = registry;
        this.isDrawable = isDrawable;
        this.failure = failure;
        this.boundaryFailure = boundaryFailure;
        hoverLabels = new HudOverlayHoverLabels(api);
        this.backdropBatch = backdropBatch ?? api.ModLoader?.GetModSystem<HudOverlaySystem>()?.Backdrop;
        this.containers = containers;
        snapping = containers == null ? null : new HudOverlaySnapping(containers);
        snapHighlight = new HudOverlayGroupHighlight(api);

    }
    /// <summary>Registers a live session host through the native GUI lifecycle without taking focus.</summary>
    public override bool TryOpen(bool withFocus)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
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
            var element = new HudOverlayGroupElement(capi, dispatchBounds, dt => DrawGroup(pair.Value, dt));
            Composers[pair.Key] = capi.Gui.CreateCompo("vanillaexpanded:hud:content:group:" + pair.Key, pair.Value.Root)
                .OnlyDynamic().AddInteractiveElement(element).Compose(false);
            groups.Add(pair.Key, pair.Value);
        }
        var retained = (containers?.ActiveContainer != null ? groups.Values : layouts.Values)
            .SelectMany(group => group.Members).ToHashSet();
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
                    target.Value, isDrawable, RequestLayout, () => containers.Reset(), UpdateSnap);
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
    }
    #endregion
    #region Input
    /// <summary>Receives keyboard events only during dragging or native dropdown interaction.</summary>
    public override bool ShouldReceiveKeyboardEvents() => ActiveControls?.Active == true;
    /// <summary>Allows selective handle routing while retaining passive behavior for hosts without controls.</summary>
    public override bool ShouldReceiveMouseEvents() => groupControls.Count > 0 && IsOpened();
    /// <summary>Leaves general input ownership with the engine.</summary>
    public override bool CaptureAllInputs() => false;
    /// <summary>Leaves raw mouse ownership with the engine.</summary>
    public override bool CaptureRawMouse() => false;
    /// <summary>Cancels active dragging instead of closing the HUD or saving its preview.</summary>
    public override bool OnEscapePressed() => ActiveControls?.Cancel() == true;
    /// <summary>Routes native dropdown navigation, consuming Escape only when it cancels an active interaction.</summary>
    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode == (int)GlKeys.Escape && OnEscapePressed()) args.Handled = true;
        else ActiveControls?.OnKeyDown(args);
        RefreshSnap();
    }
    /// <summary>Routes characters only to an open native dropdown.</summary>
    public override void OnKeyPress(KeyEvent args) => ActiveControls?.OnKeyPress(args);
    /// <summary>Rechecks Shift bypass without requiring additional pointer movement.</summary>
    public override void OnKeyUp(KeyEvent args) { RefreshSnap(); }
    /// <summary>Prioritizes native chrome, then allows direct dragging of visible indicators without capturing group gaps.</summary>
    public override void OnMouseDown(MouseEvent args)
    {
        var activeControl = ActiveControls;
        if (activeControl != null) activeControl.OnMouseDown(capi, args);
        else foreach (var control in groupControls.Values.Reverse().ToArray())
        {
            control.OnMouseDown(capi, args);
            if (args.Handled) break;
        }
        if (args.Handled || activeControl != null) return;
        // Prefer member controls so a body press detaches only that indicator, even when its group is also hovered.
        foreach (var control in groupControls.Values.Reverse().OrderByDescending(control => control.Movement.IndicatorId != null).ToArray())
        {
            control.OnContentMouseDown(capi, args);
            if (args.Handled) break;
        }
    }
    /// <summary>Routes active release delivery even after the pointer leaves the starting bounds.</summary>
    public override void OnMouseUp(MouseEvent args) => ActiveControls?.OnMouseUp(capi, args);
    /// <summary>Routes active pointer movement without taking ordinary gameplay mouse ownership.</summary>
    public override void OnMouseMove(MouseEvent args) => ActiveControls?.OnMouseMove(capi, args);
    /// <summary>Routes wheel input only to an open native dropdown under the pointer.</summary>
    public override void OnMouseWheel(MouseWheelEventArgs args) => ActiveControls?.OnMouseWheel(args);
    /// <summary>Discards active interaction when native dialog focus is transferred elsewhere.</summary>
    public override void UnFocus()
    {
        foreach (var control in groupControls.Values.ToArray()) control.Cancel();
        base.UnFocus();
    }
    /// <summary>Cancels transient positioning before native close and deregistration.</summary>
    public override bool TryClose()
    {
        foreach (var control in groupControls.Values.ToArray()) control.Cancel();
        return base.TryClose();
    }
    #endregion
    #region Rendering and lifecycle
    /// <summary>Uses the native HUD depth transform with exception-safe restoration and stable registry membership.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        if (disposed) return;
        BeforeRender?.Invoke();
        RefreshSnap();
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
                if (ActiveControls?.Movement.Dragging == true && snapping?.Selected?.Grouping == true)
                    snapHighlight.Draw(snapping.Destination, true, false);
                foreach (var pair in Composers) if (visibleGroups.Contains(pair.Key) && groups[pair.Key].Available) pair.Value.Render(deltaTime);
                hoverLabels.Draw(groups.Where(pair => visibleGroups.Contains(pair.Key)).Select(pair => pair.Value),
                    registration => active.Contains(registration) && isDrawable(registration), deltaTime);
                foreach (var control in groupControls.Values.ToArray()) control.Draw(deltaTime);
                if (ActiveControls?.Movement.Dragging == true && snapping?.Selected is { } selected)
                {
                    if (selected.Grouping)
                    {
                        snapHighlight.Draw(snapping.Destination, false, true);
                        snapHighlight.Draw(snapping.Insertion, false, true);
                    }
                    else snapHighlight.DrawGuide(selected.Guide);
                }
            }
            finally { backdropBatch?.Clear(); capi.Render.GlPopMatrix(); active.Clear(); }
        }, boundaryFailure); }
        finally { rendering = false; }
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
        TryRelease(hoverLabels.Dispose, failures);
        TryRelease(snapHighlight.Dispose, failures);
        TryRelease(() => TryClose(), failures);
        foreach (var composer in Composers.Values.ToArray()) TryRelease(composer.Dispose, failures);
        foreach (var pair in ((IEnumerable<KeyValuePair<string, GuiComposer>>)Composers).ToArray()) Composers.Remove(pair.Key);
        TryRelease(() => base.Dispose(), failures);
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
                groups.Where(item => visibleGroups.Contains(item.Key)).ToDictionary(item => item.Key, item => item.Value), isDrawable);
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
    /// <summary>Collects only visible prepared providers, applying the same membership and clipping gates as foreground drawing.</summary>
    private void CollectBackdrops()
    {
        if (backdropBatch == null) return;
        backdropBatch.Clear();
        foreach (var pair in groups)
        {
            var group = pair.Value;
            if (!visibleGroups.Contains(pair.Key) || !group.Available || !Composers[pair.Key].Enabled) continue;
            foreach (var member in group.Members)
            {
                if (!active.Contains(member.Registration) || !isDrawable(member.Registration)
                    || member.Clip.Width <= 0 || member.Clip.Height <= 0
                    || member.Registration.Overlay is not IHudOverlayBackdropProvider provider) continue;
                using var opacity = new HudOverlayDrawOpacity(member.Registration.Overlay);
                try { backdropBatch.Collect(member.Clip, () => provider.CollectBackdrops(capi.Render, member.Bounds)); }
                catch (Exception error) { failure(member.Registration, error); }
            }
        }
    }
    /// <summary>Applies the engine scissor stack around each bounded presentation, restoring it even after a consumer error.</summary>
    private void DrawGroup(HudOverlayGroupLayout group, float deltaTime)
    {
        foreach (var member in group.Members)
        {
            if (!active.Contains(member.Registration) || !isDrawable(member.Registration) || member.Clip.Width <= 0 || member.Clip.Height <= 0) continue;
            if (!clips.TryGetValue(member, out var clip)) clips[member] = clip = ElementBounds.Fixed(0, 0, 0, 0).WithEmptyParent();
            // Scissor receives initialized absolute pixel bounds, avoiding a second scale conversion.
            clip.absFixedX = member.Clip.X;
            clip.absFixedY = member.Clip.Y;
            clip.absInnerWidth = member.Clip.Width;
            clip.absInnerHeight = member.Clip.Height;
            clip.Initialized = true;
            using var opacity = new HudOverlayDrawOpacity(member.Registration.Overlay);
            capi.Render.PushScissor(clip, true);
            try { member.Registration.Overlay.Draw(capi.Render, member.Bounds, member.Clip, deltaTime); }
            catch (Exception error) { failure(member.Registration, error); }
            finally { capi.Render.PopScissor(); }
        }
    }
    #endregion
    #endregion
}
