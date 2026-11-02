using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.Gui.GuiStyledControlsTestsTestSupport;

namespace VanillaExpanded.Tests.Component.Gui;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class GuiStyledControlStateRenderingTests
{
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
    [InlineData(.75f)]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void ScrollbarPointerFeedbackTracksCaptureAndCancelsWithoutExtraScrollOrUploads(float scale)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<float>();
        using var scrollbar = new GuiElementStyledScrollbar(f.Api, changes.Add,
            ElementBounds.Fixed(10.37, 12.81, 12, 180), arrowStep: 23);
        using var composer = f.Compose(scrollbar);
        float visible = (float)scrollbar.Bounds.InnerHeight;
        scrollbar.SetHeights(visible, 1000 * scale);
        int uploads = f.Uploads;
        foreach (int index in new[] { 1, 2 })
        {
            scrollbar.CurrentYPosition = 200 * scale;
            f.SetMouse(-100, -100);
            f.Render(scrollbar);
            int idle = (int)f.Draws[index].Arguments[0];
            var mouse = PointAt(index);
            f.Render(scrollbar);
            int hover = (int)f.Draws[index].Arguments[0];
            Assert.NotEqual(idle, hover);
            Assert.True(f.Drawn(index).HasColor(0xc8b897));
            changes.Clear();
            composer.OnMouseDown(mouse);
            Assert.True(mouse.Handled);
            Assert.Equal((index == 1 ? 177 : 223) * scale, Assert.Single(changes), 3);
            f.Render(scrollbar);
            int pressed = (int)f.Draws[index].Arguments[0];
            Assert.NotEqual(hover, pressed);
            Assert.NotEqual(idle, pressed);
            Assert.True(f.Drawn(index).HasColor(0xf5d58d));
            f.SetMouse(-100, -100);
            composer.OnMouseMove(f.Mouse(-100, -100));
            f.Render(scrollbar);
            Assert.Equal(idle, f.Draws[index].Arguments[0]);
            f.SetMouse(mouse.X, mouse.Y);
            f.Render(scrollbar);
            Assert.Equal(pressed, f.Draws[index].Arguments[0]);
            f.SetMouse(-100, -100);
            composer.OnMouseUp(f.Mouse(-100, -100));
            f.SetMouse(mouse.X, mouse.Y);
            f.Render(scrollbar);
            Assert.Equal(hover, f.Draws[index].Arguments[0]);
            Assert.Single(changes);
        }

        f.SetMouse(-100, -100);
        f.Render(scrollbar);
        int idleThumb = (int)f.Draws[3].Arguments[0];
        var thumbMouse = PointAt(3);
        f.Render(scrollbar);
        int hoverThumb = (int)f.Draws[3].Arguments[0];
        Assert.NotEqual(idleThumb, hoverThumb);
        Assert.True(f.Drawn(3).HasColor(0xc8b897));
        changes.Clear();
        composer.OnMouseDown(thumbMouse);
        Assert.Empty(changes);
        f.Render(scrollbar);
        int pressedThumb = (int)f.Draws[3].Arguments[0];
        Assert.NotEqual(hoverThumb, pressedThumb);
        Assert.True(f.Drawn(3).HasColor(0xf5d58d));
        f.SetMouse(-100, -100);
        composer.OnMouseMove(f.Mouse(thumbMouse.X, thumbMouse.Y + 5 * scale));
        Assert.Single(changes);
        f.Render(scrollbar);
        Assert.Equal(pressedThumb, f.Draws[3].Arguments[0]);
        composer.OnMouseUp(f.Mouse(-100, -100));
        float releasedPosition = scrollbar.CurrentYPosition;
        composer.OnMouseMove(f.Mouse(-100, -100));
        f.Render(scrollbar);
        Assert.Equal(releasedPosition, scrollbar.CurrentYPosition);
        Assert.Equal(idleThumb, f.Draws[3].Arguments[0]);
        Assert.Single(changes);

        thumbMouse = PointAt(3);
        composer.OnMouseDown(thumbMouse);
        scrollbar.OnFocusLost();
        f.Render(scrollbar);
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
        Assert.Equal(hoverThumb, f.Draws[3].Arguments[0]);
        var downMouse = PointAt(2);
        composer.OnMouseDown(downMouse);
        f.Render(scrollbar);
        Assert.True(f.Drawn(2).HasColor(0xf5d58d));
        scrollbar.Enabled = false;
        f.Render(scrollbar);
        Assert.Equal(3, f.Draws.Length);
        Assert.InRange(f.Drawn(2).MaxAlpha, 1, 90);
        scrollbar.Enabled = true;
        f.Render(scrollbar);
        Assert.True(f.Drawn(2).HasColor(0xc8b897));
        Assert.Equal(uploads, f.Uploads);
        scrollbar.CurrentYPosition = (1000 - 180 - 11) * scale;
        changes.Clear();
        composer.OnMouseDown(downMouse);
        f.Render(scrollbar);
        Assert.Equal((1000 - 180) * scale, Assert.Single(changes), 3);
        Assert.InRange(f.Drawn(2).MaxAlpha, 1, 90);
        composer.OnMouseUp(downMouse);

        scrollbar.CurrentYPosition = 200 * scale;
        f.Render(scrollbar);
        thumbMouse = PointAt(3);
        composer.OnMouseDown(thumbMouse);
        scrollbar.SetHeights(visible, visible);
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
        scrollbar.SetHeights(visible, 1000 * scale);
        f.SetMouse(-100, -100);
        f.Render(scrollbar);
        Assert.Equal(idleThumb, f.Draws[3].Arguments[0]);
        composer.Dispose();
        scrollbar.Dispose();
        Assert.Empty(f.Graphics.Textures);

        MouseEvent PointAt(int index)
        {
            var draw = f.Draws[index];
            var mouse = f.Mouse(Convert.ToDouble(draw.Arguments[1]) + Convert.ToDouble(draw.Arguments[3]) / 2,
                Convert.ToDouble(draw.Arguments[2]) + Convert.ToDouble(draw.Arguments[4]) / 2);
            f.SetMouse(mouse.X, mouse.Y);
            return mouse;
        }
    }
}
