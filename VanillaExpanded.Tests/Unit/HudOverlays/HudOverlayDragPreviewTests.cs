using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks borrowed drag copies, immutable starting geometry, and scoped presentation dispatch.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayDragPreviewTests : IDisposable
{
    private readonly float initialScale = RuntimeEnv.GUIScale;
    #region Public API
    /// <summary>Sets deterministic native geometry for each isolated case.</summary>
    public HudOverlayDragPreviewTests() => RuntimeEnv.GUIScale = 1;
    /// <summary>Restores the borrowed engine geometry setting.</summary>
    public void Dispose() => RuntimeEnv.GUIScale = initialScale;
    /// <summary>Individual capture copies only its member while container capture retains all starting members.</summary>
    [Theory]
    [InlineData(true, 70, 30, 120, 80)]
    [InlineData(false, 40, 20, 180, 90)]
    public void CaptureRemainsAtOriginalBoundsAndExpiresWithTransaction(bool individual, float width, float height, float x, float y)
    {
        using var storage = new HudLayoutFixture();
        var first = storage.Add("test:a"); var second = storage.Add("test:b");
        var containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
        containers.SetMovable("test", true, PointF.Empty);
        var movement = containers.Get("test", individual ? first.Id : null);
        using var layout = new HudOverlayGroupLayout();
        var anchors = new HudOverlayAnchorContext(ElementBounds.Fixed(0, 0, 800, 600).WithEmptyParent(), () => RuntimeEnv.GUIScale, () => null);
        anchors.BeginFrame();
        layout.Apply(anchors, containers.Definition(containers.Resolved.Single()), new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(first, new(width, height)), new(second, new SizeF(width, height)) });
        var originals = HudOverlayDragPreview.Capture(new[] { layout });
        var expected = originals.Select(member => (member.Bounds.renderX, member.Bounds.renderY, member.Bounds.OuterWidth, member.Bounds.OuterHeight)).ToArray();
        movement.Begin(PointF.Empty, PointF.Empty, 1);
        var preview = new HudOverlayDragPreview(); preview.Begin(movement, originals);
        Assert.Equal(individual ? 1 : 2, preview.Originals.Count);
        Assert.True(preview.Contains(first)); Assert.Equal(!individual, preview.Contains(second));
        layout.Root.renderOffsetX += x; layout.Root.renderOffsetY += y;
        Assert.Equal(expected, originals.Select(member => (member.Bounds.renderX, member.Bounds.renderY, member.Bounds.OuterWidth, member.Bounds.OuterHeight)).ToArray());
        movement.Move(new PointF(x, y));
        Assert.NotEmpty(preview.Originals);
        movement.Cancel(); Assert.Empty(preview.Originals); Assert.False(preview.Contains(first));
        movement.Begin(PointF.Empty, PointF.Empty, 1);
        Assert.Empty(preview.Originals); // A later transaction cannot resurrect the previous capture.
        movement.Cancel(); preview.Clear();
    }

    /// <summary>Actual native host dispatch draws both copies without advancing the source presentation or leaking opacity.</summary>
    [Theory]
    [InlineData(false, false, false, 40, 25, 130, 90)]
    [InlineData(true, false, false, 60, 35, 160, 120)]
    [InlineData(false, true, false, 50, 20, 120, 80)]
    [InlineData(false, false, true, 55, 30, 140, 100)]
    public void HostDrawsFrozenAndMovingCopyWithScopedOpacity(bool failSource, bool remove, bool drawable, float width, float height, float x, float y)
    {
        float oldScale = RuntimeEnv.GUIScale; RuntimeEnv.GUIScale = 1;
        try
        {
            using var storage = new HudLayoutFixture(); storage.Add("test:other");
            var overlayMock = new Mock<IHudOverlay>();
            var provider = overlayMock.As<IHudOverlayBackdropProvider>();
            var registration = new HudOverlayRegistration("test:a", overlayMock.Object, () => true, "test");
            using var registrationHandle = storage.Registry.Register(registration);
            var overlay = Mock.Get(registration.Overlay); var renderer = new Mock<IRenderAPI>();
            storage.Api.SetupGet(api => api.Render).Returns(renderer.Object);
            storage.Api.SetupGet(api => api.Input).Returns(Mock.Of<IInputAPI>());
            storage.Gui.SetupGet(gui => gui.LoadedGuis).Returns(new List<GuiDialog>());
            storage.Gui.Setup(gui => gui.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>())).Returns((string name, ElementBounds bounds) =>
                (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { storage.Api.Object, bounds, name }, null)!);
            var containers = new HudOverlayContainers(storage.Api.Object, storage.Registry);
            containers.SetMovable("test", true, PointF.Empty); var movement = containers.Get("test");
            using var layout = new HudOverlayGroupLayout();
            var window = ElementBounds.Fixed(0, 0, 800, 600).WithEmptyParent(); window.CalcWorldBounds();
            var anchors = new HudOverlayAnchorContext(window, () => 1, () => null); anchors.BeginFrame();
            layout.Apply(anchors, containers.Definition(containers.Resolved.Single()), new[] { new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new(width, height)) });
            var source = HudOverlayDragPreview.Capture(new[] { layout });
            int failures = 0; var draws = new List<(double X, double Y, float Dt, float Opacity)>();
            var backdrops = new List<(double X, float Opacity)>();
            var order = new List<string>();
            provider.Setup(value => value.CollectBackdrops(renderer.Object, It.IsAny<ElementBounds>())).Callback<IRenderAPI, ElementBounds>((_, bounds) => { backdrops.Add((bounds.renderX, HudOverlayDrawOpacity.Current)); order.Add(bounds.renderX == source.Single().Bounds.renderX ? "source backdrop" : "moving backdrop"); });
            var batch = new Mock<IHudOverlayBackdropBatch>();
            batch.Setup(value => value.Collect(It.IsAny<RectangleF>(), It.IsAny<Action>())).Callback<RectangleF, Action>((_, collect) => collect());
            using var host = new HudOverlayHost(storage.Api.Object, storage.Registry, _ => !drawable, (_, _) => failures++, backdropBatch: batch.Object);
            host.Synchronize(new Dictionary<string, HudOverlayGroupLayout> { ["test"] = layout });
            var preview = (HudOverlayDragPreview)typeof(HudOverlayHost).GetField("dragPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            movement.Begin(PointF.Empty, PointF.Empty, 1); preview.Begin(movement, source);
            layout.Root.renderOffsetX += x; layout.Root.renderOffsetY += y;
            overlay.Setup(value => value.Draw(renderer.Object, It.IsAny<ElementBounds>(), It.IsAny<RectangleF>(), It.IsAny<float>()))
                .Callback<IRenderAPI, ElementBounds, RectangleF, float>((_, bounds, _, dt) => {
                    draws.Add((bounds.renderX, bounds.renderY, dt, HudOverlayDrawOpacity.Current));
                    order.Add(dt == 0 ? "source foreground" : "moving foreground");
                    if (failSource && dt == 0) throw new InvalidOperationException();
                });
            storage.Registry.BeginSession(storage.Api.Object);
            if (remove) registrationHandle.Dispose();
            host.TryOpen(false); HudOverlayNativeRenderPass.Render(host, .1f);
            if (remove || drawable) { Assert.Empty(draws); Assert.Empty(backdrops); }
            else
            {
                Assert.Equal(new[] { "moving backdrop", "moving foreground", "source backdrop", "source foreground" }, order);
                Assert.Equal(2, backdrops.Count);
                Assert.Contains(backdrops, entry => entry.X == source.Single().Bounds.renderX);
                Assert.Contains(backdrops, entry => entry.X == layout.Members.Single().Bounds.renderX);
                Assert.All(backdrops, entry => Assert.InRange(entry.Opacity, float.Epsilon, MathF.BitDecrement(1)));
                Assert.Equal(2, draws.Count); Assert.Equal(source.Single().Bounds.renderX, draws[1].X);
                Assert.Equal(source.Single().Bounds.renderY, draws[1].Y); Assert.Equal(0, draws[1].Dt);
                Assert.Equal(layout.Members.Single().Bounds.renderX, draws[0].X); Assert.Equal(.1f, draws[0].Dt);
                Assert.All(draws, draw => Assert.InRange(draw.Opacity, float.Epsilon, MathF.BitDecrement(1)));
                Assert.Equal(draws[0].Opacity, draws[1].Opacity); Assert.Equal(failSource ? 1 : 0, failures);
                renderer.Verify(value => value.PopScissor(), Times.Exactly(draws.Count));
                movement.Cancel(); draws.Clear(); HudOverlayNativeRenderPass.Render(host, .1f);
                Assert.Single(draws); Assert.Equal(1, draws[0].Opacity);
            }
            Assert.Equal(1, HudOverlayDrawOpacity.Current);
            overlay.Verify(value => value.Refresh(), Times.Never); overlay.Verify(value => value.Prepare(It.IsAny<HudOverlayPreparationContext>()), Times.Never);
            movement.Cancel();
        }
        finally { RuntimeEnv.GUIScale = oldScale; }
    }
    #endregion
}
