using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises the actual host and composer with native components over prepared synthetic data.</summary>
internal sealed class HudNativeHostFixture : IDisposable
{
    public readonly HudNativeComponentFixture Components = new();
    public readonly HudOverlayRegistry Registry = new();
    public readonly Mock<IHudOverlay> Overlay = new();
    public readonly List<ObservedView> Views = new();
    public readonly List<string> Calls = new();
    public readonly List<Exception> Failures = new();
    public readonly HudOverlayGroupLayout Layout = new();
    public readonly HudOverlayGroup Group = new("native", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
    public readonly HudOverlayAnchorContext Anchors;
    public readonly HudOverlayRegistration Registration;
    public readonly IDisposable Handle;
    public readonly HudOverlayHost Host;
    public readonly Mock<INativePresentation> Native;
    public long Revision, Structure;
    public int Composes;
    public int LayoutFailuresRemaining;
    public bool Drawable = true, FailMount, FailPrepare, FailCompose;
    public SizeF Size = new(100, 90);

    #region Public API
    /// <summary>Connects real native composition while retaining observable controller and resource lifetimes.</summary>
    public HudNativeHostFixture()
    {
        Components.Gui.SetupGet(gui => gui.LoadedGuis).Returns(new List<GuiDialog>());
        Components.Gui.Setup(gui => gui.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) =>
            {
                var composer = (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new object[] { Components.Api.Object, bounds, name }, null)!;
                composer.OnComposed += () => { Composes++; if (FailCompose) throw new InvalidOperationException("composition"); };
                return composer;
            });
        Native = Overlay.As<INativePresentation>();
        Native.Setup(native => native.CreateView(Components.Api.Object, It.IsAny<NativeViewRole>()))
            .Returns((ICoreClientAPI _, NativeViewRole role) =>
            {
                var view = new ObservedView(this, Components.CreateView(), role);
                Views.Add(view); return view;
            });
        Native.Setup(native => native.BeginFrame(It.IsAny<float>())).Callback(() => Calls.Add("frame"));
        Registry.RegisterGroup(Group);
        Registration = new("example:native", Overlay.Object, () => true, Group.Id);
        Handle = Registry.Register(Registration);
        Registry.BeginSession(Components.Api.Object);
        var window = ElementBounds.Fixed(0, 0, 700, 500).WithEmptyParent(); window.CalcWorldBounds();
        Anchors = new(window, () => Vintagestory.API.Config.RuntimeEnv.GUIScale, () => null);
        var batch = new Mock<IHudOverlayBackdropBatch>();
        batch.Setup(value => value.Collect(It.IsAny<RectangleF>(), It.IsAny<Action>()))
            .Callback<RectangleF, Action>((_, collect) => collect());
        batch.Setup(value => value.Flush(Components.Render.Object)).Callback(() => Calls.Add("flush"));
        Components.Render.Setup(value => value.Render2DTexturePremultipliedAlpha(It.IsAny<int>(), It.IsAny<float>(),
            It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vintagestory.API.MathTools.Vec4f>()))
            .Callback(() => Calls.Add("foreground"));
        Host = new(Components.Api.Object, Registry, _ => Drawable, (_, error) => Failures.Add(error), Failures.Add,
            backdropBatch: batch.Object, presentationRevision: _ => Revision, structureRevision: _ => Structure);
    }
    /// <summary>Packs the supplied canonical measurement before synchronizing its native components.</summary>
    public void Synchronize(PointF? position = null)
    {
        Anchors.BeginFrame();
        Layout.Apply(Anchors, Group, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(Registration, Size) }, position);
        Host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { [Group.Id] = Layout });
    }
    /// <summary>Runs the host's preparation barrier and both actual rendering passes.</summary>
    public void Render(float elapsed = .1f)
    {
        Host.TryOpen(false); HudOverlayNativeRenderPass.Render(Host, elapsed);
    }
    /// <summary>Releases composers before the registry's borrowed controller state.</summary>
    public void Dispose()
    {
        Host.Dispose(); Layout.Dispose(); Registry.Dispose(); Components.Dispose();
    }
    #endregion

    /// <summary>Observes native boundary calls while retaining actual composer-registered text and artwork.</summary>
    internal sealed class ObservedView(HudNativeHostFixture owner, INativeView view, NativeViewRole role) : INativeView
    {
        public readonly NativeViewRole Role = role;
        public int Prepares, Layouts, Disposals;
        public float Opacity;
        public RectangleF Clip;
        public readonly CountingBounds Probe = new();
        #region Public API
        /// <summary>Mounts the real components or supplies a failed-initialization seam.</summary>
        public void AddTo(GuiComposer composer, ElementBounds bounds, string prefix)
        {
            if (owner.FailMount) throw new InvalidOperationException("mount");
            view.AddTo(composer, bounds, prefix);
            bounds.WithChild(Probe);
        }
        /// <summary>Records resource preparation without introducing controller work.</summary>
        public void Prepare(HudOverlayPreparationContext context)
        {
            Prepares++;
            if (owner.FailPrepare) throw new InvalidOperationException("prepare");
            view.Prepare(context);
        }
        /// <summary>Records geometry finalization and forwards it to the registered components.</summary>
        public void LayoutCompleted(RectangleF clip)
        {
            Layouts++;
            if (owner.LayoutFailuresRemaining > 0)
            {
                owner.LayoutFailuresRemaining--; throw new InvalidOperationException("layout finalization");
            }
            Clip = clip; view.LayoutCompleted(clip);
        }
        /// <summary>Observes shared host opacity independently of glyph preparation.</summary>
        public void SetOpacity(float opacity) { Opacity = opacity; view.SetOpacity(opacity); }
        /// <summary>Records the prepared geometry consumed by backdrop submission.</summary>
        public void CollectBackdrops(IRenderAPI renderer)
        {
            owner.Calls.Add(Role == NativeViewRole.Live ? "live backdrop" : "source backdrop");
            view.CollectBackdrops(renderer);
        }
        /// <summary>Observes bookkeeping release while composer ownership stays with real elements.</summary>
        public void Dispose() { Disposals++; view.Dispose(); }
        #endregion
    }

    /// <summary>Counts real native bounds resolution without asserting chosen visual coordinates.</summary>
    internal sealed class CountingBounds : ElementBounds
    {
        public int Calculations;
        #region Public API
        /// <summary>Observes native calculation while retaining its normal semantics.</summary>
        public override void CalcWorldBounds() { Calculations++; base.CalcWorldBounds(); }
        #endregion
    }
}
