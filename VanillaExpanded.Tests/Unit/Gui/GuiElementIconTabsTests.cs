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
        Assert.Equal(tabs.Bounds.absX + 5 * scale, first.Bounds.absX, 4);
        Assert.Equal(first.Bounds.absX + first.Bounds.OuterWidth + 5 * scale, second.Bounds.absX, 4);
        Assert.Equal(tabs.Bounds.absY + tabs.Bounds.InnerHeight / 2,
            first.Bounds.absY + first.Bounds.OuterHeight / 2, 4);
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
            It.IsAny<ElementBounds>(), It.IsAny<float>(), It.IsAny<Vec4f>()))
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
        var bounds = ElementBounds.Fixed(120, 65, width, 30).WithEmptyParent();
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
}
