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
public sealed class GuiControlNativeProbeTests : IDisposable
{
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

    [Fact]
    public void BorderlessButtonRetainsNativeActivationAndVisibilityWithLocalFlatChrome()
    {
        // Arrange
        using var graphics = Graphics();
        int clicks = 0;
        using var button = new ButtonProbe(graphics.Api.Object, () => { clicks++; return true; },
            ElementBounds.Fixed(0, 0, 100, 26).WithEmptyParent());
        using var surface = new ImageSurface(Format.Argb32, 200, 80);
        using var ctx = new Context(surface);
        int untouchedPixel = GuiControlVisualsTests.Pixel(surface, 120, 40);

        // Act
        button.BeforeCalcBounds();
        button.ComposeElements(ctx, surface);

        // Assert
        int interiorPixel = GuiControlVisualsTests.Pixel(surface, 2, 2);
        Assert.NotEqual(untouchedPixel, interiorPixel);
        Assert.Equal(interiorPixel, GuiControlVisualsTests.Pixel(surface, 90, 20));
        Assert.Equal(untouchedPixel, GuiControlVisualsTests.Pixel(surface, 120, 40));

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
    public void InputChromeAndCallerPlaceholderUseEightUnitInsetWithoutEngineFieldAccess(float scale)
    {
        using var graphics = Graphics(scale);
        var changes = new List<string>();
        using var input = new InputProbe(graphics.Api.Object,
            ElementBounds.Fixed(10, 10, 200, 28).WithEmptyParent(), "Caller placeholder", changes.Add);
        using var surface = new ImageSurface(Format.Argb32, 600, 120);
        using var ctx = new Context(surface);
        input.ComposeElements(ctx, surface);
        double nativeInset = (double)typeof(GuiElementEditableTextBase)
            .GetField("leftPadding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        Assert.Equal(3, nativeInset);
        Assert.Equal(8 * (double)scale, input.Bounds.absX - input.Field.absX + nativeInset, 6);
        Assert.True(input.Bounds.absX + input.Bounds.OuterWidth < input.Clear.absX);
        input.RenderInteractiveElements(0);
        Assert.Equal(1, input.PlaceholderRenders);
        input.OnFocusGained();
        input.SetValue("abcdef");
        input.SetCaretPos(3);
        input.RenderInteractiveElements(0);
        Assert.Equal(1, input.PlaceholderRenders);
        Assert.Equal(3, input.CaretPosInLine);
        int x = (int)input.Bounds.absX + 4;
        int y = (int)input.Bounds.absY + 10;
        input.OnMouseDownOnElement(graphics.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        input.OnMouseMove(graphics.Api.Object, new MouseEvent(x + 30, y, EnumMouseButton.Left, 0));
        graphics.Render.Invocations.Clear();
        input.RenderInteractiveElements(0);
        int selectionTexture = (int)typeof(GuiElementEditableTextBase)
            .GetField("selectionTextureId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        var selection = Assert.Single(graphics.Render.Invocations, call =>
            call.Method.Name == "Render2DTexturePremultipliedAlpha" && (int)call.Arguments[0] == selectionTexture);
        Assert.Equal(input.Field.renderX + 8 * (double)scale, (double)selection.Arguments[1], 6);
        input.SetValue("");
        Assert.Equal(new[] { "abcdef", "" }, changes);
        input.OnFocusLost();
        input.RenderInteractiveElements(0);
        Assert.Equal(2, input.PlaceholderRenders);
        Assert.Contains(0xa99b80, input.PlaceholderColors);
    }

    [Fact]
    public void PopupRestylesAfterNativeOpenSelectionAndListReplacementWithoutChangingRowGeometry()
    {
        using var graphics = Graphics();
        var changes = new List<string>();
        using var dropdown = new DropdownProbe(graphics.Api.Object, changes.Add,
            ElementBounds.Fixed(20, 20, 160, 30).WithEmptyParent());
        using var surface = new ImageSurface(Format.Argb32, 300, 150);
        using var ctx = new Context(surface);
        dropdown.ComposeElements(ctx, surface);
        Assert.Equal(0x382f23, GuiControlVisualsTests.Pixel(surface, 25, 25));
        var menu = Assert.IsType<ListProbe>(dropdown.listMenu);
        menu.Open();
        dropdown.RenderInteractiveElements(0);
        Assert.Equal(0x382f23, menu.LastFill);
        Assert.Equal(30, menu.RowHeight);
        double x = menu.Bounds.renderX + 20;
        double y = menu.Bounds.renderY + menu.Bounds.InnerHeight + 45;
        SetMouse(graphics, x, y);
        menu.OnMouseDown(graphics.Api.Object, new MouseEvent((int)x, (int)y, EnumMouseButton.Left, 0));
        Assert.Equal("b", dropdown.SelectedValue);
        Assert.Equal("b", Assert.Single(changes));
        Assert.False(menu.IsOpened);
        dropdown.SetList(["b", "c", "d"], ["Beta", "Gamma", "Delta"]);
        dropdown.SetSelectedIndex(1);
        menu.Open();
        dropdown.RenderInteractiveElements(0);
        Assert.Equal(0x382f23, menu.LastFill);
        Assert.Equal("c", dropdown.SelectedValue);
        dropdown.OnFocusGained();
        dropdown.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.End });
        dropdown.OnKeyDown(graphics.Api.Object, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal("d", dropdown.SelectedValue);
        Assert.Equal(new[] { "b", "d" }, changes);
        Assert.Empty(graphics.Clips);
    }

    [Fact]
    public void NativeComposerRetainsSingleStripFocusOutlineAndTabRouting()
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
        graphics.Render.Invocations.Clear();
        composer.Render(0);
        var outlines = graphics.Render.Invocations.Where(call => call.Method.Name == "RenderRectangle").ToArray();
        Assert.Single(outlines);
        composer.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Tab, ShiftPressed = true }, true);
        Assert.Same(first, composer.CurrentTabIndexElement);
    }

    [Fact]
    public void TitleChromeCanBeOverpaintedLocallyWhileNativeDragPositionAndCloseRemainAuthoritative()
    {
        using var graphics = Graphics();
        graphics.Gui.Setup(value => value.GetDialogPosition(It.IsAny<string>())).Returns(new Vec2i(20, 20));
        Vec2i? saved = null;
        graphics.Gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()))
            .Callback<string, Vec2i>((_, position) => saved = position);
        var bounds = ElementBounds.Fixed(20, 20, 320, 120).WithEmptyParent();
        using var composer = Composer(graphics, bounds);
        bool closed = false;
        var title = new TitleProbe(graphics.Api.Object, composer, () => closed = true,
            ElementBounds.Fixed(0, 0, 320, 36));
        composer.AddInteractiveElement(title, "title").Compose(false);
        Assert.Equal(0x65553e, title.TopFill);
        Assert.Equal(0x4a3c2c, title.BottomFill);
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

    private static RecipeBookRenderFixture Graphics(float scale = 1)
    {
        var graphics = new RecipeBookRenderFixture(scale);
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

    private static GuiComposer Composer(RecipeBookRenderFixture graphics, ElementBounds bounds, string name = "native-probe") =>
        (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [graphics.Api.Object, bounds, name], null)!;

    private static void SetMouse(RecipeBookRenderFixture graphics, double x, double y)
    {
        var input = Mock.Get(graphics.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns((int)x);
        input.SetupGet(value => value.MouseY).Returns((int)y);
    }

    private sealed class ButtonProbe(ICoreClientAPI api, ActionConsumable clicked, ElementBounds bounds)
        : GuiElementTextButton(api, "Button", CairoFont.WhiteSmallText().WithFontSize(12),
            CairoFont.WhiteSmallText().WithFontSize(12), clicked, bounds, EnumButtonStyle.None)
    {
        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            base.ComposeElements(ctx, surface);
            GuiControlDrawing.Frame(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight,
                GuiControlPalette.ButtonFill, GuiControlPalette.ButtonOutline, scaled(1));
        }
    }

    private sealed class InputProbe : GuiElementTextInput
    {
        internal readonly ElementBounds Field;
        internal readonly ElementBounds Clear;
        private LoadedTexture placeholder;
        private readonly string label;
        internal int PlaceholderRenders;
        internal readonly HashSet<int> PlaceholderColors = new();

        internal InputProbe(ICoreClientAPI api, ElementBounds field, string label, Action<string> changed)
            : base(api, GuiControlMetrics.InputBounds(field).Editable, changed,
                CairoFont.WhiteSmallText().WithFontSize(14).WithColor(GuiControlPalette.Color(GuiControlPalette.Text)))
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
            GuiControlDrawing.Frame(ctx, Field.drawX, Field.drawY, Field.OuterWidth, Field.OuterHeight,
                GuiControlPalette.SearchFill, GuiControlPalette.SearchOutline, scaled(1));
            highlightBounds = Field;
            using var image = new ImageSurface(Format.Argb32, Field.OuterWidthInt, Field.OuterHeightInt);
            using var context = new Context(image);
            var font = Font.Clone().WithColor(GuiControlPalette.Color(GuiControlPalette.Placeholder));
            font.SetupContext(context);
            textUtil.DrawTextLine(context, font, label, scaled(8), (Field.OuterHeight - font.GetFontExtents().Height) / 2);
            image.Flush();
            var pixels = image.Data;
            for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    int offset = y * image.Stride + x * 4;
                    PlaceholderColors.Add(pixels[offset + 2] << 16 | pixels[offset + 1] << 8 | pixels[offset]);
                }
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

    private sealed class DropdownProbe : GuiElementDropDown
    {
        internal DropdownProbe(ICoreClientAPI api, Action<string> changed, ElementBounds bounds)
            : base(api, ["a", "b"], ["Alpha", "Beta"], 0, (value, _) => changed(value),
                bounds, CairoFont.WhiteSmallText().WithFontSize(12)
                    .WithColor(GuiControlPalette.Color(GuiControlPalette.DropdownText)), false)
        {
            listMenu.Dispose();
            listMenu = new ListProbe(api, ["a", "b"], ["Alpha", "Beta"], (value, selected) =>
            {
                onSelectionChanged(value, selected);
                SetSelectedIndex(listMenu.SelectedIndex);
            }, bounds.ForkChildOffseted(-bounds.fixedX, -bounds.fixedY));
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            using (var scratch = new ImageSurface(Format.Argb32, surface.Width, surface.Height))
            using (var native = new Context(scratch))
                base.ComposeElements(native, scratch);
            GuiControlDrawing.Frame(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight,
                GuiControlPalette.DropdownFill, GuiControlPalette.DropdownOutline, scaled(1));
            using var arrow = new ImageSurface(Format.Argb32, (int)scaled(20), (int)Bounds.InnerHeight);
            using var arrowContext = new Context(arrow);
            arrowContext.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.DropdownText));
            arrowContext.LineWidth = Math.Max(1, GuiControlDrawing.Pixels(1, scaled(1)));
            arrowContext.MoveTo(scaled(6), Bounds.InnerHeight / 2 - scaled(2));
            arrowContext.LineTo(scaled(10), Bounds.InnerHeight / 2 + scaled(2));
            arrowContext.LineTo(scaled(14), Bounds.InnerHeight / 2 - scaled(2));
            arrowContext.Stroke();
            generateTexture(arrow, ref arrowDownButtonReleased);
            generateTexture(arrow, ref arrowDownButtonPressed);
        }
    }

    private sealed class ListProbe(ICoreClientAPI api, string[] values, string[] names,
        SelectionChangedDelegate changed, ElementBounds bounds)
        : GuiElementListMenu(api, values, names, 0, changed, bounds, CairoFont.WhiteSmallText().WithFontSize(12)
            .WithColor(GuiControlPalette.Color(GuiControlPalette.DropdownText)), false)
    {
        internal int LastFill;
        internal double RowHeight => unscaledLineHeight;

        public override void RenderInteractiveElements(float deltaTime)
        {
            if (IsOpened)
            {
                using var image = new ImageSurface(Format.Argb32, (int)expandedBoxWidth, (int)expandedBoxHeight);
                using var ctx = new Context(image);
                GuiControlDrawing.Frame(ctx, 0, 0, expandedBoxWidth, expandedBoxHeight,
                    GuiControlPalette.DropdownFill, GuiControlPalette.DropdownOutline, scaled(1));
                foreach (var text in richtTextElem) text.ComposeFor(text.Bounds, ctx, image);
                LastFill = GuiControlVisualsTests.Pixel(image, 2, 2);
                generateTexture(image, ref dropDownTexture);
                using var hover = new ImageSurface(Format.Argb32, (int)expandedBoxWidth, (int)(unscaledLineHeight * scaled(1)));
                using var hoverContext = new Context(hover);
                hoverContext.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.HoverFill));
                hoverContext.Paint();
                if (HoveredIndex >= 0 && HoveredIndex < Names.Length)
                {
                    Font.SetupContext(hoverContext);
                    textUtil.DrawTextLine(hoverContext, Font, Names[HoveredIndex], scaled(5),
                        (hover.Height - Font.GetFontExtents().Height) / 2);
                }
                generateTexture(hover, ref hoverTexture);
            }
            base.RenderInteractiveElements(deltaTime);
        }
    }

    private sealed class TitleProbe(ICoreClientAPI api, GuiComposer composer, Action closed, ElementBounds bounds)
        : GuiElementDialogTitleBar(api, "Title", composer, closed,
            CairoFont.WhiteSmallText().WithFontSize(14).WithWeight(FontWeight.Bold), bounds)
    {
        internal int TopFill;
        internal int BottomFill;

        public override void ComposeTextElements(Context ctx, ImageSurface surface)
        {
            using (var scratch = new ImageSurface(Format.Argb32, surface.Width, surface.Height))
            using (var native = new Context(scratch))
                base.ComposeTextElements(native, scratch);
            GuiControlDrawing.Frame(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight,
                GuiControlPalette.TitleBottom, GuiControlPalette.DialogOutline, scaled(1));
            double stroke = Math.Max(1, GuiControlDrawing.Pixels(1, scaled(1)));
            GuiControlDrawing.Gradient(ctx, Bounds.drawX + stroke, Bounds.drawY + stroke,
                Bounds.OuterWidth - 2 * stroke, Bounds.OuterHeight - 2 * stroke,
                GuiControlPalette.TitleTop, GuiControlPalette.TitleBottom);
            Font.Color = GuiControlPalette.Color(GuiControlPalette.Text);
            Font.SetupContext(ctx);
            DrawTextLineAt(ctx, "Title", scaled(20), (Bounds.InnerHeight - Font.GetFontExtents().Height) / 2);
            double cross = scaled(unscaledCloseIconSize);
            double menu = scaled(unscaledCloseIconSize + 2);
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.Text));
            api.Gui.Icons.DrawCross(ctx, Bounds.drawX + Bounds.OuterWidth - cross - scaled(12),
                Bounds.drawY + scaled(7), scaled(2), cross);
            api.Gui.Icons.Drawmenuicon_svg(ctx, (int)(Bounds.drawX + Bounds.OuterWidth - cross - menu - scaled(20)),
                (int)(Bounds.drawY + scaled(8)), (int)menu, (int)menu, GuiControlPalette.Color(GuiControlPalette.Text));
            ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.TitleSeparator));
            ctx.Rectangle(Bounds.drawX, Bounds.drawY + Bounds.OuterHeight - stroke, Bounds.OuterWidth, stroke);
            ctx.Fill();
            TopFill = GuiControlVisualsTests.Pixel(surface, (int)Bounds.drawX + 150, (int)Bounds.drawY + 1);
            BottomFill = GuiControlVisualsTests.Pixel(surface, (int)Bounds.drawX + 150, (int)(Bounds.drawY + Bounds.OuterHeight) - 2);
        }
    }
}
