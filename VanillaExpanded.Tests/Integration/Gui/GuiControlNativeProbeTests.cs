using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.Integration.Gui;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class GuiControlNativeProbeTests: IDisposable
{
    [Fact]
    public void NativeButtonActivationRespectsDisabledAndHiddenState()
    {
        // Arrange
        using var graphics = Graphics();
        int clicks = 0;
        using var button = new GuiElementTextButton(graphics.Api.Object, "Button",
            CairoFont.WhiteSmallText().WithFontSize(12), CairoFont.WhiteSmallText().WithFontSize(12),
            () => { clicks++; return true; }, ElementBounds.Fixed(0, 0, 100, 26).WithEmptyParent(),
            EnumButtonStyle.None);
        using var surface = new ImageSurface(Format.Argb32, 200, 80);
        using var ctx = new Context(surface);

        // Act
        button.BeforeCalcBounds();
        button.ComposeElements(ctx, surface);

        // Act
        var mouse = new MouseEvent(10, 10, EnumMouseButton.Left, 0);
        SetMouse(graphics, 10, 10);
        button.OnMouseDownOnElement(graphics.Api.Object, mouse);
        button.OnMouseUpOnElement(graphics.Api.Object, mouse);

        // Assert
        Assert.Equal(1, clicks);

        // Act
        button.OnFocusGained();
        button.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Enter });

        // Assert
        Assert.Equal(2, clicks);

        // Act
        button.Enabled = false;
        button.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Enter });

        // Assert
        Assert.Equal(2, clicks);

        // Act
        button.Visible = false;
        graphics.Render.Invocations.Clear();
        button.RenderInteractiveElements(0);

        // Assert
        Assert.Empty(graphics.Render.Invocations);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void CallerPlaceholderVisibilityAndEditingKeepHitRegionsDistinct(float scale)
    {
        using var graphics = Graphics(scale);
        var changes = new List<string>();
        using var input = new InputProbe(graphics.Api.Object,
            ElementBounds.Fixed(10, 10, 200, 28).WithEmptyParent(), "Caller placeholder", changes.Add);
        using var surface = new ImageSurface(Format.Argb32, 600, 120);
        using var ctx = new Context(surface);
        input.ComposeElements(ctx, surface);
        int editX = (int)(input.Bounds.absX + input.Bounds.OuterWidth / 2);
        int editY = (int)(input.Bounds.absY + input.Bounds.OuterHeight / 2);
        int clearX = (int)(input.Clear.absX + input.Clear.OuterWidth / 2);
        int clearY = (int)(input.Clear.absY + input.Clear.OuterHeight / 2);
        Assert.True(input.Bounds.PointInside(editX, editY));
        Assert.False(input.Clear.PointInside(editX, editY));
        Assert.True(input.Clear.PointInside(clearX, clearY));
        Assert.False(input.Bounds.PointInside(clearX, clearY));
        input.RenderInteractiveElements(0);
        Assert.Equal(1, input.PlaceholderRenders);
        input.OnFocusGained();
        input.SetValue("abcdef");
        input.SetCaretPos(3);
        input.RenderInteractiveElements(0);
        Assert.Equal(1, input.PlaceholderRenders);
        Assert.Equal(3, input.CaretPosInLine);
        int x = editX;
        int y = editY;
        input.OnMouseDownOnElement(graphics.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        input.OnMouseMove(graphics.Api.Object,
            new MouseEvent(x + (int)(input.Bounds.OuterWidth / 4), y, EnumMouseButton.Left, 0));
        Assert.True(input.HasFocus);
        input.SetValue("");
        Assert.Equal(new[] { "abcdef", "" }, changes);
        input.OnFocusLost();
        input.RenderInteractiveElements(0);
        Assert.Equal(2, input.PlaceholderRenders);
    }

    [Fact]
    public void NativePopupOpensSelectsAndReplacesItsListWithFunctionalHitTesting()
    {
        using var graphics = Graphics();
        var changes = new List<string>();
        using var dropdown = new GuiElementDropDown(graphics.Api.Object, ["a", "b"], ["Alpha", "Beta"], 0,
            (value, _) => changes.Add(value), ElementBounds.Fixed(20, 20, 160, 30).WithEmptyParent(),
            CairoFont.WhiteSmallText().WithFontSize(12), false);
        using var surface = new ImageSurface(Format.Argb32, 300, 150);
        using var ctx = new Context(surface);
        dropdown.ComposeElements(ctx, surface);
        var menu = dropdown.listMenu;
        menu.Open();
        Assert.True(menu.IsOpened);
        dropdown.RenderInteractiveElements(0);
        double x = menu.Bounds.renderX + menu.Bounds.InnerWidth / 2;
        double y = menu.Bounds.renderY + menu.Bounds.InnerHeight + 1.5 * menu.Bounds.OuterHeight;
        SetMouse(graphics, x, y);
        menu.OnMouseDown(graphics.Api.Object, new MouseEvent((int)x, (int)y, EnumMouseButton.Left, 0));
        Assert.Equal("b", dropdown.SelectedValue);
        Assert.Equal("b", Assert.Single(changes));
        Assert.False(menu.IsOpened);
        dropdown.SetList(["b", "c", "d"], ["Beta", "Gamma", "Delta"]);
        dropdown.SetSelectedIndex(1);
        menu.Open();
        Assert.True(menu.IsOpened);
        dropdown.RenderInteractiveElements(0);
        Assert.Equal("c", dropdown.SelectedValue);
        dropdown.OnFocusGained();
        dropdown.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.End });
        dropdown.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal("d", dropdown.SelectedValue);
        Assert.Equal(new[] { "b", "d" }, changes);
        Assert.Empty(graphics.Clips);
    }

    [Fact]
    public void NativeComposerRetainsTabRouting()
    {
        using var graphics = Graphics();
        using var composer = Composer(graphics, ElementBounds.Fixed(0, 0, 300, 150).WithEmptyParent());
        using var first = new GuiElementTextInput(graphics.Api.Object, ElementBounds.Fixed(0, 0, 100, 28), _ => { },
            CairoFont.WhiteSmallText());
        using var strip = new GuiElementIconTabs(graphics.Api.Object,
            [new GuiTab { Name = "One" }, new GuiTab { Name = "Two" }], ["probe", "probe"],
            ElementBounds.Fixed(0, 40, 150, 36), _ => { });
        composer.AddInteractiveElement(first, "search").AddInteractiveElement(strip, "strip").Compose(false);
        Assert.True(composer.FocusElement(0));
        composer.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Tab }, true);
        Assert.Same(strip, composer.CurrentTabIndexElement);
        composer.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = true }, true);
        Assert.Same(first, composer.CurrentTabIndexElement);
    }

    [Fact]
    public void NativeTitleBarPersistsDragPositionAndInvokesClose()
    {
        using var graphics = Graphics();
        graphics.Gui.Setup(value => value.GetDialogPosition(It.IsAny<string>())).Returns(new Vec2i(20, 20));
        Vec2i? saved = null;
        graphics.Gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()))
            .Callback<string, Vec2i>((_, position) => saved = position);
        var bounds = ElementBounds.Fixed(20, 20, 320, 120).WithEmptyParent();
        using var composer = Composer(graphics, bounds);
        bool closed = false;
        var title = new GuiElementDialogTitleBar(graphics.Api.Object, "Title", composer, () => closed = true,
            CairoFont.WhiteSmallText(),
            ElementBounds.Fixed(0, 0, 320, 36));
        composer.AddInteractiveElement(title, "title").Compose(false);
        title.RenderInteractiveElements(0);
        Assert.True(title.Movable);
        int x = (int)title.Bounds.absX + 150;
        int y = (int)title.Bounds.absY + 12;
        title.OnMouseDown(graphics.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        title.OnMouseMove(graphics.Api.Object, new MouseEvent(x + 10, y + 6, EnumMouseButton.Left, 0));
        title.OnMouseUp(graphics.Api.Object, new MouseEvent(x + 10, y + 6, EnumMouseButton.Left, 0));
        Assert.NotNull(saved);
        Assert.Equal(30, saved.X);
        Assert.Equal(26, saved.Y);
        int closeX = (int)(title.Bounds.absX + title.Bounds.OuterWidth - 20);
        title.OnMouseUpOnElement(graphics.Api.Object,
            new MouseEvent(closeX, (int)title.Bounds.absY + 10, EnumMouseButton.Left, 0));
        Assert.True(closed);
    }

private readonly string originalLocale = Lang.CurrentLocale;
    private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");

    public GuiControlNativeProbeTests()
    {
        var translations = new Mock<ITranslationService>();
        translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
            .Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = translations.Object;
        Lang.ChangeLanguage("en");
    }

    public void Dispose()
    {
        if (originalEnglish == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = originalEnglish;
        Lang.ChangeLanguage(originalLocale);
    }











    private static GuiRenderFixture Graphics(float scale = 1)
    {
        var graphics = new GuiRenderFixture(scale);
        graphics.Render.SetupGet(value => value.FrameWidth).Returns(1000);
        graphics.Render.SetupGet(value => value.FrameHeight).Returns(1000);
        var icons = new IconUtil(graphics.Api.Object);
        icons.CustomIcons["probe"] = (_, _, _, _, _, _) => { };
        graphics.Gui.SetupGet(value => value.Icons).Returns(icons);
        int nativeTexture = 10000;
        graphics.Gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>()))
            .Returns(() =>
            {
                int texture = ++nativeTexture;
                graphics.Textures.Add(texture);
                return texture;
            });
        graphics.Render.Setup(value => value.GLDeleteTexture(It.IsAny<int>()))
            .Callback<int>(texture => Assert.True(graphics.Textures.Remove(texture)));
        graphics.Api.SetupGet(value => value.ElapsedMilliseconds).Returns(1000);
        Mock.Get(graphics.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[512]);
        graphics.Gui.Setup(value => value.CreateCompo(It.IsAny<string>(), It.IsAny<ElementBounds>()))
            .Returns((string name, ElementBounds bounds) => Composer(graphics, bounds, name));
        SetMouse(graphics, -100, -100);
        return graphics;
    }

    private static GuiComposer Composer(GuiRenderFixture graphics, ElementBounds bounds, string name = "native-probe") =>
        (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [graphics.Api.Object, bounds, name], null)!;

    private static void SetMouse(GuiRenderFixture graphics, double x, double y)
    {
        var input = Mock.Get(graphics.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns((int)x);
        input.SetupGet(value => value.MouseY).Returns((int)y);
    }

    private sealed class InputProbe : GuiElementTextInput
    {
        internal readonly ElementBounds Field;
        internal readonly ElementBounds Clear;
        private LoadedTexture placeholder;
        private readonly string label;
        internal int PlaceholderRenders;

        internal InputProbe(ICoreClientAPI api, ElementBounds field, string label, Action<string> changed)
            : base(api, GuiControlMetrics.InputBounds(field).Editable, changed,
                CairoFont.WhiteSmallText().WithFontSize(14))
        {
            Field = field;
            Clear = GuiControlMetrics.InputBounds(field).Clear.WithParent(field.ParentBounds);
            this.label = label;
            placeholder = new LoadedTexture(api);
        }

        public override void ComposeTextElements(Context ctx, ImageSurface surface)
        {
            Field.CalcWorldBounds();
            Clear.CalcWorldBounds();
            using (var scratch = new ImageSurface(Format.Argb32, 1, 1))
            using (var native = new Context(scratch))
                base.ComposeTextElements(native, scratch);
            highlightBounds = Field;
            using var image = new ImageSurface(Format.Argb32, Field.OuterWidthInt, Field.OuterHeightInt);
            using var context = new Context(image);
            var font = Font.Clone();
            font.SetupContext(context);
            textUtil.DrawTextLine(context, font, label, scaled(8), (Field.OuterHeight - font.GetFontExtents().Height) / 2);
            generateTexture(image, ref placeholder);
        }

        public override void RenderInteractiveElements(float deltaTime)
        {
            base.RenderInteractiveElements(deltaTime);
            if (HasFocus || GetText().Length != 0) return;
            api.Render.Render2DTexturePremultipliedAlpha(placeholder.TextureId, Field);
            PlaceholderRenders++;
        }

        public override void Dispose()
        {
            placeholder.Dispose();
            base.Dispose();
        }
    }
}
