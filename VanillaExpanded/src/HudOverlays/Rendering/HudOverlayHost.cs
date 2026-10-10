using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Owns passive native group composers and dispatches only prepared presentations.</summary>
internal sealed class HudOverlayHost : HudElement, IHudOverlayHost
{
    private readonly HudOverlayRegistry registry;
    private readonly Func<HudOverlayRegistration, bool> isDrawable;
    private readonly Action<HudOverlayRegistration, Exception> failure;
    private readonly Action<Exception>? boundaryFailure;
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
        Action<HudOverlayRegistration, Exception> failure, Action<Exception>? boundaryFailure = null) : base(api)
    {
        this.registry = registry;
        this.isDrawable = isDrawable;
        this.failure = failure;
        this.boundaryFailure = boundaryFailure;
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
    }
    #endregion
    #region Input
    /// <summary>Declines all keyboard delivery without changing event handling flags.</summary>
    public override bool ShouldReceiveKeyboardEvents() => false;
    /// <summary>Declines all mouse delivery without changing event handling flags.</summary>
    public override bool ShouldReceiveMouseEvents() => false;
    /// <summary>Leaves general input ownership with the engine.</summary>
    public override bool CaptureAllInputs() => false;
    /// <summary>Leaves raw mouse ownership with the engine.</summary>
    public override bool CaptureRawMouse() => false;
    /// <summary>Does not close or consume Escape.</summary>
    public override bool OnEscapePressed() => false;
    /// <summary>Ignores direct keyboard delivery.</summary>
    public override void OnKeyDown(KeyEvent args) { }
    /// <summary>Ignores direct character delivery.</summary>
    public override void OnKeyPress(KeyEvent args) { }
    /// <summary>Ignores direct key-release delivery.</summary>
    public override void OnKeyUp(KeyEvent args) { }
    /// <summary>Ignores direct mouse-button delivery.</summary>
    public override void OnMouseDown(MouseEvent args) { }
    /// <summary>Ignores direct mouse-release delivery.</summary>
    public override void OnMouseUp(MouseEvent args) { }
    /// <summary>Ignores direct pointer delivery.</summary>
    public override void OnMouseMove(MouseEvent args) { }
    /// <summary>Ignores direct wheel delivery.</summary>
    public override void OnMouseWheel(MouseWheelEventArgs args) { }
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
                foreach (var pair in Composers) if (groups[pair.Key].Available) pair.Value.Render(deltaTime);
            }
            finally { capi.Render.GlPopMatrix(); active.Clear(); }
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
            capi.Render.PushScissor(clip, true);
            try { member.Registration.Overlay.Draw(capi.Render, member.Bounds, member.Clip, deltaTime); }
            catch (Exception error) { failure(member.Registration, error); }
            finally { capi.Render.PopScissor(); }
        }
    }
    #endregion
}
