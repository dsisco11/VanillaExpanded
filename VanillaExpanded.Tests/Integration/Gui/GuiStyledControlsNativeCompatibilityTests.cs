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

namespace VanillaExpanded.Tests.Integration.Gui;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class GuiStyledControlsNativeCompatibilityTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void ButtonPreservesNativeActivationAndCancelsDisabledOrHiddenInput(float scale)
    {
        using var f = new ControlsFixture(scale);
        int clicks = 0;
        using var button = new GuiElementStyledButton(f.Api, "A caller-provided button", () => { clicks++; return true; },
            ElementBounds.Fixed(10, 10, 180, 28).WithEmptyParent());
        using var composer = f.Compose(button);
        f.Render(button);
        int uploads = f.Uploads;
        int textureCount = f.Graphics.Textures.Count;
        f.Render(button);
        Assert.Equal(uploads, f.Uploads);
        f.SetMouse(button.Bounds.absX + 10, button.Bounds.absY + 10);
        f.Render(button);
        var down = f.Mouse(button.Bounds.absX + 10, button.Bounds.absY + 10);
        button.OnMouseDownOnElement(f.Api, down);
        f.Render(button);
        Assert.Equal(0, clicks);
        var up = f.Mouse(down.X, down.Y);
        button.OnMouseUpOnElement(f.Api, up);
        f.Render(button);
        Assert.True(up.Handled);
        Assert.Equal(1, clicks);

        // A release outside the control cancels the pending activation.
        button.OnMouseDownOnElement(f.Api, f.Mouse(down.X, down.Y));
        button.OnMouseUp(f.Api, f.Mouse(-100, -100));
        f.SetMouse(-100, -100);
        f.Render(button);
        Assert.Equal(1, clicks);

        button.Enabled = false;
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
        int disabledUploads = f.Uploads;
        f.Render(button);
        Assert.Equal(disabledUploads, f.Uploads);
        button.OnMouseDownOnElement(f.Api, f.Mouse(down.X, down.Y));
        button.OnMouseUpOnElement(f.Api, f.Mouse(down.X, down.Y));
        Assert.Equal(1, clicks);
        button.OnFocusGained();
        button.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        Assert.Equal(1, clicks);
        button.Enabled = true;
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
        int enabledUploads = f.Uploads;
        f.Render(button);
        Assert.Equal(enabledUploads, f.Uploads);
        button.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Enter });
        f.Render(button);
        Assert.Equal(2, clicks);
        button.Visible = false;
        f.Render(button);
        Assert.Empty(f.Draws);
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
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
        int editX = (int)(input.Bounds.absX + input.Bounds.OuterWidth / 2);
        int editY = (int)(input.Bounds.absY + input.Bounds.OuterHeight / 2);
        int clearX = (int)(input.ClearBounds.absX + input.ClearBounds.OuterWidth / 2);
        int clearY = (int)(input.ClearBounds.absY + input.ClearBounds.OuterHeight / 2);
        Assert.True(input.Bounds.PointInside(editX, editY));
        Assert.False(input.ClearBounds.PointInside(editX, editY));
        Assert.True(input.ClearBounds.PointInside(clearX, clearY));
        Assert.False(input.Bounds.PointInside(clearX, clearY));
        Assert.Equal("Caller clear tooltip", input.ClearHint.GetText());
        input.SetValue("two caller words");
        input.OnFocusGained();
        input.SetCaretPos(3);
        Assert.Equal(3, input.CaretPosInLine);
        int selectX = (int)(input.Bounds.absX + input.Bounds.OuterWidth / 4);
        int selectY = (int)(input.Bounds.absY + input.Bounds.OuterHeight / 2);
        input.OnMouseDownOnElement(f.Api, f.Mouse(selectX, selectY));
        int selectionEndX = selectX + (int)(input.Bounds.OuterWidth / 4);
        input.OnMouseMove(f.Api, f.Mouse(selectionEndX, selectY));
        Assert.True(input.HasFocus);
        input.OnMouseUp(f.Api, f.Mouse(selectionEndX, selectY));
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
    public void DropdownOpensReplacesAndSelectsWhileReusingOwnedResources(float scale)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<string>();
        using var dropdown = new GuiElementStyledDropDown(f.Api, ["a", "b"], ["Alpha", "Beta"], 0,
            (value, _) => changes.Add(value), ElementBounds.Fixed(10, 10, 180, 20));
        using var composer = f.Compose(dropdown);
        Assert.IsType<GuiElementStyledListMenu>(dropdown.listMenu);
        f.Render(dropdown);
        dropdown.listMenu.Open();
        Assert.True(dropdown.listMenu.IsOpened);
        f.Render(dropdown);
        int uploads = f.Uploads;
        f.Render(dropdown);
        Assert.Equal(uploads, f.Uploads);
        double x = dropdown.listMenu.Bounds.renderX + 10 * scale;
        double y = dropdown.listMenu.Bounds.renderY + dropdown.listMenu.Bounds.InnerHeight + 45 * scale;
        f.SetMouse(x, y);
        dropdown.OnMouseMove(f.Api, f.Mouse(x, y));
        f.Render(dropdown);
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
        dropdown.SetList(["b", "c", "d"], ["Beta changed", "Gamma", "Delta"]);
        dropdown.SetSelectedIndex(0);
        Assert.Equal("b", dropdown.SelectedValue);
        dropdown.listMenu.Open();
        Assert.True(dropdown.listMenu.IsOpened);
        f.Render(dropdown);
        dropdown.Bounds.fixedWidth += 30;
        dropdown.Bounds.MarkDirtyRecursive();
        dropdown.Bounds.CalcWorldBounds();
        dropdown.listMenu.Open();
        f.Render(dropdown);
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
        Assert.Empty(f.Graphics.Clips);
        composer.Dispose();
        dropdown.Dispose();
        Assert.Empty(f.Graphics.Textures);
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
    public void SharedControlsKeepNativeComposerTabRouting()
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
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void CheckboxTogglesNativelyAndIgnoresDisabledInput(float scale)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<bool>();
        using var checkbox = new GuiElementStyledCheckbox(f.Api, changes.Add, ElementBounds.Fixed(10, 10, 40, 40));
        using var composer = f.Compose(checkbox);
        checkbox.OnMouseDownOnElement(f.Api, f.Mouse(checkbox.Bounds.absX + checkbox.Bounds.OuterWidth / 2,
            checkbox.Bounds.absY + checkbox.Bounds.OuterHeight / 2));
        Assert.True(checkbox.On);
        Assert.True(Assert.Single(changes));
        checkbox.Enabled = false;
        checkbox.OnFocusGained();
        checkbox.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Space });
        Assert.Single(changes);
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
            .AddDropDown(["a"], ["Alpha"], 0, (_, _) => { }, ElementBounds.Fixed(0, 120, 100, 30), "dropdown")
            .AddVerticalScrollbar(_ => { }, ElementBounds.Fixed(0, 160, 12, 100), "scrollbar");
        Assert.IsType<GuiElementTextButton>(composer.GetButton("button"));
        Assert.IsType<GuiElementSwitch>(composer.GetSwitch("checkbox"));
        Assert.IsType<GuiElementTextInput>(composer.GetTextInput("input"));
        Assert.IsType<GuiElementDropDown>(composer.GetDropDown("dropdown"));
        Assert.IsType<GuiElementScrollbar>(composer.GetScrollbar("scrollbar"));
        Assert.Throws<ArgumentException>(() => new GuiElementStyledDropDown(f.Api, [], [], 0,
            (_, _) => { }, ElementBounds.Fixed(0, 0, 100, 30)));
    }

    [Theory]
    [InlineData(.75f)]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    [InlineData(2f)]
    public void CheckboxLabelUsesOneNativeToggleAndFocusTargetAndRespectsDisabledAndClippedInput(float scale)
    {
        using var f = new ControlsFixture(scale);
        using var composer = f.NewComposer();
        var changes = new List<bool>();
        var label = ElementBounds.Fixed(35, 10, 140, 17);
        using var checkbox = new GuiElementStyledCheckbox(f.Api, changes.Add,
            ElementBounds.Fixed(10, 10, 17, 17), labelBounds: label);
        composer.AddInteractiveElement(checkbox, "checkbox")
            .AddStaticText("Caller checkbox label", CairoFont.WhiteSmallText(), label, "label")
            .Compose(false);
        int x = (int)(label.absX + label.OuterWidth / 2);
        int y = (int)(label.absY + label.OuterHeight / 2);
        Assert.False(checkbox.Bounds.PointInside(x, y));
        Click(x, y);
        Assert.True(checkbox.HasFocus);
        Assert.True(checkbox.On);
        Assert.True(Assert.Single(changes));
        Click(x, y);
        Assert.False(checkbox.On);
        Assert.Equal(new[] { true, false }, changes);
        Click((int)(label.absX + label.OuterWidth + 5), y);
        Assert.Equal(2, changes.Count);
        checkbox.Enabled = false;
        Click(x, y);
        Assert.False(checkbox.On);
        Assert.Equal(2, changes.Count);
        checkbox.Enabled = true;
        checkbox.InsideClipBounds = checkbox.Bounds;
        Click(x, y);
        Assert.False(checkbox.On);
        Assert.Equal(2, changes.Count);
        checkbox.InsideClipBounds = null;
        Assert.True(composer.FocusElement(checkbox.TabIndex));
        var space = new KeyEvent { KeyCode = (int)GlKeys.Space };
        composer.OnKeyDown(space, true);
        Assert.True(space.Handled);
        Assert.True(checkbox.On);
        Assert.Equal(new[] { true, false, true }, changes);

        void Click(int mouseX, int mouseY)
        {
            composer.OnMouseDown(f.Mouse(mouseX, mouseY));
            composer.OnMouseUp(f.Mouse(mouseX, mouseY));
        }
    }

    [Theory]
    [InlineData(.75f, 47)]
    [InlineData(1f, 47)]
    [InlineData(1.25f, 47)]
    [InlineData(1.5f, 47)]
    [InlineData(1.75f, 47)]
    [InlineData(2f, 47)]
    [InlineData(.75f, 180)]
    [InlineData(1f, 180)]
    [InlineData(1.25f, 180)]
    [InlineData(1.5f, 180)]
    [InlineData(1.75f, 180)]
    [InlineData(2f, 180)]
    public void ScrollbarArrowsAndThumbShareNativeRangeAndReleaseDragOutside(float scale, double height)
    {
        using var f = new ControlsFixture(scale);
        var changes = new List<float>();
        using var scrollbar = new GuiElementStyledScrollbar(f.Api, changes.Add,
            ElementBounds.Fixed(10.37, 12.81, 12, height), arrowStep: 23);
        scrollbar.Enabled = false;
        using var composer = f.Compose(scrollbar);
        float visible = (float)scrollbar.Bounds.InnerHeight;
        float total = 1000 * scale;
        float maximum = total - visible;
        scrollbar.SetHeights(visible, total);
        scrollbar.Enabled = true;
        changes.Clear();
        f.Render(scrollbar);
        int uploads = f.Uploads;
        ClickArrow(2);
        Assert.Equal(23 * scale, scrollbar.CurrentYPosition, 3);
        Assert.Equal(23 * scale, Assert.Single(changes), 3);
        ClickArrow(1);
        Assert.Equal(0, scrollbar.CurrentYPosition);
        Assert.Equal(2, changes.Count);
        ClickArrow(1);
        Assert.Equal(2, changes.Count);
        Assert.Equal(uploads, f.Uploads);

        f.Render(scrollbar);
        int trackX = (int)(Convert.ToDouble(f.Draws[0].Arguments[1]) + Convert.ToDouble(f.Draws[0].Arguments[3]) / 2);
        double trackTop = Convert.ToDouble(f.Draws[1].Arguments[2]) + Convert.ToDouble(f.Draws[1].Arguments[4]);
        double trackBottom = Convert.ToDouble(f.Draws[2].Arguments[2]);
        double trackHeight = trackBottom - trackTop;
        double thumbHeight = Convert.ToDouble(f.Draws[3].Arguments[4]);
        double availableTravel = trackHeight - thumbHeight;
        int trackY = (int)(trackTop + .75 * trackHeight);
        double trackThumbPosition = Math.Clamp(trackY - trackTop - (int)(thumbHeight / 2),
            0, availableTravel);
        float expectedTrackPosition = (float)(maximum * trackThumbPosition / availableTravel);
        composer.OnMouseDown(f.Mouse(trackX, trackY));
        Assert.Equal(3, changes.Count);
        Assert.Equal(expectedTrackPosition, scrollbar.CurrentYPosition, 3);
        Assert.Equal(expectedTrackPosition, changes[^1], 3);
        composer.OnMouseUp(f.Mouse(trackX, trackY));
        scrollbar.CurrentYPosition = 0;
        scrollbar.Bounds.fixedX += 9.37;
        scrollbar.Bounds.fixedY += 6.81;
        scrollbar.Bounds.CalcWorldBounds();
        ClickArrow(2);
        Assert.Equal(23 * scale, scrollbar.CurrentYPosition, 3);
        scrollbar.CurrentYPosition = 0;
        f.Render(scrollbar);
        var thumb = f.Draws[3];
        int x = (int)(Convert.ToDouble(thumb.Arguments[1]) + Convert.ToDouble(thumb.Arguments[3]) / 2);
        int y = (int)(Convert.ToDouble(thumb.Arguments[2]) + Convert.ToDouble(thumb.Arguments[4]) / 2);
        trackTop = Convert.ToDouble(f.Draws[1].Arguments[2]) + Convert.ToDouble(f.Draws[1].Arguments[4]);
        trackBottom = Convert.ToDouble(f.Draws[2].Arguments[2]);
        thumbHeight = Convert.ToDouble(thumb.Arguments[4]);
        availableTravel = trackBottom - trackTop - thumbHeight;
        Assert.True(availableTravel > 4);
        int dragPixels = Math.Max(1, (int)Math.Floor(availableTravel / 4));
        float expectedDragPosition = (float)(maximum * dragPixels / availableTravel);
        changes.Clear();
        composer.OnMouseDown(f.Mouse(x, y));
        Assert.True(scrollbar.mouseDownOnScrollbarHandle);
        Assert.Empty(changes);
        composer.OnMouseMove(f.Mouse(x, y + dragPixels));
        Assert.Equal(expectedDragPosition, scrollbar.CurrentYPosition, 2);
        Assert.Equal(expectedDragPosition, Assert.Single(changes), 2);
        composer.OnMouseMove(f.Mouse(x, y + (int)availableTravel));
        Assert.Equal(maximum, scrollbar.CurrentYPosition, 3);
        Assert.Equal(maximum, changes[1], 3);
        f.Render(scrollbar);
        double thumbBottom = Convert.ToDouble(f.Draws[3].Arguments[2]) + Convert.ToDouble(f.Draws[3].Arguments[4]);
        Assert.Equal(Convert.ToDouble(f.Draws[2].Arguments[2]), thumbBottom);
        composer.OnMouseUp(f.Mouse(-100, -100));
        Assert.False(scrollbar.mouseDownOnScrollbarHandle);
        composer.OnMouseMove(f.Mouse(x, -100));
        Assert.Equal(maximum, scrollbar.CurrentYPosition, 3);

        float comparisonStart = maximum / 2;
        scrollbar.CurrentYPosition = comparisonStart;
        var nativeChanges = new List<float>();
        using (var nativeScrollbar = new GuiElementScrollbar(f.Api, nativeChanges.Add,
            ElementBounds.Fixed(50.37, 12.81, 12, height).WithEmptyParent()))
        {
            nativeScrollbar.Bounds.CalcWorldBounds();
            nativeScrollbar.SetHeights(visible, total);
            nativeScrollbar.CurrentYPosition = comparisonStart;
            Assert.True(composer.FocusElement(scrollbar.TabIndex));
            nativeScrollbar.OnFocusGained();
            changes.Clear();
            nativeChanges.Clear();
            composer.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Up }, true);
            nativeScrollbar.OnKeyDown(f.Api, new KeyEvent { KeyCode = (int)GlKeys.Up });
            Assert.NotEqual(comparisonStart, scrollbar.CurrentYPosition);
            Assert.Equal(nativeScrollbar.CurrentYPosition, scrollbar.CurrentYPosition, 3);
            Assert.Equal(Assert.Single(nativeChanges), Assert.Single(changes), 3);

            float beforeWheel = scrollbar.CurrentYPosition;
            changes.Clear();
            nativeChanges.Clear();
            var wheelEvent = new MouseWheelEventArgs { deltaPrecise = -1 };
            var nativeWheelEvent = new MouseWheelEventArgs { deltaPrecise = -1 };
            scrollbar.OnMouseWheel(f.Api, wheelEvent);
            nativeScrollbar.OnMouseWheel(f.Api, nativeWheelEvent);
            Assert.True(wheelEvent.IsHandled);
            Assert.True(nativeWheelEvent.IsHandled);
            Assert.NotEqual(beforeWheel, scrollbar.CurrentYPosition);
            Assert.Equal(nativeScrollbar.CurrentYPosition, scrollbar.CurrentYPosition, 3);
            Assert.Equal(Assert.Single(nativeChanges), Assert.Single(changes), 3);
        }

        scrollbar.Enabled = false;
        int callbacks = changes.Count;
        ClickArrow(1);
        Assert.Equal(callbacks, changes.Count);
        scrollbar.Enabled = true;
        scrollbar.SetHeights(visible, visible);
        callbacks = changes.Count;
        ClickArrow(2);
        var wheel = new MouseWheelEventArgs { deltaPrecise = -1 };
        scrollbar.OnMouseWheel(f.Api, wheel);
        Assert.False(wheel.IsHandled);
        Assert.Equal(callbacks, changes.Count);
        Assert.Equal(0, scrollbar.CurrentYPosition);
        composer.Dispose();
        scrollbar.Dispose();
        Assert.Empty(f.Graphics.Textures);

        void ClickArrow(int drawIndex)
        {
            f.Render(scrollbar);
            var arrow = f.Draws[drawIndex];
            var mouse = f.Mouse(Convert.ToDouble(arrow.Arguments[1]) + Convert.ToDouble(arrow.Arguments[3]) / 2,
                Convert.ToDouble(arrow.Arguments[2]) + Convert.ToDouble(arrow.Arguments[4]) / 2);
            composer.OnMouseDown(mouse);
            composer.OnMouseUp(mouse);
        }
    }

    [Theory]
    [InlineData(.75f, true)]
    [InlineData(1f, true)]
    [InlineData(1.25f, true)]
    [InlineData(1.5f, true)]
    [InlineData(1.75f, true)]
    [InlineData(2f, true)]
    [InlineData(.75f, false)]
    [InlineData(1f, false)]
    [InlineData(1.25f, false)]
    [InlineData(1.5f, false)]
    [InlineData(1.75f, false)]
    [InlineData(2f, false)]
    public void ArrowButtonsUseNativeMomentaryActivationWithoutExtraFocusOrReleaseCallbacks(float scale, bool pointsUp)
    {
        using var f = new ControlsFixture(scale);
        int callbacks = 0;
        using var button = new GuiElementStyledArrowButton(f.Api, pointsUp, () => callbacks++,
            ElementBounds.Fixed(10.37, 12.81, 12, 10));
        using var composer = f.Compose(button);
        f.Render(button);
        int uploads = f.Uploads;
        int textureCount = f.Graphics.Textures.Count;
        f.Render(button);
        Assert.Equal(uploads, f.Uploads);
        Assert.False(button.Focusable);
        var mouse = f.Mouse(button.Bounds.absX + button.Bounds.OuterWidth / 2,
            button.Bounds.absY + button.Bounds.OuterHeight / 2);
        f.SetMouse(mouse.X, mouse.Y);
        composer.OnMouseDown(mouse);
        f.Render(button);
        Assert.True(mouse.Handled);
        Assert.True(button.On);
        Assert.Equal(1, callbacks);
        var repeatedDown = f.Mouse(mouse.X, mouse.Y);
        composer.OnMouseDown(repeatedDown);
        Assert.True(repeatedDown.Handled);
        Assert.True(button.On);
        Assert.Equal(1, callbacks);
        f.SetMouse(-100, -100);
        composer.OnMouseMove(f.Mouse(-100, -100));
        f.Render(button);
        Assert.True(button.On);
        composer.OnMouseUp(f.Mouse(-100, -100));
        f.Render(button);
        Assert.False(button.On);
        Assert.Equal(1, callbacks);

        composer.OnMouseDown(f.Mouse(mouse.X, mouse.Y));
        button.OnFocusLost();
        f.Render(button);
        Assert.False(button.On);
        composer.OnMouseUp(f.Mouse(mouse.X, mouse.Y));
        Assert.Equal(2, callbacks);
        composer.OnMouseDown(f.Mouse(mouse.X, mouse.Y));
        button.Enabled = false;
        f.Render(button);
        Assert.False(button.On);
        composer.OnMouseDown(f.Mouse(mouse.X, mouse.Y));
        composer.OnMouseUp(f.Mouse(mouse.X, mouse.Y));
        Assert.Equal(3, callbacks);
        button.Enabled = true;
        f.SetMouse(mouse.X, mouse.Y);
        f.Render(button);
        button.InsideClipBounds = ElementBounds.Fixed(500, 500, 10, 10).WithEmptyParent();
        button.InsideClipBounds.CalcWorldBounds();
        composer.OnMouseDown(f.Mouse(mouse.X, mouse.Y));
        f.Render(button);
        Assert.False(button.On);
        Assert.Equal(3, callbacks);
        Assert.Equal(textureCount, f.Graphics.Textures.Count);
        composer.Dispose();
        button.Dispose();
        Assert.Empty(f.Graphics.Textures);
    }
}
