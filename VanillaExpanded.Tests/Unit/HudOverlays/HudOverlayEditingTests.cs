using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks that cursor release alone never grants HUD editing permission.</summary>
public sealed class HudOverlayEditingTests
{
    #region Public API
    /// <summary>Inventory and ordinary free-cursor dialogs do not activate layout editing.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OrdinaryFreeCursorDoesNotActivateEditing(bool inventory)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var opened = new List<GuiDialog>();
        if (inventory)
        {
            var dialog = new Mock<GuiDialog>(api.Object);
            dialog.Setup(value => value.IsOpened()).Returns(true);
            opened.Add(dialog.Object);
        }
        api.SetupGet(value => value.Input.MouseGrabbed).Returns(false);
        api.SetupGet(value => value.Gui.OpenedGuis).Returns(opened);
        Assert.False(HudOverlayEditing.IsActive(api.Object));
    }

    /// <summary>The actual native escape menu grants editing even when the world cannot pause.</summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void NativePauseMenuRequiresVisibleHudAndFreeCursor(bool grabbed, bool hidden, bool expected)
    {
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var type = typeof(ShaderProgram).Assembly.GetType("Vintagestory.Client.NoObf.GuiDialogEscapeMenu", true)!;
        var menu = (GuiDialog)RuntimeHelpers.GetUninitializedObject(type);
        typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(menu, true);
        api.SetupGet(value => value.IsGamePaused).Returns(false);
        api.SetupGet(value => value.HideGuis).Returns(hidden);
        api.SetupGet(value => value.Input.MouseGrabbed).Returns(grabbed);
        api.SetupGet(value => value.Gui.OpenedGuis).Returns(new List<GuiDialog> { menu });
        Assert.Equal(expected, HudOverlayEditing.IsActive(api.Object));
        Assert.False(HudOverlayEditing.BlocksAllInput(menu));
    }

    /// <summary>Unrelated modal dialogs retain their native all-input capture contract.</summary>
    [Fact]
    public void OtherModalDialogsStillBlockInput()
    {
        var dialog = new Mock<GuiDialog>(Mock.Of<ICoreClientAPI>());
        dialog.Setup(value => value.CaptureAllInputs()).Returns(true);
        Assert.True(HudOverlayEditing.BlocksAllInput(dialog.Object));
    }
    #endregion
}
