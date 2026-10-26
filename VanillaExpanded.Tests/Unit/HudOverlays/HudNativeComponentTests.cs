using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Rendering.Native;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Verifies real native component registration, prepared geometry and resource ownership.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudNativeComponentTests
{
    #region Public API
    #region Composition and content
    /// <summary>Native bounds center independent rows and scale caller-supplied artwork dimensions.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ComposerOwnsRealTextAndArtworkWithNativeParentage(float scale)
    {
        using var fixture = new HudNativeComponentFixture(scale);
        using var view = fixture.CreateView();
        using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        var reading = Assert.IsType<TextElement>(composer.GetElement("test:reading"));
        var icon = Assert.IsType<TextureElement>(composer.GetElement("test:icon"));
        Assert.Same(reading.Bounds.ParentBounds, icon.Bounds.ParentBounds);
        Assert.NotSame(reading.Bounds.ParentBounds, reading.Bounds.ParentBounds.ParentBounds);
        Assert.Equal(fixture.Content.IconSize.Width * scale, icon.Bounds.OuterWidth, 4);
        Assert.Equal(fixture.Content.IconSize.Height * scale, icon.Bounds.OuterHeight, 4);
        Assert.Equal(reading.Bounds.renderX + reading.Bounds.OuterWidth / 2,
            icon.Bounds.renderX + icon.Bounds.OuterWidth / 2, 4);
        Assert.Equal(reading.Bounds.renderY + reading.Bounds.OuterHeight + fixture.Content.Gap * scale, icon.Bounds.renderY, 4);
        var parent = icon.Bounds.ParentBounds;
        Assert.True(icon.Bounds.renderY + icon.Bounds.OuterHeight <= parent.renderY + parent.OuterHeight + .001);
        Assert.Empty(fixture.Failures);
    }
    /// <summary>Rendering and same-size updates retain native structure and avoid resource creation for stable data.</summary>
    [Fact]
    public void ContentUpdatesReuseElementsAndRenderingConsumesCachedGeometry()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView();
        using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        var reading = Assert.IsType<TextElement>(composer.GetElement("test:reading"));
        var icon = Assert.IsType<TextureElement>(composer.GetElement("test:icon"));
        var prepared = reading.State.Target;
        var originalSize = view.Size;
        // Mutating unresolved bounds proves foreground rendering consumes cached geometry, not current layout fields.
        reading.Bounds.fixedWidth += 100;
        reading.Bounds.MarkDirtyRecursive();
        for (int frame = 0; frame < 4; frame++) composer.Render(.1f);
        Assert.Equal(prepared, reading.State.Target);
        Assert.False(reading.Bounds.Initialized);
        Assert.Equal(1, fixture.TextCreates); Assert.Equal(1, fixture.IconLoads);
        fixture.Content = fixture.Content with { Reading = "changed" };
        view.Prepare(fixture.Context);
        Assert.Equal(originalSize, view.Size);
        Assert.Same(reading, composer.GetElement("test:reading")); Assert.Same(icon, composer.GetElement("test:icon"));
        Assert.Equal(2, fixture.TextCreates); Assert.Equal(1, fixture.IconLoads);
        view.Prepare(fixture.Context);
        Assert.Equal(2, fixture.TextCreates);
        Assert.True(fixture.Glyphs[0].Disposed);
    }
    /// <summary>Absent rows remove their spacing and measurement changes do not require a new element tree.</summary>
    [Fact]
    public void OptionalRowsAndChangedMeasurementsUsePreparedNativeBounds()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView();
        using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        var icon = composer.GetElement("test:icon");
        fixture.Content = fixture.Content with { Reading = null };
        view.Prepare(fixture.Context);
        composer.Bounds.MarkDirtyRecursive(); composer.Bounds.CalcWorldBounds();
        view.LayoutCompleted(new(0, 0, 500, 500));
        Assert.Equal(fixture.Content.IconSize, view.Size);
        Assert.Equal(icon.Bounds.ParentBounds.renderY, icon.Bounds.renderY, 4);
        fixture.Content = fixture.Content with { IconPath = null, Reading = "a longer reading" };
        view.Prepare(fixture.Context);
        Assert.Equal(new SizeF(fixture.Glyphs[^1].Width, fixture.Glyphs[^1].Height), view.Size);
        fixture.Content = fixture.Content with { Reading = null };
        view.Prepare(fixture.Context);
        Assert.Equal(SizeF.Empty, view.Size);
    }
    /// <summary>Moving the native root changes cached output coordinates without loading or rasterizing resources.</summary>
    [Fact]
    public void PositionOnlyChangesKeepPreparedTextures()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView();
        using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        var icon = Assert.IsType<TextureElement>(composer.GetElement("test:icon"));
        var previous = icon.State.Target;
        composer.Bounds.fixedX += 17; composer.Bounds.fixedY += 23;
        composer.Bounds.MarkDirtyRecursive(); composer.Bounds.CalcWorldBounds();
        view.LayoutCompleted(new(0, 0, 500, 500));
        Assert.Equal(previous.X + 17, icon.State.Target.X, 4);
        Assert.Equal(previous.Y + 23, icon.State.Target.Y, 4);
        Assert.Equal(1, fixture.TextCreates); Assert.Equal(1, fixture.IconLoads);
    }
    /// <summary>Context changes replace only affected glyphs and constrained readings preserve their aspect ratio.</summary>
    [Fact]
    public void ContextInvalidationAndReadingFitArePreparedBeforeDrawing()
    {
        using var fixture = new HudNativeComponentFixture();
        fixture.Content = fixture.Content with { IconPath = null, MaximumReadingHeight = 6 };
        using var view = fixture.CreateView(); using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        var glyph = fixture.Glyphs[0];
        float fit = fixture.Content.MaximumReadingHeight / glyph.Height;
        Assert.Equal(new SizeF(glyph.Width * fit, glyph.Height * fit), view.Size);
        var reading = Assert.IsType<TextElement>(composer.GetElement("test:reading"));
        var font = reading.Font;
        view.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", 1));
        Assert.Equal(2, fixture.TextCreates); Assert.Equal(0, fixture.IconLoads);
        Assert.True(glyph.Disposed); Assert.NotSame(font, reading.Font);
        view.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 1, "fr", 1));
        Assert.Equal(3, fixture.TextCreates);
        Vintagestory.API.Config.RuntimeEnv.GUIScale = 2;
        view.Prepare(new HudOverlayPreparationContext(fixture.Api.Object, 2, "fr", 1));
        Assert.Equal(4, fixture.TextCreates);
        Assert.Equal(fixture.Content.MaximumReadingHeight, view.Size.Height);
    }
    #endregion
    #region Input and drawing boundaries
    /// <summary>Both dynamic component types leave presses, keys, wheel events and native focus untouched.</summary>
    [Fact]
    public void DynamicRegistrationRemainsPassive()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView();
        using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        foreach (var key in new[] { "test:reading", "test:icon" })
        {
            var element = composer.GetElement(key);
            var press = new MouseEvent((int)element.Bounds.renderX + 1, (int)element.Bounds.renderY + 1, EnumMouseButton.Left, 0);
            element.OnMouseDown(fixture.Api.Object, press);
            element.OnMouseUp(fixture.Api.Object, press);
            var wheel = new MouseWheelEventArgs(); element.OnMouseWheel(fixture.Api.Object, wheel);
            var keyEvent = new KeyEvent(); element.OnKeyDown(fixture.Api.Object, keyEvent);
            Assert.False(press.Handled); Assert.False(wheel.IsHandled); Assert.False(keyEvent.Handled);
            Assert.False(element.Focusable); Assert.False(element.HasFocus);
        }
        Assert.False(composer.FocusElement(0));
    }
    /// <summary>Opacity changes tint cached pixels and intersect clips even when an artwork draw fails.</summary>
    [Fact]
    public void FadesDoNotRasterizeAndDrawingFailureRestoresScissor()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView();
        using var composer = fixture.CreateComposer();
        fixture.Mount(view, composer);
        view.SetOpacity(.7f);
        var icon = Assert.IsType<TextureElement>(composer.GetElement("test:icon"));
        fixture.Render.Setup(render => render.RenderTexture(It.IsAny<int>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<float>(), It.IsAny<Vec4f>())).Throws(new InvalidOperationException());
        icon.RenderInteractiveElements(.1f);
        Assert.Single(fixture.Failures);
        fixture.Render.Verify(render => render.PushScissor(It.IsAny<ElementBounds>(), true), Times.Once);
        fixture.Render.Verify(render => render.PopScissor(), Times.Once);
        Assert.Equal(.7f, icon.State.Tint(false)!.W);
        view.SetOpacity(.3f);
        var text = Assert.IsType<TextElement>(composer.GetElement("test:reading"));
        Assert.Equal(text.State.Tint(true)!.W, text.State.Tint(true)!.X);
        Assert.Equal(1, fixture.TextCreates); Assert.Equal(1, fixture.IconLoads);
    }
    #endregion
    #region Ownership and failure cleanup
    /// <summary>Distinct source and live views own distinct glyphs and release resources once despite native duplicate callbacks.</summary>
    [Fact]
    public void IndependentComposersRetainSurvivingResources()
    {
        using var fixture = new HudNativeComponentFixture { OwnArtwork = true };
        using var live = fixture.CreateView(); using var source = fixture.CreateView();
        using var liveComposer = fixture.CreateComposer(); using var sourceComposer = fixture.CreateComposer();
        fixture.Mount(live, liveComposer); fixture.Mount(source, sourceComposer);
        Assert.NotSame(liveComposer.GetElement("test:reading"), sourceComposer.GetElement("test:reading"));
        live.Dispose();
        Assert.False(fixture.Glyphs[0].Disposed);
        liveComposer.Dispose(); liveComposer.Dispose();
        Assert.True(fixture.Glyphs[0].Disposed); Assert.True(fixture.Artwork[0].Disposed);
        Assert.False(fixture.Glyphs[1].Disposed); Assert.False(fixture.Artwork[1].Disposed);
        sourceComposer.Render(.1f);
        fixture.Gui.Verify(gui => gui.DeleteTexture(fixture.Glyphs[1].TextureId), Times.Never);
        sourceComposer.Dispose();
        fixture.Gui.Verify(gui => gui.DeleteTexture(100), Times.Once);
        fixture.Gui.Verify(gui => gui.DeleteTexture(101), Times.Once);
    }
    /// <summary>Unattached resources are released locally while borrowed artwork is never disposed.</summary>
    [Fact]
    public void UnmountedViewsReleaseOwnedGlyphsButBorrowArtwork()
    {
        using var fixture = new HudNativeComponentFixture();
        var view = fixture.CreateView(); view.Prepare(fixture.Context);
        view.Dispose(); view.Dispose();
        Assert.True(fixture.Glyphs.Single().Disposed);
        Assert.False(fixture.Artwork.Single().Disposed);
        fixture.Gui.Verify(gui => gui.DeleteTexture(100), Times.Once);
        fixture.Gui.Verify(gui => gui.DeleteTexture(101), Times.Never);
        Assert.Throws<ObjectDisposedException>(() => view.Prepare(fixture.Context));
    }
    /// <summary>Mounting a view twice is rejected before another composer can acquire its elements.</summary>
    [Fact]
    public void ViewCannotShareElementsWithAnotherComposer()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView(); using var first = fixture.CreateComposer(); using var second = fixture.CreateComposer();
        fixture.Mount(view, first);
        Assert.Throws<InvalidOperationException>(() => view.AddTo(second, ElementBounds.Fixed(0, 0, 30, 30), "other"));
        Assert.Null(second.GetElement("other:reading"));
    }
    /// <summary>Invalid API identity or self-parenting is rejected before native tree mutation.</summary>
    [Fact]
    public void MountRejectsForeignApiAndSelfParenting()
    {
        using var fixture = new HudNativeComponentFixture(); using var other = new HudNativeComponentFixture();
        using var view = fixture.CreateView(); using var foreign = other.CreateComposer(); using var composer = fixture.CreateComposer();
        Assert.Throws<ArgumentException>(() => view.AddTo(foreign, ElementBounds.Fixed(0, 0, 10, 10), "wrong"));
        Assert.Throws<ArgumentException>(() => view.AddTo(composer, composer.Bounds, "self"));
        fixture.Mount(view, composer);
        Assert.IsType<TextElement>(composer.GetElement("test:reading"));
    }
    /// <summary>Duplicate-key initialization failure releases partial unique components without a second owner.</summary>
    [Fact]
    public void FailedMountCleansUpPartiallyRegisteredElements()
    {
        using var fixture = new HudNativeComponentFixture { OwnArtwork = true };
        using var view = fixture.CreateView(); using var composer = fixture.CreateComposer();
        composer.AddInteractiveElement(new TextureElement(fixture.Api.Object, ElementBounds.Fixed(0, 0, 2, 2), fixture.Failures.Add), "test:icon");
        view.Prepare(fixture.Context);
        Assert.Throws<AggregateException>(() => view.AddTo(composer, ElementBounds.Fixed(0, 0, 80, 80), "test"));
        Assert.All(fixture.Glyphs.Concat(fixture.Artwork), texture => Assert.True(texture.Disposed));
        fixture.Gui.Verify(gui => gui.DeleteTexture(100), Times.Once);
        fixture.Gui.Verify(gui => gui.DeleteTexture(101), Times.Once);
    }
    /// <summary>Failed glyph replacement keeps the old resource owned until normal cleanup.</summary>
    [Fact]
    public void FailedContentPreparationRetainsCleanupOwnership()
    {
        using var fixture = new HudNativeComponentFixture();
        using var view = fixture.CreateView();
        view.Prepare(fixture.Context);
        fixture.ThrowText = true; fixture.Content = fixture.Content with { Reading = "broken" };
        Assert.Throws<InvalidOperationException>(() => view.Prepare(fixture.Context));
        Assert.False(fixture.Glyphs[0].Disposed);
        view.Dispose();
        Assert.True(fixture.Glyphs[0].Disposed);
    }
    #endregion
    #endregion
}
