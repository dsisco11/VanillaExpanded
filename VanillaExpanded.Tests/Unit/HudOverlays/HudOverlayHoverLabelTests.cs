using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks hover eligibility without composing text or issuing graphics calls.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayHoverLabelTests : IDisposable
{
    private readonly string previousLocale = Lang.CurrentLocale;
    private readonly ITranslationService? previousLanguage;

    #region Public API
    /// <summary>Provides native localization without loading game assets.</summary>
    public HudOverlayHoverLabelTests()
    {
        Lang.AvailableLanguages.TryGetValue("en", out previousLanguage);
        Lang.AvailableLanguages["en"] = Mock.Of<ITranslationService>();
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
    }

    /// <summary>Restores shared localization after each test.</summary>
    public void Dispose()
    {
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, previousLocale);
        if (previousLanguage == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = previousLanguage;
    }

    /// <summary>Labels use the foreground pass above passive HUDs while respecting native window and visibility ownership.</summary>
    [Theory]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, true, false, true, false)]
    public void HoverLabelsUseForegroundPassWithoutCoveringWindowTargets(bool hud, bool modal, bool overlaps,
        bool hidden, bool grabbed, bool expected)
    {
        using var native = new HudNativeComponentFixture();
        var api = native.Api;
        var input = Mock.Get(api.Object.Input);
        input.SetupGet(value => value.MouseGrabbed).Returns(grabbed);
        api.SetupGet(value => value.HideGuis).Returns(hidden);
        native.Gui.SetupGet(value => value.LoadedGuis).Returns(new List<GuiDialog>());
        using var registry = new HudOverlayRegistry();
        var overlay = new Mock<IHudOverlay>();
        overlay.Setup(value => value.CreateView(api.Object, It.IsAny<NativeViewRole>()))
            .Returns(() => new CallbackView(api.Object));
        var registration = new HudOverlayRegistration("test:label", overlay.Object, () => true, "group",
            hoverLabelKey: "test:hover");
        var definition = new HudOverlayGroup("group", new HudOverlayPlacement("screen", HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop));
        registry.RegisterGroup(definition); registry.Register(registration);
        using var group = new HudOverlayGroupLayout();
        var window = ElementBounds.Fixed(0, 0, 700, 500).WithEmptyParent(); window.CalcWorldBounds();
        var anchors = new HudOverlayAnchorContext(window, () => 1, () => null); anchors.BeginFrame();
        group.Apply(anchors, definition, new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new(40, 30)) });
        var pointer = group.Members.Single().Clip;
        input.SetupGet(value => value.MouseX).Returns((int)pointer.Left + 1);
        input.SetupGet(value => value.MouseY).Returns((int)pointer.Top + 1);
        native.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { api.Object, bounds, name }, null)!);
        using var host = new HudOverlayHost(api.Object, registry, _ => true, (_, error) => throw error);
        host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { ["group"] = group }); host.TryOpen(false);
        var foreground = new Mock<GuiDialog>(api.Object);
        foreground.Setup(value => value.IsOpened()).Returns(true);
        foreground.Setup(value => value.CaptureAllInputs()).Returns(modal);
        foreground.SetupGet(value => value.DialogType).Returns(hud ? EnumDialogType.HUD : EnumDialogType.Dialog);
        var bounds = ElementBounds.Fixed(overlaps ? pointer.Left : pointer.Right + 100, pointer.Top, 60, 60).WithParent(window);
        bounds.CalcWorldBounds();
        foreground.Object.Composers["peer"] = api.Object.Gui.CreateCompo("peer", bounds);
        native.Gui.SetupGet(value => value.OpenedGuis).Returns(new List<GuiDialog> { host, foreground.Object });
        var labels = (HudOverlayHoverLabels)typeof(HudOverlayHost).GetField("hoverLabels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var tooltip = new Mock<GuiElementHoverText>(api.Object, "label", CairoFont.WhiteSmallText(), 280,
            ElementBounds.Fixed(0, 0, 1, 1).WithEmptyParent(), null!);
        typeof(HudOverlayHoverLabels).GetField("tooltip", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(labels, tooltip.Object);
        typeof(HudOverlayHoverLabels).GetField("text", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(labels, Lang.Get(registration.HoverLabelKey!));
        typeof(HudOverlayHoverLabels).GetField("scale", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(labels, (double)RuntimeEnv.GUIScale);
        var artwork = (GuiDialog)typeof(HudOverlayHost).GetField("artworkDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Assert.True(artwork.DrawOrder < host.DrawOrder);
        artwork.OnRenderGUI(.1f);
        tooltip.Verify(value => value.RenderInteractiveElements(It.IsAny<float>()), Times.Never);
        native.Render.Invocations.Clear();
        host.OnRenderGUI(.1f);
        tooltip.Verify(value => value.RenderInteractiveElements(.1f), expected ? Times.Once() : Times.Never());
        Assert.DoesNotContain(native.Render.Invocations, call => call.Method.Name == nameof(IRenderAPI.GlTranslate));
    }

    /// <summary>Only a labeled visible member under a free pointer exposes a hover target.</summary>
    [Theory]
    [InlineData(false, true, true, true, 25, 25, 20, true)]
    [InlineData(true, true, true, true, 25, 25, 20, false)]
    [InlineData(false, false, true, true, 25, 25, 20, false)]
    [InlineData(false, true, false, true, 25, 25, 20, false)]
    [InlineData(false, true, true, false, 25, 25, 20, false)]
    [InlineData(false, true, true, true, 50, 20, 20, false)]
    [InlineData(false, true, true, true, 25, 25, 0, false)]
    public void HitTestingRequiresVisibleLabeledMember(bool grabbed, bool available, bool drawable,
        bool labeled, float x, float y, float width, bool expected)
    {
        float oldScale = RuntimeEnv.GUIScale;
        int oldLeft = GuiStyle.LeftDialogMargin;
        try
        {
            RuntimeEnv.GUIScale = 1;
            GuiStyle.LeftDialogMargin = 0;
            var registration = new HudOverlayRegistration("test:indicator", Mock.Of<IHudOverlay>(), () => true, "group",
                hoverLabelKey: labeled ? "test:label" : null);
            using var group = new HudOverlayGroupLayout();
            var window = ElementBounds.Fixed(0, 0, 200, 100).WithEmptyParent();
            window.CalcWorldBounds();
            var anchors = new HudOverlayAnchorContext(window, () => 1, () => null);
            anchors.BeginFrame();
            group.Apply(anchors, new HudOverlayGroup("group", new HudOverlayPlacement(available ? "screen" : "hotbar",
                HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop, 5, 5)),
                new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new SizeF(width, 20)) });
            var result = HudOverlayHoverLabels.FindTarget(new[] { group }, _ => drawable, grabbed, new PointF(x, y));
            Assert.Equal(expected, result == registration);
        }
        finally
        {
            RuntimeEnv.GUIScale = oldScale;
            GuiStyle.LeftDialogMargin = oldLeft;
        }
    }
    #endregion
}
