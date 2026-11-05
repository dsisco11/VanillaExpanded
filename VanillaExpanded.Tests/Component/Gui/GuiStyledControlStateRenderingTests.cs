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
using static VanillaExpanded.Tests.TestSupport.Gui.GuiStyledControlsTestsTestSupport;

namespace VanillaExpanded.Tests.Component.Gui;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Trait(TestCapabilities.TraitName, TestCapabilities.Cairo)]
[Collection("Recipe book native GUI")]
public sealed class GuiStyledControlStateRenderingTests
{
    [Fact]
    public void PopupSelectionChangesPreserveSelectionAndReuseCachedResources()
    {
        // Arrange
        using var f = new ControlsFixture();
        using var dropdown = new GuiElementStyledDropDown(f.Api, ["a"], ["Alpha"], 0,
            (_, _) => { }, ElementBounds.Fixed(10, 10, 180, 30));
        using var composer = f.Compose(dropdown);
        dropdown.listMenu.Open();
        f.Render(dropdown);
        Assert.Equal("a", dropdown.SelectedValue);

        // Act
        dropdown.SetSelectedIndex(-1);
        Assert.Empty(dropdown.listMenu.SelectedIndices);
        f.Render(dropdown);
        Assert.Empty(dropdown.listMenu.SelectedIndices);
        dropdown.SetSelectedIndex(0);
        Assert.Equal(new[] { 0 }, dropdown.listMenu.SelectedIndices);
        Assert.Equal("a", dropdown.SelectedValue);
        f.Render(dropdown);
        Assert.Equal(new[] { 0 }, dropdown.listMenu.SelectedIndices);

        // Assert
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
    public void ScrollbarPointerCaptureCancelsWithoutExtraScrollAndReusesResources(float scale)
    {
        // Arrange
        using var f = new ControlsFixture(scale);
        var changes = new List<float>();
        using var scrollbar = new GuiElementStyledScrollbar(f.Api, changes.Add,
            ElementBounds.Fixed(10.37, 12.81, 12, 180), arrowStep: 23);
        using var composer = f.Compose(scrollbar);
        float visible = (float)scrollbar.Bounds.InnerHeight;
        float total = 1000 * scale;
        scrollbar.SetHeights(visible, total);
        float maximum = total - visible;
        int textureCount = f.Graphics.Textures.Count;

        // Act: capture each arrow and release outside its hit target.
        foreach (int index in new[] { 1, 2 })
        {
            scrollbar.CurrentYPosition = 200 * scale;
            f.SetMouse(-100, -100);
            f.Render(scrollbar);
            var mouse = PointAt(index);
            changes.Clear();
            composer.OnMouseDown(mouse);
            Assert.True(mouse.Handled);
            Assert.Equal((index == 1 ? 177 : 223) * scale, Assert.Single(changes), 3);
            f.SetMouse(-100, -100);
            composer.OnMouseMove(f.Mouse(-100, -100));
            composer.OnMouseUp(f.Mouse(-100, -100));
            Assert.Equal(index == 1 ? 177 * scale : 223 * scale, scrollbar.CurrentYPosition, 3);
            Assert.Single(changes);
        }

        // Act: drag the thumb outside the track and release it.
        f.SetMouse(-100, -100);
        f.Render(scrollbar);
        var thumbMouse = PointAt(3);
        changes.Clear();
        composer.OnMouseDown(thumbMouse);
        Assert.Empty(changes);
        f.SetMouse(-100, -100);
        composer.OnMouseMove(f.Mouse(thumbMouse.X, thumbMouse.Y + 5 * scale));
        Assert.Single(changes);
        composer.OnMouseUp(f.Mouse(-100, -100));
        float releasedPosition = scrollbar.CurrentYPosition;
        composer.OnMouseMove(f.Mouse(-100, -100));
        Assert.Equal(releasedPosition, scrollbar.CurrentYPosition);
        Assert.Single(changes);

        // Act: focus loss cancels a later drag without allowing further movement.
        f.Render(scrollbar);
        thumbMouse = PointAt(3);
        float positionAtFocusLoss = scrollbar.CurrentYPosition;
        changes.Clear();
        composer.OnMouseDown(thumbMouse);
        scrollbar.OnFocusLost();
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
        composer.OnMouseMove(f.Mouse(thumbMouse.X, thumbMouse.Y + 10 * scale));
        Assert.Equal(positionAtFocusLoss, scrollbar.CurrentYPosition);
        Assert.Empty(changes);

        // Act: disabled arrows and a collapsed range cannot change the scroll position.
        f.Render(scrollbar);
        var downMouse = PointAt(2);
        float beforeDisabledClick = scrollbar.CurrentYPosition;
        composer.OnMouseDown(downMouse);
        float afterEnabledClick = scrollbar.CurrentYPosition;
        Assert.NotEqual(beforeDisabledClick, afterEnabledClick);
        composer.OnMouseUp(f.Mouse(downMouse.X, downMouse.Y));
        scrollbar.Enabled = false;
        int callbacksWhileEnabled = changes.Count;
        composer.OnMouseDown(f.Mouse(downMouse.X, downMouse.Y));
        Assert.Equal(callbacksWhileEnabled, changes.Count);
        composer.OnMouseUp(f.Mouse(downMouse.X, downMouse.Y));
        scrollbar.Enabled = true;

        scrollbar.CurrentYPosition = maximum - 11 * scale;
        f.Render(scrollbar);
        downMouse = PointAt(2);
        changes.Clear();
        composer.OnMouseDown(downMouse);
        Assert.Equal(maximum, Assert.Single(changes), 3);
        composer.OnMouseUp(downMouse);

        scrollbar.CurrentYPosition = maximum / 2;
        f.Render(scrollbar);
        thumbMouse = PointAt(3);
        composer.OnMouseDown(thumbMouse);
        scrollbar.SetHeights(visible, visible);
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
        Assert.Equal(0, scrollbar.CurrentYPosition);
        var wheel = new MouseWheelEventArgs { deltaPrecise = -1 };
        scrollbar.OnMouseWheel(f.Api, wheel);
        Assert.False(wheel.IsHandled);
        Assert.Equal(0, scrollbar.CurrentYPosition);
        scrollbar.SetHeights(visible, 1000 * scale);
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
        f.SetMouse(-100, -100);
        int uploads = f.Uploads;
        f.Render(scrollbar);
        Assert.Equal(uploads, f.Uploads);

        // Assert reuse and disposal preserve texture ownership.
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
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
