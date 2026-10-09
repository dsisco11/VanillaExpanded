using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.RadialMenu;

/// <summary>Exercises the actual native dialog key-matching path without creating graphics resources.</summary>
[Trait("Category", "Unit")]
public sealed class RadialMenuDialogKeyTests
{
    #region Public API
    #region Native toggle binding
    /// <summary>Closes parent or picker content through the current default, rebound or modified native hotkey.</summary>
    [Theory]
    [InlineData((int)GlKeys.F, false, false)]
    [InlineData((int)GlKeys.R, false, false)]
    [InlineData((int)GlKeys.R, true, true)]
    [InlineData((int)GlKeys.F, false, true)]
    public void OnKeyDown_CurrentNativeBindingCancels(int key, bool ctrl, bool picker)
    {
        var fixture = new Fixture("toolmodeselect", key, ctrl);
        if (picker) fixture.ShowPicker();
        var args = new KeyEvent { KeyCode = key, CtrlPressed = ctrl };
        fixture.Dialog.OnKeyDown(args);
        Assert.True(args.Handled);
        Assert.False(fixture.Dialog.IsOpened());
        Assert.Equal(1, fixture.Cancellations);
        Assert.Equal(string.Empty, fixture.Dialog.ToggleKeyCombinationCode);
        fixture.Input.Verify(value => value.GetHotKeyByCode("toolmodeselect"), Times.Once);
    }

    /// <summary>Old bindings, wrong modifiers and unrelated keys are consumed without closing.</summary>
    [Theory]
    [InlineData((int)GlKeys.F, false)]
    [InlineData((int)GlKeys.R, false)]
    [InlineData((int)GlKeys.E, true)]
    public void OnKeyDown_UnmatchedInputKeepsModalOpen(int key, bool ctrl)
    {
        var fixture = new Fixture("toolmodeselect", (int)GlKeys.R, true);
        fixture.ShowPicker();
        var args = new KeyEvent { KeyCode = key, CtrlPressed = ctrl };
        fixture.Dialog.OnKeyDown(args);
        Assert.True(args.Handled);
        Assert.True(fixture.Dialog.IsOpened());
        Assert.Equal(0, fixture.Cancellations);
        Assert.Equal("toolmodeselect", fixture.Dialog.ToggleKeyCombinationCode);
    }

    /// <summary>Quick menus without a caller toggle code continue swallowing ordinary keys.</summary>
    [Fact]
    public void OnKeyDown_NoCallerToggleDoesNotCloseOnToolModeKey()
    {
        var fixture = new Fixture(string.Empty, (int)GlKeys.F, false);
        var args = new KeyEvent { KeyCode = (int)GlKeys.F };
        fixture.Dialog.OnKeyDown(args);
        Assert.True(args.Handled);
        Assert.True(fixture.Dialog.IsOpened());
        Assert.Equal(0, fixture.Cancellations);
    }
    #endregion
    #region Cancellation lifecycle
    /// <summary>Escape remains unhandled until the engine calls the existing escape cancellation hook.</summary>
    [Fact]
    public void Escape_PreservesNativeEscapeDispatch()
    {
        var fixture = new Fixture("toolmodeselect", (int)GlKeys.F, false);
        var args = new KeyEvent { KeyCode = (int)GlKeys.Escape };
        fixture.Dialog.OnKeyDown(args);
        Assert.False(args.Handled);
        Assert.True(fixture.Dialog.IsOpened());
        Assert.True(fixture.Dialog.OnEscapePressed());
        Assert.False(fixture.Dialog.IsOpened());
        Assert.Equal(1, fixture.Cancellations);
    }

    /// <summary>Texture reload never replaces the owner's hotkey opening handler.</summary>
    [Fact]
    public void OnBlockTexturesLoaded_DoesNotRegisterToggleHandler()
    {
        var fixture = new Fixture("toolmodeselect", (int)GlKeys.F, false);
        fixture.Dialog.OnBlockTexturesLoaded();
        fixture.Input.VerifyNoOtherCalls();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Constructs only the dialog keyboard state and managed layout resources needed by these tests.</summary>
    private sealed class Fixture
    {
        public RadialMenuDialog Dialog { get; }
        public Mock<IInputAPI> Input { get; } = new();
        public int Cancellations { get; private set; }

        /// <summary>Bypasses graphics startup while retaining native GuiDialog composers, hotkey matching and closure.</summary>
        public Fixture(string toggleCode, int key, bool ctrl)
        {
            var api = new Mock<ICoreClientAPI>();
            var world = new Mock<IClientWorldAccessor>();
            api.Setup(value => value.World).Returns(world.Object);
            api.Setup(value => value.Input).Returns(Input.Object);
            api.Setup(value => value.Gui).Returns(new Mock<IGuiAPI>().Object);
            var hotkey = new HotKey { Code = "toolmodeselect", CurrentMapping = new KeyCombination { KeyCode = key, Ctrl = ctrl } };
            Input.Setup(value => value.GetHotKeyByCode("toolmodeselect")).Returns(hotkey);
            Dialog = (RadialMenuDialog)RuntimeHelpers.GetUninitializedObject(typeof(RadialMenuDialog));
            SetField(Dialog, "capi", api.Object);
            SetField(Dialog, "opened", true);
            SetField(Dialog, "toggleKeyCode", toggleCode);
            Dialog.Composers = new GuiDialog.DlgComposers(Dialog);
            var layout = new RadialMenuLayout(["parent"], 0, 1);
            var interaction = new RadialMenuInteraction(layout, [new RadialMenuEntry("parent", "Parent", true)]);
            interaction.Cancelled += () => Cancellations++;
            interaction.Open();
            SetField(Dialog, "interaction", interaction);
            SetField(Dialog, "layout", layout);
            // UpdateLayout only retains managed hover and label caches; no shader, mesh or texture is created here.
            var renderer = (RadialMenuRenderer)RuntimeHelpers.GetUninitializedObject(typeof(RadialMenuRenderer));
            SetField(renderer, "hoverAnimation", new RadialMenuHoverAnimation());
            SetField(renderer, "renderedLabels", new Dictionary<string, string>());
            SetField(renderer, "labels", new Dictionary<string, LoadedTexture>());
            SetField(renderer, "labelScales", new Dictionary<string, float>());
            SetField(Dialog, "renderer", renderer);
        }

        /// <summary>Uses the actual dialog layout replacement without releasing its captured hotkey.</summary>
        public void ShowPicker()
        {
            var layout = new RadialMenuLayout(["material"], 0.38, 1, new RadialMenuLayout(["back"], 0, .36));
            Dialog.UpdateLayout(layout, [new RadialMenuEntry("material", "Material", true), new RadialMenuEntry("back", "Back", true)]);
        }
    }

    /// <summary>Initializes managed private fixture state without adding a production testing abstraction.</summary>
    private static void SetField(object instance, string name, object value)
    {
        for (Type? type = instance.GetType(); type is not null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (field is null) continue;
            field.SetValue(instance, value);
            return;
        }
        throw new MissingFieldException(instance.GetType().Name, name);
    }
    #endregion
}
