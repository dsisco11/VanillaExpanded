using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Owns one container's composer generation and its independent visual instances.</summary>
internal sealed class Composition : IDisposable
{
    private readonly List<Binding> bindings = new();
    private readonly Action<HudOverlayRegistration, Exception> failure;
    private bool disposed;

    #region Public API
    #region Generation membership
    /// <summary>Gets the native composer whose registered elements own their generated resources.</summary>
    public GuiComposer Composer { get; }
    /// <summary>Identifies compositions borrowing prepared data from a retiring registration.</summary>
    public bool Contains(HudOverlayRegistration registration) =>
        bindings.Any(binding => ReferenceEquals(binding.Member.Registration, registration));
    /// <summary>Creates a generation without changing the surviving composition.</summary>
    public Composition(GuiComposer composer, Action<HudOverlayRegistration, Exception> failure)
    {
        Composer = composer; this.failure = failure;
    }
    /// <summary>Checks identities and prepared style revisions before deciding whether to replace elements.</summary>
    public bool Matches(IReadOnlyList<HudOverlayMemberLayout> members, Func<HudOverlayRegistration, bool> proxy,
        Func<HudOverlayRegistration, long> structure) => bindings.Count == members.Count
        && bindings.Zip(members).All(pair => ReferenceEquals(pair.First.Member, pair.Second)
            && pair.First.Proxy == proxy(pair.Second.Registration)
            && pair.First.Structure == structure(pair.Second.Registration));
    /// <summary>Mounts one independent visual under owned bounds, retaining cleanup after partial failure.</summary>
    public void Add(HudOverlayMemberLayout member, INativeView view, bool proxy, long structure)
    {
        if (member.Registration.MenuSettings?.Size is { } size) view = new SizedNativeView(view, size);
        var bounds = ElementBounds.Fixed(0, 0, 0, 0);
        var binding = new Binding(member, view, bounds, proxy, structure);
        bindings.Add(binding);
        view.AddTo(Composer, bounds, member.Registration.Id);
    }
    #endregion
    #region Preparation
    /// <summary>Updates changed content and geometry before either rendering pass, never resampling gameplay.</summary>
    public void Prepare(HudOverlayPreparationContext context, Func<HudOverlayRegistration, long> revision)
    {
        bool geometry = !Composer.Composed;
        foreach (var binding in bindings)
        {
            try
            {
                long next = revision(binding.Member.Registration);
                double sizeScale = binding.Member.Registration.MenuSettings?.Size?.Scale ?? 1;
                bool contentChanged = binding.Context != context || binding.Revision != next || binding.SizeScale != sizeScale;
                // A failed geometry finalization must remain pending even when resources and bounds
                // are unchanged on the retry (notably for failures owned by the edit-proxy boundary).
                binding.Changed = contentChanged || !binding.Ready;
                if (contentChanged)
                {
                    binding.View.Prepare(context);
                    binding.Context = context; binding.Revision = next; binding.SizeScale = sizeScale;
                }
                var member = binding.Member;
                var rectangle = new RectangleF((float)member.Bounds.renderX, (float)member.Bounds.renderY,
                    (float)member.Bounds.OuterWidth, (float)member.Bounds.OuterHeight);
                var origin = (Composer.Bounds.renderX, Composer.Bounds.renderY);
                if (binding.Rectangle != rectangle || binding.Clip != member.Clip
                    || binding.Origin != origin || binding.Scale != context.GuiScale)
                {
                    binding.Rectangle = rectangle; binding.Clip = member.Clip; binding.Changed = true;
                    binding.Origin = origin; binding.Scale = context.GuiScale;
                    var bounds = binding.Bounds;
                    bounds.fixedX = (rectangle.X - Composer.Bounds.renderX) / context.GuiScale;
                    bounds.fixedY = (rectangle.Y - Composer.Bounds.renderY) / context.GuiScale;
                    bounds.fixedWidth = rectangle.Width / context.GuiScale;
                    bounds.fixedHeight = rectangle.Height / context.GuiScale;
                    geometry = true;
                }
                binding.Ready = true;
            }
            catch (Exception error)
            {
                binding.Ready = false;
                binding.View.SetOpacity(0);
                failure(binding.Member.Registration, error);
            }
        }
        // Dynamic-only compositions need a native compose for membership, but ordinary movement and
        // content changes only resolve dirty bounds. Glyph and artwork resources stay in their elements.
        geometry |= Dirty(Composer.Bounds);
        if (!Composer.Composed) Composer.Compose(false);
        else if (geometry)
        {
            Composer.Bounds.MarkDirtyRecursive(); Composer.Bounds.CalcWorldBounds();
        }
        foreach (var binding in bindings.Where(binding => binding.Ready && (geometry || binding.Changed)))
        {
            try { binding.View.LayoutCompleted(binding.Clip); }
            catch (Exception error)
            {
                binding.Ready = false; binding.View.SetOpacity(0);
                failure(binding.Member.Registration, error);
            }
        }
    }
    #endregion
    #region Rendering state and retirement
    /// <summary>Publishes one frame's settled visibility to every visual before backdrop submission.</summary>
    public void SetFrame(Func<HudOverlayRegistration, float> opacity)
    {
        foreach (var binding in bindings)
        {
            try
            {
                binding.Opacity = binding.Ready ? opacity(binding.Member.Registration) : 0;
                binding.View.SetOpacity(binding.Opacity);
            }
            catch (Exception error) { failure(binding.Member.Registration, error); }
        }
    }
    /// <summary>Collects cached targets through the same clip and eligibility gates as foreground elements.</summary>
    public void CollectBackdrops(IRenderAPI renderer, IHudOverlayBackdropBatch batch)
    {
        if (!Composer.Enabled) return;
        foreach (var binding in bindings.Where(binding => binding.Ready && !binding.Proxy && binding.Opacity > 0
            && binding.Clip.Width > 0 && binding.Clip.Height > 0))
        {
            try { batch.Collect(binding.Clip, () => binding.View.CollectBackdrops(renderer)); }
            catch (Exception error) { failure(binding.Member.Registration, error); }
        }
    }
    /// <summary>Releases mounted elements before their view bookkeeping and detaches owned native roots.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var errors = new List<Exception>();
        try { Composer.Dispose(); } catch (Exception error) { errors.Add(error); }
        foreach (var binding in bindings)
        {
            Composer.Bounds.ChildBounds.Remove(binding.Bounds);
            binding.Bounds.ParentBounds = null;
            try { binding.View.Dispose(); } catch (Exception error) { errors.Add(error); }
        }
        bindings.Clear();
        if (errors.Count > 0) throw new AggregateException(errors);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Observes invalidation without calculating bounds or arranging unchanged components.</summary>
    private static bool Dirty(ElementBounds bounds) => !bounds.Initialized || bounds.RequiresRecalculation
        || bounds.ChildBounds.Any(Dirty);

    /// <summary>Retains only generation ownership, content revisions and geometry checkpoints.</summary>
    private sealed class Binding(HudOverlayMemberLayout member, INativeView view, ElementBounds bounds, bool proxy, long structure)
    {
        public readonly HudOverlayMemberLayout Member = member;
        public readonly INativeView View = view;
        public readonly ElementBounds Bounds = bounds;
        public readonly bool Proxy = proxy;
        public readonly long Structure = structure;
        public long Revision = -1;
        public HudOverlayPreparationContext? Context;
        public RectangleF Rectangle, Clip;
        public (double X, double Y) Origin;
        public double Scale, SizeScale;
        public bool Changed, Ready;
        public float Opacity;
    }
    #endregion
}
