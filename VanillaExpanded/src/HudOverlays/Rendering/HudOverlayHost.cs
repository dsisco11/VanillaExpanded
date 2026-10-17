using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly HudOverlayGroupControls? controls;
    private readonly Dictionary<string, HudOverlayGroupLayout> groups = new(StringComparer.Ordinal);
    private readonly Dictionary<HudOverlayMemberLayout, ElementBounds> clips = new();
    private HashSet<HudOverlayRegistration> active = new();
    private bool disposed;
    public Action? BeforeRender { get; set; }
    public override bool Focusable => false;
    public override bool UnregisterOnClose => true;
    public override double DrawOrder => .1;
    #region Public API
    #region Composition
    /// <summary>Borrows registry and prepared-state decisions; owns only composers allocated for this session.</summary>
    public HudOverlayHost(ICoreClientAPI api, HudOverlayRegistry registry, Func<HudOverlayRegistration, bool> isDrawable,
        Action<HudOverlayRegistration, Exception> failure, Action<Exception>? boundaryFailure = null,
        IHudOverlayBackdropBatch? backdropBatch = null, HudOverlayGroupMovement? movement = null) : base(api)
    {
        this.registry = registry;
        this.isDrawable = isDrawable;
        this.failure = failure;
        this.boundaryFailure = boundaryFailure;
        hoverLabels = new HudOverlayHoverLabels(api);
        this.backdropBatch = backdropBatch ?? api.ModLoader?.GetModSystem<HudOverlaySystem>()?.Backdrop;
        if (movement != null)
            controls = new HudOverlayGroupControls(api, this, movement, isDrawable, () => BeforeRender?.Invoke());
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
        foreach (string id in groups.Keys.Where(id => !layouts.ContainsKey(id)).ToArray())
        {
            Composers[id].Dispose();
            Composers.Remove(id);
            groups.Remove(id);
        }
        foreach (var pair in layouts)
        {
            if (!pair.Value.Available || groups.ContainsKey(pair.Key)) continue;
            var dispatchBounds = ElementBounds.Fill.WithParent(pair.Value.Root);
            var element = new HudOverlayGroupElement(capi, dispatchBounds, dt => DrawGroup(pair.Value, dt));
            Composers[pair.Key] = capi.Gui.CreateCompo("vanillaexpanded:hud:" + pair.Key, pair.Value.Root)
                .OnlyDynamic().AddInteractiveElement(element).Compose(false);
            groups.Add(pair.Key, pair.Value);
        }
        var retained = layouts.Values.SelectMany(group => group.Members).ToHashSet();
        foreach (var member in clips.Keys.Where(member => !retained.Contains(member)).ToArray()) clips.Remove(member);
        controls?.Synchronize(layouts);
    }
    #endregion
    #region Input
    /// <summary>Receives keyboard events only during dragging or native dropdown interaction.</summary>
    public override bool ShouldReceiveKeyboardEvents() => controls?.Active == true;
    /// <summary>Allows selective handle routing while retaining passive behavior for hosts without controls.</summary>
    public override bool ShouldReceiveMouseEvents() => controls != null && IsOpened();
    /// <summary>Leaves general input ownership with the engine.</summary>
    public override bool CaptureAllInputs() => false;
    /// <summary>Leaves raw mouse ownership with the engine.</summary>
    public override bool CaptureRawMouse() => false;
    /// <summary>Cancels active dragging instead of closing the HUD or saving its preview.</summary>
    public override bool OnEscapePressed() => controls?.Cancel() == true;
    /// <summary>Routes native dropdown navigation, consuming Escape only when it cancels an active interaction.</summary>
    public override void OnKeyDown(KeyEvent args)
    {
        if (args.KeyCode == (int)GlKeys.Escape && OnEscapePressed()) args.Handled = true;
        else controls?.OnKeyDown(args);
    }
    /// <summary>Routes characters only to an open native dropdown.</summary>
    public override void OnKeyPress(KeyEvent args) => controls?.OnKeyPress(args);
    /// <summary>Ignores direct key-release delivery.</summary>
    public override void OnKeyUp(KeyEvent args) { }
    /// <summary>Routes presses to the handle without consuming the content composer's entire rectangle.</summary>
    public override void OnMouseDown(MouseEvent args) => controls?.OnMouseDown(capi, args);
    /// <summary>Routes active release delivery even after the pointer leaves the starting bounds.</summary>
    public override void OnMouseUp(MouseEvent args) => controls?.OnMouseUp(capi, args);
    /// <summary>Routes active pointer movement without taking ordinary gameplay mouse ownership.</summary>
    public override void OnMouseMove(MouseEvent args) => controls?.OnMouseMove(capi, args);
    /// <summary>Routes wheel input only to an open native dropdown under the pointer.</summary>
    public override void OnMouseWheel(MouseWheelEventArgs args) => controls?.OnMouseWheel(args);
    /// <summary>Discards active interaction when native dialog focus is transferred elsewhere.</summary>
    public override void UnFocus()
    {
        controls?.Cancel();
        base.UnFocus();
    }
    /// <summary>Cancels transient positioning before native close and deregistration.</summary>
    public override bool TryClose()
    {
        controls?.Cancel();
        return base.TryClose();
    }
    #endregion
    #region Rendering and lifecycle
    /// <summary>Uses the native HUD depth transform with exception-safe restoration and stable registry membership.</summary>
    public override void OnRenderGUI(float deltaTime)
    {
        if (disposed) return;
        BeforeRender?.Invoke();
        if (!IsOpened() || capi.HideGuis) return;
        // Keep consumers alive until all composer callbacks finish, including removals requested by drawing.
        registry.RunSnapshotPass(snapshot =>
        {
            active.UnionWith(snapshot);
            capi.Render.GlPushMatrix();
            try
            {
                capi.Render.GlTranslate(0, 0, -150);
                CollectBackdrops();
                try { backdropBatch?.Flush(capi.Render); }
                catch (Exception error) { boundaryFailure?.Invoke(error); }
                controls?.DrawBackground();
                foreach (var pair in Composers) if (groups[pair.Key].Available) pair.Value.Render(deltaTime);
                hoverLabels.Draw(groups.Values, registration => active.Contains(registration) && isDrawable(registration), deltaTime);
                controls?.Draw(deltaTime);
            }
            finally { backdropBatch?.Clear(); capi.Render.GlPopMatrix(); active.Clear(); }
        }, boundaryFailure);
    }
    /// <summary>No post-render work is needed by these dynamic-only presentations.</summary>
    public override void OnFinalizeFrame(float dt) { }
    /// <summary>Closes through native registration before disposing owned composers exactly once.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        BeforeRender = null;
        controls?.Dispose();
        hoverLabels.Dispose();
        try { TryClose(); }
        finally
        {
            try { base.Dispose(); }
            finally
            {
                // Native dialog disposal releases composers but retains its collection entries.
                foreach (string id in groups.Keys) Composers.Remove(id);
                groups.Clear(); clips.Clear(); active.Clear();
            }
        }
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Collects only visible prepared providers, applying the same membership and clipping gates as foreground drawing.</summary>
    private void CollectBackdrops()
    {
        if (backdropBatch == null) return;
        backdropBatch.Clear();
        foreach (var pair in groups)
        {
            var group = pair.Value;
            if (!group.Available || !Composers[pair.Key].Enabled) continue;
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
}
