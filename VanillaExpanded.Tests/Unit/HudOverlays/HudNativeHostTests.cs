using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Qualifies host-driven native invalidation, ordering, independent previews and teardown.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudNativeHostTests
{
    #region Public API
    #region Invalidation and prepared rendering
    /// <summary>Repeated rendering consumes the same glyphs, components, geometry and prepared content.</summary>
    [Fact]
    public void UnchangedFramesUsePreparedGeometryBeforeBothPasses()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var view = Assert.Single(fixture.Views);
        var composer = fixture.Host.Composers[fixture.Group.Id];
        Assert.IsType<TextElement>(composer.GetElement(fixture.Registration.Id + ":reading"));
        int layouts = view.Layouts, prepares = view.Prepares, glyphs = fixture.Components.TextCreates;
        int calculations = view.Probe.Calculations, composes = fixture.Composes;
        for (int frame = 0; frame < 3; frame++) fixture.Render();
        Assert.Equal(layouts, view.Layouts); Assert.Equal(prepares, view.Prepares);
        Assert.Equal(glyphs, fixture.Components.TextCreates);
        Assert.Equal(calculations, view.Probe.Calculations); Assert.Equal(composes, fixture.Composes);
        fixture.Native.Verify(native => native.BeginFrame(.1f), Times.Exactly(3));
        Assert.Equal(fixture.Layout.Members.Single().Clip, view.Clip);
        Assert.True(fixture.Calls.IndexOf("frame") < fixture.Calls.IndexOf("live backdrop"));
        Assert.True(fixture.Calls.IndexOf("live backdrop") < fixture.Calls.IndexOf("flush"));
        Assert.True(fixture.Calls.IndexOf("flush") < fixture.Calls.IndexOf("foreground"));
        fixture.Overlay.Verify(overlay => overlay.Draw(It.IsAny<IRenderAPI>(), It.IsAny<ElementBounds>(),
            It.IsAny<RectangleF>(), It.IsAny<float>()), Times.Never);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>Content-only updates retain packing; movement and changed measurement retain native resources.</summary>
    [Fact]
    public void ContentMovementAndMeasurementHaveSeparateInvalidation()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var view = Assert.Single(fixture.Views); var composer = fixture.Host.Composers[fixture.Group.Id];
        long packed = fixture.Layout.Revision;
        fixture.Components.Content = fixture.Components.Content with { Reading = "changed" };
        fixture.Revision++; fixture.Synchronize();
        Assert.Equal(packed, fixture.Layout.Revision); Assert.Equal(2, fixture.Components.TextCreates);
        Assert.Equal(1, fixture.Components.IconLoads);
        int glyphs = fixture.Components.TextCreates, prepares = view.Prepares;
        var old = composer.GetElement(fixture.Registration.Id + ":reading").Bounds.renderX;
        // A container can change origin while preserving a member's screen position. Its owned
        // mounting bounds must still rebase into the changed parent coordinate system.
        fixture.Layout.Root.renderOffsetX += 17;
        fixture.Layout.Members.Single().Bounds.renderOffsetX -= 17;
        fixture.Synchronize();
        Assert.Equal(old, composer.GetElement(fixture.Registration.Id + ":reading").Bounds.renderX);
        fixture.Synchronize(new(160, 130));
        Assert.NotEqual(old, composer.GetElement(fixture.Registration.Id + ":reading").Bounds.renderX);
        fixture.Size = new(fixture.Size.Width + 40, fixture.Size.Height + 20); fixture.Synchronize(new(160, 130));
        Assert.True(fixture.Layout.Revision > packed);
        Assert.Same(composer, fixture.Host.Composers[fixture.Group.Id]);
        Assert.Equal(glyphs, fixture.Components.TextCreates); Assert.Equal(prepares, view.Prepares);
        Assert.Single(fixture.Views); Assert.Empty(fixture.Failures);
    }

    /// <summary>Prepared style changes replace visual ownership while preserving the controller and session.</summary>
    [Fact]
    public void StructureChangesReplaceDistinctElementsWithoutRebindingController()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var first = fixture.Views.Single(); var root = fixture.Layout.Root;
        int children = root.ChildBounds.Count;
        fixture.Structure++; fixture.Revision++; fixture.Synchronize();
        Assert.Equal(1, first.Disposals); Assert.Equal(2, fixture.Views.Count);
        Assert.Equal(children, root.ChildBounds.Count);
        Assert.Equal(0, fixture.Views.Last().Disposals);
        fixture.Overlay.Verify(overlay => overlay.BeginSession(fixture.Components.Api.Object), Times.Once);
        fixture.Overlay.Verify(overlay => overlay.EndSession(), Times.Never);
        fixture.Overlay.Verify(overlay => overlay.Refresh(), Times.Never);
        Assert.True(fixture.Components.Glyphs.First().Disposed);
        Assert.False(fixture.Components.Glyphs.Last().Disposed);
    }

    /// <summary>GUI scale and empty optional rows invalidate component geometry without replacing the composer.</summary>
    [Fact]
    public void ScaleAndOptionalRowsResolveBeforeRendering()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var view = fixture.Views.Single(); var composer = fixture.Host.Composers[fixture.Group.Id];
        int glyphs = fixture.Components.TextCreates;
        Vintagestory.API.Config.RuntimeEnv.GUIScale *= 1.5f;
        fixture.Synchronize(); fixture.Render();
        Assert.Equal(glyphs + 1, fixture.Components.TextCreates);
        Assert.Same(composer, fixture.Host.Composers[fixture.Group.Id]);
        Assert.Equal(fixture.Layout.Members.Single().Clip, view.Clip);
        fixture.Components.Content = fixture.Components.Content with { Reading = null };
        fixture.Revision++; fixture.Synchronize(); fixture.Render();
        Assert.True(fixture.Components.Glyphs.Last().Disposed);
        Assert.Single(fixture.Views); Assert.Empty(fixture.Failures);
    }
    #endregion

    #region Availability and failure isolation
    /// <summary>A disappearing native member retires its view and does not call a hidden factory.</summary>
    [Fact]
    public void MissingContentRetiresNativeViewWithoutPreparingHiddenFeature()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var view = fixture.Views.Single(); fixture.Drawable = false;
        fixture.Synchronize(); fixture.Render();
        Assert.Equal(1, view.Disposals); Assert.Single(fixture.Views);
        Assert.DoesNotContain("foreground", fixture.Calls);
        fixture.Drawable = true; fixture.Synchronize(); fixture.Render();
        Assert.Equal(2, fixture.Views.Count); Assert.Contains("foreground", fixture.Calls);
    }

    /// <summary>Unavailable native anchors release visual resources while retaining the registration for restoration.</summary>
    [Fact]
    public void UnavailableContainerRetiresNativeResourcesAndCanRestore()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var view = fixture.Views.Single();
        typeof(VanillaExpanded.HudOverlays.Layout.HudOverlayGroupLayout)
            .GetProperty(nameof(fixture.Layout.Available))!.SetValue(fixture.Layout, false);
        fixture.Render();
        Assert.Equal(1, view.Disposals); Assert.Empty(fixture.Host.Composers);
        fixture.Components.Content = fixture.Components.Content with { Reading = "restored" };
        fixture.Size = new(fixture.Size.Width + 1, fixture.Size.Height); fixture.Synchronize(); fixture.Render();
        Assert.Equal(2, fixture.Views.Count); Assert.Contains("foreground", fixture.Calls);
        fixture.Overlay.Verify(overlay => overlay.EndSession(), Times.Never);
    }

    /// <summary>A failed native factory leaves a manual-drawing peer drawable in the same container.</summary>
    [Fact]
    public void NativeFactoryFailureDoesNotSuppressManualPeer()
    {
        using var fixture = new HudNativeHostFixture();
        fixture.Native.Setup(native => native.CreateView(It.IsAny<ICoreClientAPI>(), It.IsAny<NativeViewRole>()))
            .Throws(new InvalidOperationException("factory"));
        var manual = new Mock<IHudOverlay>();
        var registration = new HudOverlayRegistration("example:manual", manual.Object, () => true, fixture.Group.Id);
        fixture.Registry.Register(registration);
        fixture.Anchors.BeginFrame();
        fixture.Layout.Apply(fixture.Anchors, fixture.Group, new[]
        {
            new KeyValuePair<HudOverlayRegistration, SizeF>(fixture.Registration, fixture.Size),
            new KeyValuePair<HudOverlayRegistration, SizeF>(registration, new(53, 64))
        });
        fixture.Host.Synchronize(new Dictionary<string, VanillaExpanded.HudOverlays.Layout.HudOverlayGroupLayout>
            { [fixture.Group.Id] = fixture.Layout });
        fixture.Render();
        Assert.NotEmpty(fixture.Failures);
        manual.Verify(overlay => overlay.Draw(fixture.Components.Render.Object, It.IsAny<ElementBounds>(),
            It.IsAny<RectangleF>(), .1f), Times.Once);
        Assert.Empty(fixture.Views);
    }

    /// <summary>Failed layout finalization remains pending when content, geometry and host-owned eligibility stay unchanged.</summary>
    [Fact]
    public void LayoutFinalizationFailureRetriesBeforeBecomingDrawable()
    {
        using var fixture = new HudNativeHostFixture { LayoutFailuresRemaining = 1 };
        fixture.Synchronize(); var view = Assert.Single(fixture.Views);
        Assert.Single(fixture.Failures); Assert.Equal(1, view.Layouts);
        fixture.Render();
        Assert.Equal(2, view.Layouts); Assert.Equal(1, view.Prepares);
        Assert.Equal(fixture.Layout.Members.Single().Clip, view.Clip);
        Assert.Equal(1, fixture.Components.TextCreates); Assert.Contains("foreground", fixture.Calls);
        fixture.Render(); Assert.Equal(2, view.Layouts);
    }

    /// <summary>Failed generations release partial resources and recover without duplicating controller lifetime.</summary>
    [Theory]
    [InlineData("mount")]
    [InlineData("prepare")]
    [InlineData("compose")]
    public void FailedInitializationRetainsCleanupAndCanRecover(string failure)
    {
        using var fixture = new HudNativeHostFixture();
        fixture.FailMount = failure == "mount"; fixture.FailPrepare = failure == "prepare"; fixture.FailCompose = failure == "compose";
        fixture.Synchronize(); Assert.NotEmpty(fixture.Failures);
        fixture.FailMount = fixture.FailPrepare = fixture.FailCompose = false;
        fixture.Revision++; fixture.Synchronize(); fixture.Render();
        Assert.Contains("foreground", fixture.Calls);
        fixture.Host.Dispose(); fixture.Host.Dispose();
        Assert.All(fixture.Views, view => Assert.Equal(1, view.Disposals));
        fixture.Overlay.Verify(overlay => overlay.EndSession(), Times.Never);
    }
    #endregion

    #region Removal and preview lifetime
    /// <summary>Removal retires composer resources before controller cleanup even when requested during drawing.</summary>
    [Fact]
    public void RemovalAndSessionTeardownRespectOwnershipOrder()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        var view = fixture.Views.Single();
        fixture.Native.Setup(native => native.BeginFrame(It.IsAny<float>())).Callback(() => fixture.Handle.Dispose());
        fixture.Overlay.Setup(overlay => overlay.EndSession()).Callback(() => Assert.Equal(1, view.Disposals));
        fixture.Render();
        Assert.Equal(0, view.Disposals);
        fixture.Registry.RunPass(_ => { });
        Assert.Equal(1, view.Disposals); Assert.Empty(fixture.Host.Composers);
        fixture.Overlay.Verify(overlay => overlay.EndSession(), Times.Once);
        fixture.Overlay.Verify(overlay => overlay.Dispose(), Times.Once);
    }

    /// <summary>Source traces have distinct resources, share one effect advance, and retire safely on cancellation.</summary>
    [Fact]
    public void SourceTraceOwnsIndependentElementsAndCancellationRetainsLiveResources()
    {
        using var fixture = new HudNativeHostFixture(); fixture.Synchronize();
        using var settings = new HudLayoutFixture();
        HudLayoutFixture.BindSettings(fixture.Components.Api, settings.Strings, settings.Values);
        var containers = new HudOverlayContainers(fixture.Components.Api.Object, fixture.Registry);
        containers.SetMovable(fixture.Group.Id, true, PointF.Empty);
        var movement = containers.Get(fixture.Group.Id);
        var preview = (HudOverlayDragPreview)typeof(HudOverlayHost).GetField("dragPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Host)!;
        var captured = HudOverlayDragPreview.Capture(new[] { fixture.Layout });
        movement.Begin(PointF.Empty, PointF.Empty, 1); preview.Begin(movement, captured);
        fixture.Synchronize(new(170, 140)); fixture.Render();
        var live = fixture.Views.Single(view => view.Role == NativeViewRole.Live);
        var source = fixture.Views.Single(view => view.Role == NativeViewRole.SourceTrace);
        Assert.Equal(live.Opacity, source.Opacity); Assert.InRange(live.Opacity, float.Epsilon, MathF.BitDecrement(1));
        Assert.Equal(captured.Single().Clip, source.Clip);
        fixture.Native.Verify(native => native.BeginFrame(.1f), Times.Once);
        Assert.Equal(2, fixture.Components.TextCreates);
        Assert.True(fixture.Calls.IndexOf("foreground") < fixture.Calls.IndexOf("source backdrop"));
        movement.Cancel(); fixture.Render();
        Assert.Equal(1, source.Disposals); Assert.Equal(0, live.Disposals); Assert.Equal(1, live.Opacity);
        Assert.Equal(2, fixture.Components.TextCreates);
    }
    #endregion
    #endregion
}
