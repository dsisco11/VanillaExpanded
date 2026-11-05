using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.TestSupport.Gui;

internal static class GuiElementIconTabsTestsTestSupport
{

internal static void AssertHints()
    {
        using var graphics = new GuiRenderFixture();
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

internal static (int Left, int Top, int Right, int Bottom) NativeScissorRectangle(GuiRenderFixture graphics)
    {
        var call = Assert.Single(graphics.Render.Invocations.Where(value =>
            value.Method.Name == nameof(IRenderAPI.PushScissor)));
        var bounds = Assert.IsAssignableFrom<ElementBounds>(call.Arguments[0]);
        Assert.False((bool)call.Arguments[1]);
        // Match the installed renderer's non-stacking OpenGL scissor conversion.
        int left = (int)Math.Round(bounds.renderX);
        int bottom = graphics.Render.Object.FrameHeight -
            (int)Math.Round(graphics.Render.Object.FrameHeight - bounds.renderY - bounds.InnerHeight);
        return (left, bottom - (int)bounds.InnerHeight, left + (int)bounds.InnerWidth, bottom);
    }

    internal static void Setup(GuiRenderFixture graphics)
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

    internal static void SetMouse(GuiRenderFixture graphics, double x, double y)
    {
        var input = Mock.Get(graphics.Api.Object.Input);
        input.SetupGet(value => value.MouseX).Returns((int)x);
        input.SetupGet(value => value.MouseY).Returns((int)y);
    }

    internal static GuiElementIconTabs Create(GuiRenderFixture graphics, string[] names, double width, Action<int> changed)
    {
        var bounds = ElementBounds.Fixed(120, 65, width, GuiElementIconTabs.TabHeight).WithEmptyParent();
        bounds.CalcWorldBounds();
        var tabs = new GuiElementIconTabs(graphics.Api.Object,
            names.Select((name, index) => new GuiTab { Name = name, DataInt = index }).ToArray(),
            names.Select(_ => "test-icon").ToArray(), bounds, changed);
        Compose(tabs);
        return tabs;
    }

    internal static void Compose(GuiElementIconTabs tabs)
    {
        using var surface = new ImageSurface(Format.Argb32, tabs.Bounds.OuterWidthInt, tabs.Bounds.OuterHeightInt);
        using var ctx = new Context(surface);
        tabs.ComposeElements(ctx, surface);
    }

}
