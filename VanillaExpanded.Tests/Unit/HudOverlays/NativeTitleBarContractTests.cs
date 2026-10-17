using System.Reflection;
using Moq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Probes installed native titlebar behavior relevant to hover controls without opening a game or graphics context.</summary>
[Collection("HudOverlayGeometry")]
public sealed class NativeTitleBarContractTests : IDisposable
{
    private readonly float oldScale = RuntimeEnv.GUIScale;
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    private readonly string? locale = Lang.CurrentLocale;
    private readonly Mock<ICoreClientAPI> api = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IGuiAPI> gui = new();
    private readonly ElementBounds parent;
    private readonly GuiElementDialogTitleBar titleBar;

    #region Public API
    /// <summary>Creates the actual installed titlebar; reflection supplies only normally composed hit rectangles and drag state.</summary>
    public NativeTitleBarContractTests()
    {
        RuntimeEnv.GUIScale = 1;
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns("TITLE");
        Lang.AvailableLanguages["en"] = language.Object;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.Render.FrameWidth).Returns(1000);
        api.SetupGet(value => value.Render.FrameHeight).Returns(800);
        parent = ElementBounds.Fixed(12.9, 24.8, 200, 100).WithEmptyParent();
        parent.CalcWorldBounds();
        var composer = (GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { api.Object, parent, "vanillaexpanded:test:native-titlebar" }, null)!;
        var bounds = ElementBounds.Fixed(0, 0, 200, 20);
        parent.WithChild(bounds);
        bounds.CalcWorldBounds();
        titleBar = new GuiElementDialogTitleBar(api.Object, "Title", composer, bounds: bounds);
        SetField("closeIconRect", new Rectangled(10000, 10000, 1, 1));
        SetField("menuIconRect", new Rectangled(10000, 10000, 1, 1));
    }

    /// <summary>Releases native helpers and restores process-wide language and GUI settings.</summary>
    public void Dispose()
    {
        titleBar.Dispose();
        RuntimeEnv.GUIScale = oldScale;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, locale);
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }

    /// <summary>Escape and native focus loss leave the private active drag alive, so cancellation needs external ownership.</summary>
    [Fact]
    public void EscapeAndFocusLossDoNotCancelNativeDrag()
    {
        SetField("moving", true);
        titleBar.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Escape });
        Assert.True(GetMoving());
        titleBar.OnFocusLost();
        Assert.True(GetMoving());
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Never);
        Assert.False(typeof(GuiElementDialogTitleBar).GetField("moving", BindingFlags.Instance | BindingFlags.NonPublic)!.IsPublic);
        Assert.False(typeof(GuiElementDialogTitleBar).GetProperty(nameof(GuiElementDialogTitleBar.Movable))!.CanWrite);
    }

    /// <summary>The initial movable render writes the native position even when no drag or release has happened.</summary>
    [Fact]
    public void FirstMovableRenderPersistsBeforeAnyDrop()
    {
        SetField("movable", true);
        Assert.False(GetMoving());
        titleBar.RenderInteractiveElements(0);
        gui.Verify(value => value.SetDialogPosition("vanillaexpanded:test:native-titlebar",
            It.Is<Vec2i>(position => position.X == 12 && position.Y == 24)), Times.Once);
        titleBar.RenderInteractiveElements(0);
        gui.Verify(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()), Times.Once);
    }

    /// <summary>Any button release while moving commits truncated parent coordinates, including outside the handle.</summary>
    [Theory]
    [InlineData(EnumMouseButton.Left)]
    [InlineData(EnumMouseButton.Right)]
    [InlineData(EnumMouseButton.Middle)]
    public void AnyButtonReleaseCommitsActiveNativeDrag(EnumMouseButton button)
    {
        SetField("moving", true);
        titleBar.OnMouseUp(api.Object, new MouseEvent(-100, -100, button, 0));
        gui.Verify(value => value.SetDialogPosition("vanillaexpanded:test:native-titlebar",
            It.Is<Vec2i>(position => position.X == 12 && position.Y == 24)), Times.Once);
        Assert.False(GetMoving());
    }
    #endregion

    #region Private
    /// <summary>Reads the native private drag flag solely to characterize cancellation behavior.</summary>
    private bool GetMoving() => (bool)typeof(GuiElementDialogTitleBar).GetField("moving", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(titleBar)!;

    /// <summary>Initializes native state that composition or pointer-down would normally establish.</summary>
    private void SetField(string name, object value) => typeof(GuiElementDialogTitleBar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(titleBar, value);
    #endregion
}
