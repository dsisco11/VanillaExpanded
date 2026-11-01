using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.Unit.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.Gui;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class GuiElementIconTabsTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void FolderTabRendersDistinctBackgroundsAndFixedPixelAlignedNearestFilteredIcons(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        var draws = new List<(int Width, int Height, double WidthRequested, double[] Color)>();
        graphics.Gui.Object.Icons.CustomIcons["test-icon"] = (ctx, _, _, width, _, color) =>
        {
            using var target = ctx.GetTarget();
            var surface = Assert.IsType<ImageSurface>(target);
            draws.Add((surface.Width, surface.Height, width, color.ToArray()));
        };
        using var button = new GuiElementIconTab(graphics.Api.Object, "test-icon",
            CairoFont.WhiteSmallText(), _ => { }, ElementBounds.Fixed(120.3, 65.3, 30, 36).WithEmptyParent());
        using var surface = new ImageSurface(Format.Argb32, 64, 64);
        using var ctx = new Context(surface);
        button.ComposeElements(ctx, surface);
        Assert.Equal(2, draws.Count);
        Assert.All(draws, draw =>
        {
            Assert.Equal(draw.Width, draw.Height);
            Assert.Equal((double)draw.Width, draw.WidthRequested);
        });
        Assert.Equal(draws[0].Width, draws[1].Width);
        Assert.Equal(button.Font.Color, draws[0].Color);
        Assert.Equal(GuiStyle.ActiveButtonTextColor, draws[1].Color);
        Assert.False(draws[0].Color.SequenceEqual(draws[1].Color));
        Assert.Equal(4, graphics.Gui.Invocations.Count(call =>
            call.Method.Name == nameof(IGuiAPI.LoadOrUpdateCairoTexture) && !(bool)call.Arguments[1]));
        var textures = new Dictionary<bool, int>();
        var backgrounds = new Dictionary<bool, int>();
        (double X, double Y)? iconPosition = null;
        foreach (var state in new[] { (On: false, Hover: false), (On: false, Hover: true),
            (On: true, Hover: false), (On: true, Hover: true) })
        {
            button.On = state.On;
            SetMouse(graphics, state.Hover ? button.Bounds.absX + 10 : -100,
                state.Hover ? button.Bounds.absY + 10 : -100);
            graphics.Render.Invocations.Clear();
            button.RenderInteractiveElements(0);
            var calls = graphics.Render.Invocations.Where(call =>
                call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)).ToArray();
            Assert.Equal(2, calls.Length);
            var background = calls[0].Arguments;
            Assert.Equal(Math.Round(button.Bounds.renderX, MidpointRounding.AwayFromZero), (double)background[1]);
            Assert.Equal(Math.Round(button.Bounds.renderY, MidpointRounding.AwayFromZero), (double)background[2]);
            Assert.Equal(Math.Round(30 * scale, MidpointRounding.AwayFromZero), (double)background[3]);
            Assert.Equal(Math.Round(36 * scale, MidpointRounding.AwayFromZero), (double)background[4]);
            if (backgrounds.TryGetValue(state.On, out int backgroundTexture))
                Assert.Equal(backgroundTexture, (int)background[0]);
            else backgrounds.Add(state.On, (int)background[0]);
            var overlay = calls[1].Arguments;
            int size = (int)Math.Round(20 * scale, MidpointRounding.AwayFromZero);
            Assert.Equal((double)size, (double)overlay[3]);
            Assert.Equal((double)size, (double)overlay[4]);
            Assert.Equal(Math.Round((double)overlay[1]), (double)overlay[1]);
            Assert.Equal(Math.Round((double)overlay[2]), (double)overlay[2]);
            var position = ((double)overlay[1], (double)overlay[2]);
            if (iconPosition is { } previous) Assert.Equal(previous, position);
            else iconPosition = position;
            Assert.Equal(51f, (float)overlay[5]);
            bool active = state.On || state.Hover;
            if (textures.TryGetValue(active, out int texture)) Assert.Equal(texture, (int)overlay[0]);
            else textures.Add(active, (int)overlay[0]);
        }
        Assert.NotEqual(textures[false], textures[true]);
        Assert.NotEqual(backgrounds[false], backgrounds[true]);
        button.Dispose();
        Assert.Empty(graphics.Textures);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void FolderTabRasterHasRoundedShouldersAndOpensIntoTheMatchingPanel(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        var uploads = new List<(int Width, int Height, int Stride, byte[] Pixels)>();
        graphics.Gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(),
            ref It.Ref<LoadedTexture>.IsAny)).Callback(new Upload((ImageSurface image, bool linear, ref LoadedTexture texture) =>
        {
            Assert.False(linear);
            image.Flush();
            uploads.Add((image.Width, image.Height, image.Stride, image.Data.ToArray()));
            texture.TextureId = uploads.Count;
            graphics.Textures.Add(texture.TextureId);
            texture.Width = image.Width;
            texture.Height = image.Height;
        }));
        using var button = new GuiElementIconTab(graphics.Api.Object, "test-icon", CairoFont.WhiteSmallText(),
            _ => { }, ElementBounds.Fixed(0, 0, 30, 36).WithEmptyParent());
        using var surface = new ImageSurface(Format.Argb32, 100, 100);
        using var ctx = new Context(surface);
        button.ComposeElements(ctx, surface);
        var released = uploads[0];
        var selected = uploads[1];
        Assert.Equal(4, uploads.Count);
        Assert.Equal((released.Width, released.Height), (selected.Width, selected.Height));
        int middle = selected.Width / 2;
        Assert.Equal(0, Pixel(released, middle, 0)[3]);
        Assert.Equal(255, Pixel(selected, middle, 0)[3]);
        Assert.Equal(0, Pixel(selected, 0, 0)[3]);
        Assert.Equal(255, Pixel(released, middle, (int)Math.Round(4 * scale))[3]);

        var panelBounds = ElementBounds.Fixed(0, 0, 80, 80).WithEmptyParent();
        panelBounds.CalcWorldBounds();
        GuiElementIconTab.DrawPanel(ctx, surface, panelBounds);
        surface.Flush();
        var panel = (surface.Width, surface.Height, surface.Stride, surface.Data.ToArray());
        Assert.Equal(Pixel(panel, 10, 10), Pixel(selected, middle, selected.Height - 1));
        Assert.Equal(Pixel(panel, 10, 0), Pixel(released, middle, released.Height - 1));
        Assert.NotEqual(Pixel(released, middle, released.Height - 1), Pixel(selected, middle, selected.Height - 1));

        static byte[] Pixel((int Width, int Height, int Stride, byte[] Pixels) image, int x, int y) =>
            image.Pixels.AsSpan(y * image.Stride + x * 4, 4).ToArray();
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    [InlineData(1.1f)]
    public void SelectedTabStaysJoinedToHostPanelAcrossFractionalMovementAndScrolling(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        using var tabs = Create(graphics, Enumerable.Range(0, 9).Select(i => "Entry " + i).ToArray(), 180, _ => { });
        var host = ElementBounds.Fixed(100.3, 80.3, 1000, 1000).WithEmptyParent();
        host.IsDrawingSurface = true;
        tabs.Bounds.WithParent(host);
        host.WithChild(tabs.Bounds);
        var panel = ElementBounds.Fixed(120, 65 + GuiElementIconTabs.TabHeight - 1 / GuiElement.scaled(1), 180, 50).WithParent(host);
        host.WithChild(panel);
        foreach (double position in new[] { .1, .3, .7 })
        {
            host.fixedX = 100 + position;
            host.fixedY = 80 + position;
            host.MarkDirtyRecursive();
            host.CalcWorldBounds();
            tabs.SetValue(8, false);
            graphics.Render.Invocations.Clear();
            tabs.RenderInteractiveElements(0);
            var backgrounds = graphics.Render.Invocations.Where(call =>
                call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
                && call.Arguments.Count == 7 && (float)call.Arguments[5] == 50).ToArray();
            Assert.Equal(9, backgrounds.Length);
            var selected = backgrounds[8].Arguments;
            Assert.Equal((int)host.renderY + Math.Round(panel.drawY, MidpointRounding.AwayFromZero),
                (double)selected[2] + (double)selected[4] - 1);
            Assert.True((double)selected[2] + (double)selected[4] <=
                (int)tabs.Viewport!.renderY + tabs.Viewport.OuterHeightInt);
            var selectedBounds = tabs.Composer!.GetToggleButton("tab-8").Bounds;
            Assert.Equal((int)host.renderX + Math.Round(selectedBounds.renderX - host.renderX, MidpointRounding.AwayFromZero),
                (double)selected[1]);
        }
    }

    [Theory]
    [InlineData(.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void NativeComposerOwnsIconOnlyButtonsAndBoundsAndRecompositionDisposesTextures(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        using var tabs = Create(graphics, ["First", "A much longer localized name", "Third"], 500, _ => { });
        var composer = Assert.IsType<GuiComposer>(tabs.Composer);
        Assert.False(composer.Tabbable);
        Assert.Same(tabs.Bounds, composer.Bounds.ParentBounds);
        Assert.Same(composer.Bounds, tabs.Viewport!.ParentBounds);
        var first = composer.GetToggleButton("tab-0");
        var second = composer.GetToggleButton("tab-1");
        Assert.Equal("", first.GetText());
        Assert.Equal("", second.GetText());
        Assert.Equal(first.Bounds.OuterWidth, second.Bounds.OuterWidth);
        Assert.Equal(GuiElementIconTabs.TabSize * scale, first.Bounds.OuterWidth, 4);
        Assert.Equal(tabs.Bounds.absX + 4 * scale, first.Bounds.absX, 4);
        Assert.Equal(first.Bounds.absX + first.Bounds.OuterWidth + 4 * scale, second.Bounds.absX, 4);
        Assert.Equal(tabs.Bounds.absY + tabs.Bounds.InnerHeight,
            first.Bounds.absY + first.Bounds.OuterHeight, 4);
        Assert.Equal(GuiElementIconTabs.TabHeight * scale, first.Bounds.OuterHeight, 4);
        Assert.Equal("A much longer localized name", tabs.Hints[1].GetText());
        int textureCount = graphics.Textures.Count;
        Assert.True(textureCount > 0);
        tabs.SetValue(2, false);
        Compose(tabs);
        Assert.Equal(textureCount, graphics.Textures.Count);
        Assert.Equal(2, tabs.activeElement);
        Assert.True(tabs.Composer!.GetToggleButton("tab-2").On);
        Assert.Single(tabs.Bounds.ChildBounds);
        tabs.Dispose();
        tabs.Dispose();
        Assert.Empty(tabs.Bounds.ChildBounds);
        Assert.Empty(graphics.Textures);
        Assert.Empty(graphics.Bakes);
    }

    [Theory]
    [InlineData(1f, 9)]
    [InlineData(1.25f, 10)]
    [InlineData(1.5f, 18)]
    [InlineData(1.75f, 9)]
    [InlineData(2f, 10)]
    public void MeasuredWidthShowsEveryTabWithoutScrollingOrArrows(float scale, int count)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        double width = GuiElementIconTabs.MeasureWidth(count);
        Assert.Equal(count * 30 + (count + 1) * 4, width);
        using var tabs = Create(graphics, Enumerable.Range(0, count).Select(i => "Entry " + i).ToArray(), width, _ => { });
        Assert.False(tabs.Composer!.GetButton("left").Visible);
        Assert.False(tabs.Composer.GetButton("right").Visible);
        Assert.All(Enumerable.Range(0, count), index =>
        {
            var bounds = tabs.Composer.GetToggleButton("tab-" + index).Bounds;
            Assert.True(bounds.absX >= tabs.Viewport!.absX);
            Assert.True(bounds.absX + bounds.OuterWidth <= tabs.Viewport.absX + tabs.Viewport.InnerWidth + .001);
        });
        tabs.SetValue(count - 1, false);
        Assert.Equal(tabs.Bounds.absX + 4 * scale, tabs.Composer.GetToggleButton("tab-0").Bounds.absX, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => GuiElementIconTabs.MeasureWidth(0));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void SelectionKeyboardWrapAndWheelScrollingUseNativeBoundsAndClipHitTesting(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        var changes = new List<int>();
        using var tabs = Create(graphics, Enumerable.Range(0, 9).Select(i => "Entry " + i).ToArray(), 180, changes.Add);
        tabs.SetValue(8);
        Assert.Equal(8, Assert.Single(changes));
        AssertSelectedVisible();
        tabs.SetValue(0, false);
        Assert.Single(changes);
        AssertSelectedVisible();
        tabs.SetValue(3, false);
        Compose(tabs);
        AssertSelectedVisible();
        tabs.SetValue(0, false);
        tabs.OnFocusGained();
        var left = new KeyEvent { KeyCode = (int)GlKeys.Left };
        tabs.OnKeyDown(graphics.Api.Object, left);
        Assert.True(left.Handled);
        Assert.Equal(8, tabs.activeElement);
        AssertSelectedVisible();
        var right = new KeyEvent { KeyCode = (int)GlKeys.Right };
        tabs.OnKeyDown(graphics.Api.Object, right);
        Assert.True(right.Handled);
        Assert.Equal(0, tabs.activeElement);
        AssertSelectedVisible();
        SetMouse(graphics, tabs.Viewport!.absX + 1, tabs.Viewport.absY + 10 * scale);
        var wheel = new MouseWheelEventArgs { deltaPrecise = 1000 };
        tabs.OnMouseWheel(graphics.Api.Object, wheel);
        Assert.Equal(0, tabs.activeElement);
        Assert.True(tabs.Composer!.GetToggleButton("tab-0").Bounds.absX < tabs.Viewport.absX);
        int count = changes.Count;
        double beforeArrow = tabs.Composer.GetToggleButton("tab-0").Bounds.absX;
        tabs.OnMouseDownOnElement(graphics.Api.Object,
            new MouseEvent((int)tabs.Bounds.absX + 1, (int)tabs.Bounds.absY + 10, EnumMouseButton.Left, 0));
        Assert.Equal(count, changes.Count);
        tabs.OnMouseUp(graphics.Api.Object,
            new MouseEvent((int)tabs.Bounds.absX + 1, (int)tabs.Bounds.absY + 10, EnumMouseButton.Left, 0));
        Assert.True(tabs.Composer.GetToggleButton("tab-0").Bounds.absX > beforeArrow);
        tabs.SetValue(0, false);
        tabs.Enabled = false;
        graphics.Render.Invocations.Clear();
        tabs.RenderInteractiveElements(0);
        tabs.OnMouseDownOnElement(graphics.Api.Object, new MouseEvent(10, 10, EnumMouseButton.Left, 0));
        tabs.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Right });
        Assert.Empty(graphics.Render.Invocations);
        Assert.Equal(0, tabs.activeElement);
        Assert.Equal(count, changes.Count);

        void AssertSelectedVisible()
        {
            var bounds = tabs.Composer!.GetToggleButton("tab-" + tabs.activeElement).Bounds;
            Assert.True(bounds.absX >= tabs.Viewport!.absX - .001);
            Assert.True(bounds.absX + bounds.OuterWidth <= tabs.Viewport.absX + tabs.Viewport.InnerWidth + .001);
        }
    }

    [Fact]
    public void PointerAndKeyboardHintsUseTabNamesAndAreOutsideTheClipAndDismissOffThePanel()
    {
        string originalLocale = Lang.CurrentLocale;
        var originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        Lang.AvailableLanguages["en"] = Mock.Of<ITranslationService>();
        Lang.ChangeLanguage("en");
        try { AssertHints(); }
        finally
        {
            if (originalEnglish == null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = originalEnglish;
            Lang.ChangeLanguage(originalLocale);
        }
    }

    private static void AssertHints()
    {
        using var graphics = new RecipeBookRenderFixture();
        Setup(graphics);
        using var tabs = Create(graphics, ["Localized first", "Localized second", "Localized third"], 85, _ => { });
        var first = tabs.Composer!.GetToggleButton("tab-0").Bounds;
        SetMouse(graphics, first.absX + 8, first.absY + 8);
        var host = ElementBounds.Fixed(0, 0, 1000, 1000).WithEmptyParent();
        graphics.Clips.Push(host);
        graphics.Render.Setup(value => value.Render2DTexture(It.IsAny<int>(), It.IsAny<float>(),
            It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback<int, float, float, float, float, float, Vec4f>((_, _, _, _, _, z, _) =>
            {
                if (z >= 500) Assert.Same(host, graphics.Clips.Peek());
            });
        tabs.RenderInteractiveElements(0);
        Assert.True(tabs.Hints[0].IsVisible);
        Assert.Same(host, Assert.Single(graphics.Clips));
        SetMouse(graphics, -100, -100);
        tabs.OnMouseMove(graphics.Api.Object, new MouseEvent(-100, -100, EnumMouseButton.None, 0));
        tabs.RenderInteractiveElements(0);
        Assert.All(tabs.Hints, hint => Assert.False(hint.IsVisible));
        tabs.OnFocusGained();
        tabs.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Right });
        tabs.RenderInteractiveElements(0);
        Assert.True(tabs.Hints[1].IsVisible);
        tabs.OnFocusLost();
        tabs.RenderInteractiveElements(0);
        Assert.All(tabs.Hints, hint => Assert.False(hint.IsVisible));
        SetMouse(graphics, tabs.Bounds.absX + 1, tabs.Bounds.absY + 10);
        tabs.RenderInteractiveElements(0);
        Assert.All(tabs.Hints, hint => Assert.False(hint.IsVisible));
        SetMouse(graphics, tabs.Viewport!.absX + 5, tabs.Viewport.absY + 10);
        tabs.Enabled = false;
        tabs.RenderInteractiveElements(0);
        Assert.All(tabs.Hints, hint => Assert.False(hint.IsVisible));
    }

    [Fact]
    public void NativeCompositionRestoresTheHostClipWhenRenderingFails()
    {
        using var graphics = new RecipeBookRenderFixture();
        Setup(graphics);
        using var tabs = Create(graphics, ["One", "Two"], 150, _ => { });
        var host = ElementBounds.Fixed(0, 0, 1000, 1000).WithEmptyParent();
        graphics.Clips.Push(host);
        graphics.Render.Setup(value => value.Render2DTexturePremultipliedAlpha(It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<float>(), It.IsAny<Vec4f>()))
            .Callback(() =>
            {
                if (graphics.Clips.Count > 1) throw new InvalidOperationException("Native tab rendering failed.");
            });
        Assert.Throws<InvalidOperationException>(() => tabs.RenderInteractiveElements(0));
        Assert.Same(host, Assert.Single(graphics.Clips));
    }

    [Fact]
    public void IconClickSelectsAndInvalidSelectionsAndDescriptionsFailExplicitly()
    {
        using var graphics = new RecipeBookRenderFixture();
        Setup(graphics);
        var changes = new List<int>();
        using var tabs = Create(graphics, ["One", "Two", "Three"], 500, changes.Add);
        var bounds = tabs.Composer!.GetToggleButton("tab-1").Bounds;
        var args = new MouseEvent((int)bounds.absX + 14, (int)bounds.absY + 13, EnumMouseButton.Left, 0);
        tabs.OnMouseDownOnElement(graphics.Api.Object, args);
        Assert.True(args.Handled);
        Assert.Equal(1, tabs.activeElement);
        Assert.Equal(1, Assert.Single(changes));
        tabs.OnMouseDownOnElement(graphics.Api.Object, new MouseEvent(args.X, args.Y, EnumMouseButton.Left, 0));
        Assert.Equal(new[] { 1, 1 }, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => tabs.SetValue(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => tabs.SetValue(3));
        Assert.Throws<ArgumentException>(() => new GuiElementIconTabs(graphics.Api.Object,
            [new GuiTab { Name = "One" }], [], tabs.Bounds, _ => { }));
        Assert.Equal(1, tabs.activeElement);
    }

    private static void Setup(RecipeBookRenderFixture graphics)
    {
        var icons = new IconUtil(graphics.Api.Object);
        icons.CustomIcons["test-icon"] = (_, _, _, _, _, _) => { };
        graphics.Gui.SetupGet(value => value.Icons).Returns(icons);
        graphics.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [graphics.Api.Object, bounds, name], null)!);
        graphics.Render.SetupGet(value => value.FrameWidth).Returns(1000);
        graphics.Render.SetupGet(value => value.FrameHeight).Returns(1000);
        SetMouse(graphics, -100, -100);
    }

    private static void SetMouse(RecipeBookRenderFixture graphics, double x, double y)
    {
        var input = Mock.Get(graphics.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns((int)x);
        input.SetupGet(value => value.MouseY).Returns((int)y);
    }

    private static GuiElementIconTabs Create(RecipeBookRenderFixture graphics, string[] names, double width, Action<int> changed)
    {
        var bounds = ElementBounds.Fixed(120, 65, width, GuiElementIconTabs.TabHeight).WithEmptyParent();
        bounds.CalcWorldBounds();
        var tabs = new GuiElementIconTabs(graphics.Api.Object,
            names.Select((name, index) => new GuiTab { Name = name, DataInt = index }).ToArray(),
            names.Select(_ => "test-icon").ToArray(), bounds, changed);
        Compose(tabs);
        return tabs;
    }

    private static void Compose(GuiElementIconTabs tabs)
    {
        using var surface = new ImageSurface(Format.Argb32, tabs.Bounds.OuterWidthInt, tabs.Bounds.OuterHeightInt);
        using var ctx = new Context(surface);
        tabs.ComposeElements(ctx, surface);
    }

    private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);
}
