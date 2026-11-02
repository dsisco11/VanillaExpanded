using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookOptionsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2)]
    public void RecipeBookOptionsSvgStatesUseIsolatedTexturesWithoutModifyingTheSharedDialogSurface(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        using var surface = new ImageSurface(Format.Argb32, 128, 64);
        using var ctx = new Context(surface);
        ctx.SetSourceRGB(1, 1, 1);
        ctx.Paint();
        ctx.SetSourceRGB(0, 0, 0);
        for (int x = 0; x < 128; x += 4)
        {
            ctx.Rectangle(x, 0, 2, 64);
            ctx.Fill();
        }
        surface.Flush();
        var original = surface.Data.ToArray();
        using var options = new GuiElementRecipeBookOptions(f.Lifecycle.Api.Object,
            ElementBounds.Fixed(40.3, 10.3, 17, 17).WithEmptyParent());
        var gui = Mock.Get(f.Lifecycle.Api.Object.Gui);
        gui.Invocations.Clear();
        options.ComposeElements(ctx, surface);
        surface.Flush();
        Assert.Equal(original, surface.Data);
        Assert.Equal(2, f.OptionsDraws.Count);
        int iconSize = (int)Math.Round(GuiElement.scaled(17), MidpointRounding.AwayFromZero);
        Assert.All(f.OptionsDraws, draw =>
        {
            Assert.Equal(iconSize, draw.Width);
            Assert.Equal(iconSize, draw.Height);
            Assert.Equal(iconSize, draw.SurfaceWidth);
            Assert.Equal(iconSize, draw.SurfaceHeight);
        });
        Assert.NotEqual(f.OptionsDraws[0].Color, f.OptionsDraws[1].Color);
        Assert.Equal(6, gui.Invocations.Count(call =>
            call.Method.Name == nameof(IGuiAPI.LoadOrUpdateCairoTexture) && !(bool)call.Arguments[1]));
        var render = Mock.Get(f.Lifecycle.Api.Object.Render);
        foreach (bool hovered in new[] { false, true })
        {
            f.SetMouse(hovered ? (int)(options.Bounds.absX + 5) : 0,
                hovered ? (int)(options.Bounds.absY + 5) : 0);
            render.Invocations.Clear();
            options.RenderInteractiveElements(0);
            var draw = Assert.Single(render.Invocations, call =>
                call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha));
            Assert.Equal(Math.Round(options.Bounds.absX, MidpointRounding.AwayFromZero), (double)draw.Arguments[1]);
            Assert.Equal(Math.Round(options.Bounds.absY, MidpointRounding.AwayFromZero), (double)draw.Arguments[2]);
            Assert.Equal((double)iconSize, (double)draw.Arguments[3]);
            Assert.Equal((double)iconSize, (double)draw.Arguments[4]);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void RecipeBookOptionsControlRestorationWithoutClearingBrowsingState(float scale)
    {
        using var f = new MenuFixture(scale: scale);
        f.Parent.TryOpen();
        int baseline = f.TextureCount;
        f.Toggle();
        var book = Assert.Single(f.Menus);
        book.SingleComposer.GetTextInput("search").SetValue("retained search");
        var options = f.OpenOptions(book);
        Assert.True(options.IsOpened);
        Assert.True(options.HasFocus);
        Assert.True(book.Focused);
        Assert.True(options.Checkbox.On);
        Assert.InRange(options.PopupBounds.absX, book.SingleComposer.Bounds.absX, options.Bounds.absX);
        Assert.InRange(System.Math.Abs(options.PopupBounds.absY - options.Bounds.absY - options.Bounds.OuterHeight), 0, 1);
        var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
        book.OnKeyDown(enter);
        Assert.True(enter.Handled);
        Assert.False(options.Checkbox.On);
        Assert.False(f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
        Assert.True(options.IsOpened);
        Assert.True(book.OnEscapePressed());
        Assert.True(book.Focused);

        f.Parent.TryClose();
        Assert.False(options.IsOpened);
        Assert.Equal(baseline, f.TextureCount);
        f.Parent.TryOpen();
        Assert.Empty(f.Menus);
        f.Toggle();
        book = Assert.Single(f.Menus);
        Assert.Equal("retained search", book.SingleComposer.GetTextInput("search").GetText());
        var secondOptions = f.OpenOptions(book);
        Assert.False(secondOptions.Checkbox.On);
        f.ToggleReopenOption(book);
        Assert.True(secondOptions.Checkbox.On);
        Assert.Equal("retained search", book.SingleComposer.GetTextInput("search").GetText());
        Assert.True(secondOptions.IsOpened);
        Assert.True(book.OnEscapePressed());
        f.Parent.TryClose();
        Assert.Equal(baseline, f.TextureCount);
        f.Parent.TryOpen();
        Assert.Equal("retained search", Assert.Single(f.Menus).SingleComposer.GetTextInput("search").GetText());
    }

    [Fact]
    public void RecipeBookRememberOpenPreferenceSurvivesClientSessions()
    {
        var savedSettings = new Dictionary<string, bool>();
        using (var f = new MenuFixture(savedSettings: savedSettings))
        {
            f.Parent.TryOpen();
            f.Toggle();
            var book = Assert.Single(f.Menus);
            f.OpenOptions(book);
            book.OnKeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
        }
        using var next = new MenuFixture(savedSettings: savedSettings);
        next.Parent.TryOpen();
        next.Toggle();
        next.Parent.TryClose();
        next.Parent.TryOpen();
        Assert.Empty(next.Menus);
        next.Toggle();
        var restoredOptions = next.OpenOptions(Assert.Single(next.Menus));
        Assert.False(restoredOptions.Checkbox.On);
    }

    [Theory]
    [InlineData((int)GlKeys.Enter)]
    [InlineData((int)GlKeys.KeypadEnter)]
    [InlineData((int)GlKeys.Space)]
    public void RecipeBookOptionsKeyboardTogglesBothStatesAndEscapeOnlyDismissesTheList(int key)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = Assert.IsType<GuiElementRecipeBookOptions>(book.SingleComposer.GetElement("options"));
        Assert.True(book.SingleComposer.FocusElement(options.TabIndex));
        var open = new KeyEvent { KeyCode = key };
        book.OnKeyDown(open);
        Assert.True(open.Handled);
        Assert.True(options.IsOpened);
        foreach (bool enabled in new[] { false, true })
        {
            var toggle = new KeyEvent { KeyCode = key };
            book.OnKeyDown(toggle);
            Assert.True(toggle.Handled);
            Assert.Equal(enabled, options.Checkbox.On);
            Assert.Equal(enabled, f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
        }
        Assert.True(book.OnEscapePressed());
        Assert.False(options.IsOpened);
        Assert.True(options.HasFocus);
        Assert.True(book.IsOpened());
        Assert.True(f.Parent.IsOpened());
    }

    [Fact]
    public void RecipeBookOptionsOutsideClickDismissesTheListAndAllowsSearchFocus()
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = f.OpenOptions(book);
        var search = book.SingleComposer.GetTextInput("search");
        search.SetValue("retained search");
        Mock.Get(f.Lifecycle.Api.Object.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[256]);
        int x = (int)(search.Bounds.absX + GuiElement.scaled(5));
        int y = (int)(search.Bounds.absY + GuiElement.scaled(5));
        f.SetMouse(x, y);
        book.OnMouseDown(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        book.OnMouseUp(new MouseEvent(x, y, EnumMouseButton.Left, 0));
        Assert.False(options.IsOpened);
        Assert.True(search.HasFocus);
        Assert.True(book.IsOpened());
        Assert.True(f.Lifecycle.Api.Object.Settings.Bool.Get(RecipeBookSystem.RememberOpenSetting, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenRecipeBookOptionsAreDisposedWithTheirBook(bool externalDisposal)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var book = Assert.Single(f.Menus);
        var options = f.OpenOptions(book);
        if (externalDisposal) book.Dispose();
        else f.Parent.TryClose();
        Assert.False(options.IsOpened);
        Assert.Empty(f.Menus);
    }
}
