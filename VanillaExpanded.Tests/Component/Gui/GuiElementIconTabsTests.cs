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
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.Gui.GuiElementIconTabsTestsTestSupport;

namespace VanillaExpanded.Tests.Component.Gui;

[Trait(TestCategories.TraitName, TestCategories.Component)]
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
        Assert.Equal(3, draws.Count);
        Assert.All(draws, draw =>
        {
            Assert.Equal(draw.Width, draw.Height);
            Assert.Equal((double)draw.Width, draw.WidthRequested);
        });
        Assert.Equal(draws[0].Width, draws[1].Width);
        Assert.Equal(Color(0xb7a98c), draws[0].Color);
        Assert.Equal(Color(0xfff0cc), draws[1].Color);
        Assert.Equal(Color(0xf5d58d), draws[2].Color);
        Assert.Equal(6, graphics.Gui.Invocations.Count(call =>
            call.Method.Name == nameof(IGuiAPI.LoadOrUpdateCairoTexture) && !(bool)call.Arguments[1]));
        var textures = new Dictionary<int, int>();
        var backgrounds = new Dictionary<int, int>();
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
            Assert.Equal(Math.Round(button.Bounds.renderX + 30 * scale, MidpointRounding.AwayFromZero)
                - Math.Round(button.Bounds.renderX, MidpointRounding.AwayFromZero), (double)background[3]);
            Assert.Equal(Math.Round(36 * scale, MidpointRounding.AwayFromZero), (double)background[4]);
            int key = state.On ? 2 : state.Hover ? 1 : 0;
            if (backgrounds.TryGetValue(key, out int backgroundTexture))
                Assert.Equal(backgroundTexture, (int)background[0]);
            else backgrounds.Add(key, (int)background[0]);
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
            if (textures.TryGetValue(key, out int texture)) Assert.Equal(texture, (int)overlay[0]);
            else textures.Add(key, (int)overlay[0]);
            Assert.DoesNotContain(graphics.Render.Invocations, call => call.Method.Name == "RenderRectangle");
        }
        Assert.Equal(3, textures.Values.Distinct().Count());
        Assert.Equal(3, backgrounds.Values.Distinct().Count());
        Assert.Equal(6, graphics.Gui.Invocations.Count(call =>
            call.Method.Name == nameof(IGuiAPI.LoadOrUpdateCairoTexture)));
        button.Dispose();
        Assert.Empty(graphics.Textures);

        static double[] Color(int rgb) =>
            [((rgb >> 16) & 255) / 255d, ((rgb >> 8) & 255) / 255d, (rgb & 255) / 255d, 1];
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    [InlineData(1.1f)]
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
        var hovered = uploads[1];
        var selected = uploads[2];
        Assert.Equal(6, uploads.Count);
        Assert.Equal((released.Width, released.Height), (selected.Width, selected.Height));
        int middle = selected.Width / 2;
        Assert.Equal(0, Pixel(released, middle, 0)[3]);
        Assert.Equal(255, Pixel(selected, middle, 0)[3]);
        Assert.Equal(0, Pixel(selected, 0, 0)[3]);
        int inactiveTop = selected.Height - (int)Math.Round(32 * scale, MidpointRounding.AwayFromZero);
        int stroke = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
        int accent = (int)Math.Round(2 * scale, MidpointRounding.AwayFromZero);
        Assert.Equal(255, Pixel(released, middle, inactiveTop)[3]);
        Assert.Equal(0, Pixel(released, middle, inactiveTop - 1)[3]);
        Assert.Equal(0x382f23, Rgb(released, middle, released.Height / 2));
        Assert.Equal(0x51422e, Rgb(hovered, middle, hovered.Height / 2));
        Assert.Equal(0x51432f, Rgb(selected, middle, selected.Height / 2));
        for (int i = 0; i < stroke; i++)
        {
            Assert.Equal(0x766349, Rgb(released, middle, inactiveTop + i));
            Assert.Equal(0x766349, Rgb(hovered, middle, inactiveTop + i));
            Assert.Equal(0xaa8c54, Rgb(selected, middle, i));
            Assert.Equal(0x766349, Rgb(released, i, released.Height / 2));
            Assert.Equal(0xaa8c54, Rgb(selected, i, selected.Height / 2));
        }
        for (int i = 0; i < accent; i++)
            Assert.Equal(0xd3b46f, Rgb(selected, middle, stroke + i));
        Assert.Equal(0x51432f, Rgb(selected, middle, stroke + accent));

        var panelBounds = ElementBounds.Fixed(0, 0, 80, 80).WithEmptyParent();
        panelBounds.CalcWorldBounds();
        GuiElementIconTab.DrawPanel(ctx, surface, panelBounds);
        surface.Flush();
        var panel = (surface.Width, surface.Height, surface.Stride, surface.Data.ToArray());
        for (int i = 0; i < stroke; i++)
        {
            Assert.Equal(0x887453, Rgb(panel, 10, i));
            Assert.Equal(Pixel(panel, 10, 10), Pixel(selected, middle, selected.Height - 1 - i));
            Assert.Equal(Pixel(panel, 10, i), Pixel(released, middle, released.Height - 1 - i));
            Assert.Equal(Pixel(panel, 10, i), Pixel(hovered, middle, hovered.Height - 1 - i));
        }
        Assert.NotEqual(Pixel(released, middle, released.Height - 1), Pixel(selected, middle, selected.Height - 1));

        static byte[] Pixel((int Width, int Height, int Stride, byte[] Pixels) image, int x, int y) =>
            image.Pixels.AsSpan(y * image.Stride + x * 4, 4).ToArray();
        static int Rgb((int Width, int Height, int Stride, byte[] Pixels) image, int x, int y)
        {
            byte[] pixel = Pixel(image, x, y);
            return pixel[2] << 16 | pixel[1] << 8 | pixel[0];
        }
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
        int stroke = Math.Max(1, (int)Math.Round(scale, MidpointRounding.AwayFromZero));
        var panel = ElementBounds.Fixed(120, 65 + 36 - stroke / GuiElement.scaled(1), 180, 50).WithParent(host);
        host.WithChild(panel);
        int textureCount = graphics.Textures.Count;
        foreach (double position in new[] { .1, .3, .7 })
        {
            host.fixedX = 100 + position;
            host.fixedY = 80 + position;
            host.MarkDirtyRecursive();
            host.CalcWorldBounds();
            Compose(tabs);
            Assert.Equal(textureCount, graphics.Textures.Count);
            tabs.SetValue(8, false);
            graphics.Render.Invocations.Clear();
            tabs.RenderInteractiveElements(0);
            var backgrounds = graphics.Render.Invocations.Where(call =>
                call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
                && call.Arguments.Count == 7 && (float)call.Arguments[5] == 50).ToArray();
            Assert.Equal(9, backgrounds.Length);
            var selected = backgrounds[8].Arguments;
            Assert.Equal((int)host.renderY + Math.Round(panel.drawY, MidpointRounding.AwayFromZero),
                (double)selected[2] + (double)selected[4] - stroke);
            Assert.True((double)selected[2] + (double)selected[4] <= NativeScissorRectangle(graphics).Bottom);
            var selectedBounds = tabs.Composer!.GetToggleButton("tab-8").Bounds;
            Assert.Equal((int)host.renderX + Math.Round(selectedBounds.renderX - host.renderX, MidpointRounding.AwayFromZero),
                (double)selected[1]);
        }
    }

    [Theory]
    [InlineData(.6f, false)]
    [InlineData(.75f, false)]
    [InlineData(1f, false)]
    [InlineData(1.1f, false)]
    [InlineData(1.25f, false)]
    [InlineData(1.5f, false)]
    [InlineData(1.75f, false)]
    [InlineData(2f, false)]
    [InlineData(.6f, true)]
    [InlineData(.75f, true)]
    [InlineData(1f, true)]
    [InlineData(1.1f, true)]
    [InlineData(1.25f, true)]
    [InlineData(1.5f, true)]
    [InlineData(1.75f, true)]
    [InlineData(2f, true)]
    public void NativeScissorRetainsEverySelectedBorderAcrossFractionalMovementAndScrolling(float scale, bool overflow)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        double callerWidth = overflow ? 180 : 400;
        using var tabs = Create(graphics, Enumerable.Range(0, 9).Select(i => "Entry " + i).ToArray(),
            callerWidth, _ => { });
        var host = ElementBounds.Fixed(100.75, 80.75, 1000, 1000).WithEmptyParent();
        host.IsDrawingSurface = true;
        tabs.Bounds.WithParent(host);
        host.WithChild(tabs.Bounds);
        host.CalcWorldBounds();
        Compose(tabs);
        foreach (double offset in new[] { .1, .3, .5, .7, .75, .9 })
        {
            host.fixedX = 100 + offset;
            host.fixedY = 80 + offset;
            host.MarkDirtyRecursive();
            host.CalcWorldBounds();
            foreach (int selected in new[] { 0, 4, 8 })
            {
                tabs.SetValue(selected, false);
                graphics.Render.Invocations.Clear();
                tabs.RenderInteractiveElements(0);
                var (left, top, right, bottom) = NativeScissorRectangle(graphics);
                var callerBounds = tabs.Bounds;
                var viewport = tabs.Viewport!;
                double reservation = overflow ? 20 * scale : 0;
                double expectedLeft = callerBounds.absX + reservation;
                double expectedWidth = callerBounds.InnerWidth - 2 * reservation;
                double expectedHeight = callerBounds.InnerHeight;
                Assert.Equal(expectedLeft, viewport.absX, 4);
                Assert.Equal(callerBounds.absY, viewport.absY, 4);
                Assert.Equal(expectedWidth, viewport.InnerWidth, 4);
                Assert.Equal(expectedHeight, viewport.InnerHeight, 4);
                Assert.InRange((double)(right - left), Math.Floor(expectedWidth), Math.Ceiling(expectedWidth));
                Assert.Equal(Math.Round(expectedHeight, MidpointRounding.AwayFromZero), bottom - top);
                var background = graphics.Render.Invocations.Where(call =>
                    call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
                    && call.Arguments.Count == 7 && (float)call.Arguments[5] == 50).ElementAt(selected).Arguments;
                Assert.True((double)background[1] >= left, $"Left border clipped at scale {scale}, offset {offset}.");
                Assert.True((double)background[2] >= top, $"Top border clipped at scale {scale}, offset {offset}.");
                Assert.True((double)background[1] + (double)background[3] <= right,
                    $"Right border clipped at scale {scale}, offset {offset}.");
                Assert.True((double)background[2] + (double)background[4] <= bottom,
                    $"Bottom border clipped at scale {scale}, offset {offset}.");
                Assert.Empty(graphics.Clips);
            }
        }
    }

    [Theory]
    [InlineData(.6f)]
    [InlineData(.75f)]
    [InlineData(1f)]
    [InlineData(1.1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void ClippedTabsRejectPointerActivationAndKeyboardWrapRestoresAccessAfterMovement(float scale)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        var changes = new List<int>();
        using var tabs = Create(graphics, Enumerable.Range(0, 9).Select(i => "Entry " + i).ToArray(), 180, changes.Add);
        var host = ElementBounds.Fixed(100.75, 80.75, 1000, 1000).WithEmptyParent();
        host.IsDrawingSurface = true;
        tabs.Bounds.WithParent(host);
        host.WithChild(tabs.Bounds);
        host.CalcWorldBounds();
        Compose(tabs);
        double expectedLeft = 0;
        double expectedRight = 0;
        foreach (double offset in new[] { .1, .75 })
        {
            host.fixedX = 100 + offset;
            host.fixedY = 80 + offset;
            host.MarkDirtyRecursive();
            host.CalcWorldBounds();
            tabs.SetValue(0, false);
            changes.Clear();
            double reservation = 20 * scale;
            var callerBounds = tabs.Bounds;
            expectedLeft = callerBounds.absX + reservation;
            expectedRight = callerBounds.absX + callerBounds.InnerWidth - reservation;
            var viewport = tabs.Viewport!;
            Assert.Equal(expectedLeft, viewport.absX, 4);
            Assert.Equal(callerBounds.absY, viewport.absY, 4);
            Assert.Equal(expectedRight - expectedLeft, viewport.InnerWidth, 4);
            Assert.Equal(callerBounds.InnerHeight, viewport.InnerHeight, 4);
            Assert.NotEqual(Math.Round(expectedRight), expectedRight);
            AssertClippedClickRejected(left: false);
            SetMouse(graphics, (expectedLeft + expectedRight) / 2,
                callerBounds.absY + callerBounds.InnerHeight / 2);
            var wheel = new MouseWheelEventArgs { deltaPrecise = 1000 };
            tabs.OnMouseWheel(graphics.Api.Object, wheel);
            Assert.True(wheel.IsHandled);
            AssertClippedClickRejected(left: true);

            var last = tabs.Composer!.GetToggleButton("tab-8").Bounds;
            Click((int)(last.absX + last.OuterWidth / 2), (int)(last.absY + last.OuterHeight / 2));
            Assert.Equal(8, tabs.activeElement);
            Assert.Equal(8, Assert.Single(changes));
            tabs.OnFocusGained();
            Navigate(GlKeys.Right, 0);
            Navigate(GlKeys.Left, 8);
            Assert.Equal(new[] { 8, 0, 8 }, changes);
            tabs.OnFocusLost();
            var unfocused = new KeyEvent { KeyCode = (int)GlKeys.Right };
            tabs.OnKeyDown(graphics.Api.Object, unfocused);
            Assert.False(unfocused.Handled);
            Assert.Equal(8, tabs.activeElement);
            Assert.Equal(new[] { 8, 0, 8 }, changes);
        }

        void AssertClippedClickRejected(bool left)
        {
            var clipped = tabs.Composer!.GetToggleButton("tab-4").Bounds;
            int x = left ? (int)Math.Ceiling(expectedLeft) - 1
                : (int)Math.Floor(expectedRight) + 1;
            int y = (int)(clipped.absY + clipped.OuterHeight / 2);
            Assert.True(clipped.PointInside(x, y));
            Assert.True(left ? x < expectedLeft : x >= expectedRight);
            Assert.False(tabs.Viewport!.PointInside(x, y));
            Click(x, y);
            Assert.Empty(changes);
            Assert.Equal(0, tabs.activeElement);
        }

        void Click(int x, int y)
        {
            SetMouse(graphics, x, y);
            tabs.OnMouseDownOnElement(graphics.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
            tabs.OnMouseUp(graphics.Api.Object, new MouseEvent(x, y, EnumMouseButton.Left, 0));
        }

        void Navigate(GlKeys key, int expected)
        {
            var args = new KeyEvent { KeyCode = (int)key };
            tabs.OnKeyDown(graphics.Api.Object, args);
            Assert.True(args.Handled);
            Assert.Equal(expected, tabs.activeElement);
            var selected = tabs.Composer!.GetToggleButton("tab-" + expected).Bounds;
            var viewport = tabs.Viewport!;
            Assert.True(selected.absX >= viewport.absX - .001);
            Assert.True(selected.absX + selected.OuterWidth <= viewport.absX + viewport.InnerWidth + .001);
        }
    }

    [Theory]
    [InlineData(1f, 9)]
    [InlineData(1.25f, 1)]
    [InlineData(1.25f, 10)]
    [InlineData(1.5f, 18)]
    [InlineData(1.75f, 9)]
    [InlineData(2f, 10)]
    public void MeasuredWidthShowsEveryTabWithoutScrollingOrArrows(float scale, int count)
    {
        using var graphics = new RecipeBookRenderFixture(scale);
        Setup(graphics);
        double width = GuiElementIconTabs.MeasureWidth(count);
        Assert.Equal(count * 30 + (count - 1) * 4, width);
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
        Assert.Equal(tabs.Bounds.absX, tabs.Composer.GetToggleButton("tab-0").Bounds.absX, 4);
        var last = tabs.Composer.GetToggleButton("tab-" + (count - 1)).Bounds;
        Assert.Equal(tabs.Bounds.absX + width * scale, last.absX + last.OuterWidth, 4);
        graphics.Render.Invocations.Clear();
        tabs.RenderInteractiveElements(0);
        var backgrounds = graphics.Render.Invocations.Where(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Count == 7 && (float)call.Arguments[5] == 50).ToArray();
        Assert.Equal(count, backgrounds.Length);
        var clip = NativeScissorRectangle(graphics);
        Assert.All(backgrounds, call =>
        {
            double x = (double)call.Arguments[1];
            double right = x + (double)call.Arguments[3];
            Assert.True(x >= clip.Left);
            Assert.True(right <= clip.Right);
        });
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
}
