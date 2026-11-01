using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.Unit.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.Gui;

[Trait("Category", "Unit")]
[Collection("Recipe book native GUI")]
public sealed class GuiStyledControlsTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void ButtonPreservesNativeActivationAndRendersApprovedStates(float scale)
    {
        using var f = new ControlsFixture(scale);
        int clicks = 0;
        using var button = new GuiElementStyledButton(f.Api, "A caller-provided button", () => { clicks++; return true; },
            ElementBounds.Fixed(10, 10, 180, 28).WithEmptyParent());
        using var composer = f.Compose(button);
        f.Render(button);
        Assert.Equal(0x51412d, f.Drawn(0).Rgb(2, 2));
        Assert.Equal(0x756247, f.Drawn(0).Rgb(0, 10));
        Assert.True(f.Drawn(1).HasColor(0xc9bba0));
        int uploads = f.Uploads;
        f.SetMouse(button.Bounds.absX + 10, button.Bounds.absY + 10);
        f.Render(button);
        Assert.Equal(0x51422e, f.Drawn(0).Rgb(2, 2));
        var down = f.Mouse(button.Bounds.absX + 10, button.Bounds.absY + 10);
        button.OnMouseDownOnElement(f.Api, down);
        f.Render(button);
        Assert.Equal(0x382f23, f.Drawn(0).Rgb(2, 2));
        Assert.Equal(0, clicks);
        var up = f.Mouse(down.X, down.Y);
        button.OnMouseUpOnElement(f.Api, up);
        Assert.True(up.Handled);
        Assert.Equal(1, clicks);
        button.SetActive(true);
        f.Render(button);
        Assert.Equal(0x382f23, f.Drawn(0).Rgb(2, 2));
        button.SetActive(false);
        Assert.Equal(uploads, f.Uploads);
        button.Enabled = false;
        f.Render(button);
        Assert.Equal(0x51412d, f.Drawn(0).Rgb(2, 2));
        Assert.InRange(f.Drawn(1).MaxAlpha, 1, 90);
        button.OnFocusGained();
        button.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(1, clicks);
        button.Enabled = true;
        button.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(2, clicks);
        button.Visible = false;
        f.Render(button);
        Assert.Empty(f.Draws);
        composer.Dispose();
        button.Dispose();
        Assert.Empty(f.Graphics.Textures);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void ClearableInputUsesCallerLabelsAndNotifiesExactlyOnceWithNativeGeometry(float scale)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<string>();
        var callerFont = CairoFont.WhiteSmallText().WithFontSize(14);
        var originalColor = callerFont.Color.ToArray();
        using var input = new GuiElementClearableTextInput(f.Api, ElementBounds.Fixed(10, 10, 240, 28),
            "Caller placeholder", "Caller clear tooltip", changes.Add, callerFont);
        using var composer = f.Compose(input);
        Assert.Equal(originalColor, callerFont.Color);
        Assert.Equal(input.FieldBounds.absX + 8 * scale, input.Bounds.absX + 3, 4);
        Assert.True(input.Bounds.absX + input.Bounds.OuterWidth < input.ClearBounds.absX);
        Assert.Equal("Caller clear tooltip", input.ClearHint.GetText());
        f.Render(input);
        Assert.Equal(0x30281e, f.Drawn(0).Rgb(2, 2));
        Assert.True(f.Drawn(f.Draws.Length - 1).MaxAlpha > 90);
        Assert.True(f.Drawn(f.Draws.Length - 1).HasColor(0xa99b80));
        input.Enabled = false;
        f.Render(input);
        Assert.InRange(f.Drawn(f.Draws.Length - 1).MaxAlpha, 1, 90);
        input.Enabled = true;
        input.SetValue("two caller words");
        input.OnFocusGained();
        input.SetCaretPos(3);
        Assert.Equal(3, input.CaretPosInLine);
        int selectX = (int)input.Bounds.absX + 4;
        int selectY = (int)input.Bounds.absY + 10;
        input.OnMouseDownOnElement(f.Api, f.Mouse(selectX, selectY));
        input.OnMouseMove(f.Api, f.Mouse(selectX + 30, selectY));
        f.Render(input);
        int selectionId = (int)typeof(GuiElementEditableTextBase)
            .GetField("selectionTextureId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(input)!;
        var selection = Assert.Single(f.Graphics.Render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && (int)call.Arguments[0] == selectionId);
        Assert.Equal(input.FieldBounds.renderX + 8 * (double)scale, (double)selection.Arguments[1], 6);
        input.OnMouseUp(f.Api, f.Mouse(selectX + 30, selectY));
        changes.Clear();
        f.SetMouse(input.ClearBounds.absX + 8 * scale, input.ClearBounds.absY + 8 * scale);
        f.Render(input);
        Assert.True(input.ClearVisible);
        Assert.Equal("linkselect", input.MouseOverCursor);
        var down = f.Mouse(input.ClearBounds.absX + 8 * scale, input.ClearBounds.absY + 8 * scale);
        input.OnMouseDownOnElement(f.Api, down);
        Assert.True(down.Handled);
        var up = f.Mouse(down.X, down.Y);
        input.OnMouseUpOnElement(f.Api, up);
        Assert.True(up.Handled);
        Assert.Equal("", Assert.Single(changes));
        Assert.True(input.HasFocus);
        Assert.False(input.ClearVisible);
        input.SetValue("not cleared");
        changes.Clear();
        input.OnMouseDownOnElement(f.Api, down);
        input.OnMouseUp(f.Api, f.Mouse(-100, -100));
        input.OnMouseUpOnElement(f.Api, f.Mouse(up.X, up.Y));
        Assert.Empty(changes);
        Assert.Equal("not cleared", input.GetText());
        input.Enabled = false;
        input.OnMouseDownOnElement(f.Api, down);
        input.OnMouseUpOnElement(f.Api, f.Mouse(up.X, up.Y));
        Assert.Equal("not cleared", input.GetText());
        Assert.False(input.ClearVisible);
        composer.Dispose();
        input.Dispose();
        Assert.Empty(f.Graphics.Textures);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void DropdownCachesStyledResourcesAcrossNativeOpeningReplacementAndSelection(float scale)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<string>();
        using var dropdown = new GuiElementStyledDropDown(f.Api, ["a", "b"], ["Alpha", "Beta"], 0,
            (value, _) => changes.Add(value), ElementBounds.Fixed(10, 10, 180, 20));
        using var composer = f.Compose(dropdown);
        Assert.IsType<GuiElementStyledListMenu>(dropdown.listMenu);
        Assert.Equal(30 * scale, dropdown.Bounds.InnerHeight, 4);
        f.Render(dropdown);
        Assert.Equal(0x382f23, f.Drawn(0).Rgb(2, 2));
        Assert.True(f.Drawn(1).HasColor(0xd7c8aa));
        dropdown.listMenu.Open();
        f.Render(dropdown);
        Assert.Equal(0x51422e, f.Drawn(0).Rgb(2, 2));
        var popup = f.Popup();
        Assert.True(popup.HasColor(0xd7c8aa));
        Assert.Equal(0x382f23, popup.Rgb(2, popup.Height - 5));
        int uploads = f.Uploads;
        f.Render(dropdown);
        Assert.Equal(uploads, f.Uploads);
        double x = dropdown.listMenu.Bounds.renderX + 10 * scale;
        double y = dropdown.listMenu.Bounds.renderY + dropdown.listMenu.Bounds.InnerHeight + 45 * scale;
        f.SetMouse(x, y);
        dropdown.OnMouseMove(f.Api, f.Mouse(x, y));
        f.Render(dropdown);
        Assert.Equal(0x51422e, f.Popup().Rgb(f.Popup().Width - 15, (int)(45 * scale)));
        int stroke = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
        Assert.Equal(0xf5d58d, f.Popup().Rgb(stroke, (int)(15 * scale)));
        dropdown.OnMouseDown(f.Api, f.Mouse(x, y));
        Assert.Equal("b", dropdown.SelectedValue);
        Assert.Equal("b", Assert.Single(changes));
        Assert.False(dropdown.listMenu.IsOpened);
        dropdown.listMenu.Open();
        f.Render(dropdown);
        dropdown.listMenu.OnFocusLost();
        dropdown.listMenu.Open();
        uploads = f.Uploads;
        f.Render(dropdown);
        Assert.Equal(uploads, f.Uploads);
        Assert.Equal(0x382f23, f.Popup().Rgb(2, 2));
        dropdown.SetList(["b", "c", "d"], ["Beta changed", "Gamma", "Delta"]);
        dropdown.SetSelectedIndex(0);
        dropdown.listMenu.Open();
        f.Render(dropdown);
        Assert.False(popup.Pixels.SequenceEqual(f.Popup().Pixels));
        Assert.Equal((int)(90 * scale), f.Popup().Height);
        dropdown.Bounds.fixedWidth += 30;
        dropdown.Bounds.MarkDirtyRecursive();
        dropdown.Bounds.CalcWorldBounds();
        dropdown.listMenu.Open();
        f.Render(dropdown);
        var valueDraw = f.Draws[1];
        Assert.Equal((double)(int)(dropdown.Bounds.InnerWidth - 20 * scale), (double)valueDraw.Arguments[3]);
        uploads = f.Uploads;
        f.Render(dropdown);
        Assert.Equal(uploads, f.Uploads);
        dropdown.OnFocusGained();
        dropdown.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.End });
        dropdown.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal("d", dropdown.SelectedValue);
        Assert.Equal(new[] { "b", "d" }, changes);
        dropdown.Enabled = false;
        dropdown.OnFocusLost();
        Assert.False(dropdown.listMenu.IsOpened);
        dropdown.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Home });
        Assert.Equal("d", dropdown.SelectedValue);
        f.Render(dropdown);
        Assert.Equal(0x382f23, f.Drawn(0).Rgb(2, 2));
        Assert.InRange(f.Drawn(1).MaxAlpha, 1, 90);
        Assert.Empty(f.Graphics.Clips);
        composer.Dispose();
        dropdown.Dispose();
        Assert.Empty(f.Graphics.Textures);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void PopupSelectionPresenceInvalidatesCachedIndicator(float scale)
    {
        using var f = new ControlsFixture(scale);
        using var dropdown = new GuiElementStyledDropDown(f.Api, ["a"], ["Alpha"], 0,
            (_, _) => { }, ElementBounds.Fixed(10, 10, 180, 30));
        using var composer = f.Compose(dropdown);
        dropdown.listMenu.Open();
        f.Render(dropdown);
        Assert.True(f.Popup().HasColor(0xf5d58d));
        dropdown.SetSelectedIndex(-1);
        f.Render(dropdown);
        Assert.False(f.Popup().HasColor(0xf5d58d));
        dropdown.SetSelectedIndex(0);
        f.Render(dropdown);
        Assert.True(f.Popup().HasColor(0xf5d58d));
        int uploads = f.Uploads;
        f.Render(dropdown);
        Assert.Equal(uploads, f.Uploads);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void PopupLongLabelsKeepNativeWheelScrollingAndHitCoordinates(float scale)
    {
        using var f = new ControlsFixture(scale);
        var values = Enumerable.Range(0, 20).Select(i => "code-" + i).ToArray();
        var names = Enumerable.Range(0, 20).Select(i => "A long translated caller label " + i).ToArray();
        var changes = new List<string>();
        using var dropdown = new GuiElementStyledDropDown(f.Api, values, names, 0,
            (value, _) => changes.Add(value), ElementBounds.Fixed(10, 10, 40, 30));
        using var composer = f.Compose(dropdown);
        double measured = Math.Ceiling(dropdown.Font.GetTextExtents(names[^1]).Width / scale + 10) + 20;
        Assert.True(dropdown.Bounds.InnerWidth >= measured * scale);
        dropdown.OnFocusGained();
        dropdown.listMenu.MaxHeight = 60;
        dropdown.listMenu.Open();
        double x = dropdown.listMenu.Bounds.renderX + 10 * scale;
        double y = dropdown.listMenu.Bounds.renderY + 45 * scale;
        f.SetMouse(x, y);
        dropdown.OnMouseWheel(f.Api, new MouseWheelEventArgs { deltaPrecise = -1 });
        double scroll = (double)typeof(GuiElementListMenu)
            .GetField("scrollOffY", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dropdown.listMenu)!;
        Assert.True(scroll > 0);
        dropdown.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.End });
        f.Render(dropdown);
        Assert.Empty(f.Graphics.Clips);
        dropdown.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal("code-19", dropdown.SelectedValue);
        Assert.Equal("code-19", Assert.Single(changes));
        Assert.False(dropdown.listMenu.IsOpened);
    }

    [Fact]
    public void SharedControlsKeepNativeComposerTabRoutingAndSingleFocusOutline()
    {
        using var f = new ControlsFixture();
        using var composer = f.NewComposer();
        using var input = new GuiElementClearableTextInput(f.Api, ElementBounds.Fixed(0, 0, 200, 28),
            "Caller", "Clear caller text", _ => { });
        using var button = new GuiElementStyledButton(f.Api, "Caller button", () => true, ElementBounds.Fixed(0, 40, 100, 28));
        using var dropdown = new GuiElementStyledDropDown(f.Api, ["a"], ["Alpha"], 0,
            (_, _) => { }, ElementBounds.Fixed(0, 80, 100, 30));
        using var checkbox = new GuiElementStyledCheckbox(f.Api, _ => { }, ElementBounds.Fixed(0, 120, 17, 17));
        composer.AddInteractiveElement(input, "input").AddInteractiveElement(button, "button")
            .AddInteractiveElement(dropdown, "dropdown").AddInteractiveElement(checkbox, "checkbox").Compose(false);
        Assert.True(composer.FocusElement(0));
        foreach (var expected in new GuiElement[] { button, dropdown, checkbox, input })
        {
            composer.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Tab }, true);
            Assert.Same(expected, composer.CurrentTabIndexElement);
            f.Graphics.Render.Invocations.Clear();
            composer.Render(0);
            Assert.Single(f.Graphics.Render.Invocations, call => call.Method.Name == "RenderRectangle");
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void CheckboxHasFlatScaledFrameNativeTogglingAndDisabledIndicator(float scale)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<bool>();
        using var checkbox = new GuiElementStyledCheckbox(f.Api, changes.Add, ElementBounds.Fixed(10, 10, 40, 40));
        using var composer = f.Compose(checkbox);
        Assert.Equal(17 * scale, checkbox.Bounds.InnerWidth, 4);
        f.Render(checkbox);
        Assert.Equal(0x30291e, f.Drawn(0).Rgb(2, 2));
        Assert.Equal(0x948366, f.Drawn(0).Rgb(0, 8));
        checkbox.OnMouseDownOnElement(f.Api, f.Mouse(checkbox.Bounds.absX + 8, checkbox.Bounds.absY + 8));
        Assert.True(checkbox.On);
        Assert.True(Assert.Single(changes));
        f.Render(checkbox);
        Assert.InRange(f.Drawn(1).MaxAlpha, 180, 255);
        checkbox.Enabled = false;
        checkbox.OnFocusGained();
        checkbox.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Space });
        Assert.Single(changes);
        f.Render(checkbox);
        Assert.InRange(f.Drawn(1).MaxAlpha, 1, 90);
        checkbox.Enabled = true;
        checkbox.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.False(checkbox.On);
        Assert.Equal(new[] { true, false }, changes);
        composer.Dispose();
        checkbox.Dispose();
        Assert.Empty(f.Graphics.Textures);
    }

    [Fact]
    public void NativeHelpersRemainOptInAndInvalidDropdownsFailExplicitly()
    {
        using var f = new ControlsFixture();
        using var composer = f.NewComposer();
        composer.AddSmallButton("Native", () => true, ElementBounds.Fixed(0, 0, 100, 30), key: "button")
            .AddSwitch(_ => { }, ElementBounds.Fixed(0, 40, 30, 30), "checkbox")
            .AddTextInput(ElementBounds.Fixed(0, 80, 100, 30), _ => { }, key: "input")
            .AddDropDown(["a"], ["Alpha"], 0, (_, _) => { }, ElementBounds.Fixed(0, 120, 100, 30), "dropdown");
        Assert.IsType<GuiElementTextButton>(composer.GetButton("button"));
        Assert.IsType<GuiElementSwitch>(composer.GetSwitch("checkbox"));
        Assert.IsType<GuiElementTextInput>(composer.GetTextInput("input"));
        Assert.IsType<GuiElementDropDown>(composer.GetDropDown("dropdown"));
        Assert.Throws<ArgumentException>(() => new GuiElementStyledDropDown(f.Api, [], [], 0,
            (_, _) => { }, ElementBounds.Fixed(0, 0, 100, 30)));
    }

    private sealed record Raster(int Width, int Height, int Stride, byte[] Pixels)
    {
        internal int Rgb(int x, int y)
        {
            int p = y * Stride + x * 4;
            return Pixels[p + 2] << 16 | Pixels[p + 1] << 8 | Pixels[p];
        }
        internal int MaxAlpha => Pixels.Where((_, i) => i % 4 == 3).Max(value => (int)value);
        internal bool HasColor(int rgb)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Pixels[y * Stride + x * 4 + 3] == 255 && Rgb(x, y) == rgb) return true;
            return false;
        }
    }

    private sealed class ControlsFixture : IDisposable
    {
        internal readonly RecipeBookRenderFixture Graphics;
        internal ICoreClientAPI Api => Graphics.Api.Object;
        private readonly string oldLocale = Lang.CurrentLocale;
        private readonly ITranslationService? oldEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        private readonly Dictionary<int, Raster> rasters = new();
        private int nextTexture = 10000;
        internal int Uploads;
        internal Moq.IInvocation[] Draws => Graphics.Render.Invocations.Where(call =>
            call.Method.Name.StartsWith("Render2DTexture", StringComparison.Ordinal)).ToArray();

        internal ControlsFixture(float scale = 1)
        {
            Graphics = new RecipeBookRenderFixture(scale);
            var translations = new Mock<ITranslationService>();
            translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key);
            Lang.AvailableLanguages["en"] = translations.Object;
            Lang.ChangeLanguage("en");
            Graphics.Render.SetupGet(value => value.FrameWidth).Returns(1000);
            Graphics.Render.SetupGet(value => value.FrameHeight).Returns(1000);
            Graphics.Gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(),
                ref It.Ref<LoadedTexture>.IsAny)).Callback(new Upload((ImageSurface image, bool _, ref LoadedTexture texture) =>
                {
                    if (texture.TextureId == 0) { texture.TextureId = ++nextTexture; Graphics.Textures.Add(texture.TextureId); }
                    texture.Width = image.Width;
                    texture.Height = image.Height;
                    image.Flush();
                    rasters[texture.TextureId] = new(image.Width, image.Height, image.Stride, image.Data.ToArray());
                    Uploads++;
                }));
            Graphics.Gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>()))
                .Returns(() => { int id = ++nextTexture; Graphics.Textures.Add(id); return id; });
            Graphics.Render.Setup(value => value.GLDeleteTexture(It.IsAny<int>()))
                .Callback<int>(id => Assert.True(Graphics.Textures.Remove(id)));
            Mock.Get(Api.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[512]);
            Graphics.Api.SetupGet(value => value.ElapsedMilliseconds).Returns(1000);
            SetMouse(-100, -100);
        }

        internal GuiComposer NewComposer() => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [Api, ElementBounds.Fixed(0, 0, 600, 300).WithEmptyParent(), "styled-controls"], null)!;

        internal GuiComposer Compose(GuiElement control)
        {
            var composer = NewComposer();
            composer.AddInteractiveElement(control, "control").Compose(false);
            return composer;
        }

        internal void Render(GuiElement control)
        {
            Graphics.Render.Invocations.Clear();
            control.RenderInteractiveElements(0);
        }
        internal Raster Drawn(int index) => rasters[(int)Draws[index].Arguments[0]];
        internal Raster Popup() => rasters[(int)Draws.Single(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexture) && (float)call.Arguments[5] == 310).Arguments[0]];
        internal MouseEvent Mouse(double x, double y) => new((int)x, (int)y, EnumMouseButton.Left, 0);
        internal void SetMouse(double x, double y)
        {
            Mock.Get(Api.Input).SetupGet(value => value.MouseX).Returns((int)x);
            Mock.Get(Api.Input).SetupGet(value => value.MouseY).Returns((int)y);
        }
        public void Dispose()
        {
            if (oldEnglish == null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = oldEnglish;
            Lang.ChangeLanguage(oldLocale);
            Graphics.Dispose();
        }
        private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);
    }
}
