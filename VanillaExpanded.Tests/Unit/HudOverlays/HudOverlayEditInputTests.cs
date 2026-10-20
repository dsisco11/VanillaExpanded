using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks configurable edit input, native cursor ownership, and teardown.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayEditInputTests : IDisposable
{
    private readonly Mock<ICoreClientAPI> api = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IInputAPI> input = new();
    private readonly Mock<IClientEventAPI> events = new();
    private readonly Mock<IGuiAPI> gui = new();
    private readonly List<GuiDialog> loaded = new();
    private readonly HotKey hotkey = new();
    private readonly bool oldEnabled = VanillaExpandedModSystem.Config.EnableHudOverlays;
    private readonly string? oldLocale = Lang.CurrentLocale;
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");
    private readonly HudOverlayEditInput integration;
    private bool ready = true, hidden;

    #region Public API
    /// <summary>Provides native dialog registration without requiring an engine window.</summary>
    public HudOverlayEditInputTests()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = language.Object;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
        VanillaExpandedModSystem.Config.EnableHudOverlays = true;
        api.SetupGet(value => value.Input).Returns(input.Object);
        api.SetupGet(value => value.Event).Returns(events.Object);
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        api.SetupGet(value => value.HideGuis).Returns(() => hidden);
        var world = new Mock<IClientWorldAccessor>();
        var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        player.SetupGet(value => value.Entity).Returns(new EntityPlayer());
        world.SetupGet(value => value.Player).Returns(() => ready ? player.Object : null!);
        api.SetupGet(value => value.World).Returns(world.Object);
        gui.SetupGet(value => value.LoadedGuis).Returns(loaded);
        gui.SetupGet(value => value.OpenedGuis).Returns(() => loaded.Where(value => value.IsOpened()).ToList());
        gui.Setup(value => value.RegisterDialog(It.IsAny<GuiDialog[]>())).Callback<GuiDialog[]>(dialogs => loaded.AddRange(dialogs));
        input.Setup(value => value.SetHotKeyHandler(HudOverlayEditInput.HotkeyCode, It.IsAny<ActionConsumable<KeyCombination>>()))
            .Callback<string, ActionConsumable<KeyCombination>>((_, handler) => hotkey.Handler = handler);
        input.Setup(value => value.GetHotKeyByCode(HudOverlayEditInput.HotkeyCode)).Returns(hotkey);
        integration = new HudOverlayEditInput(api.Object);
    }

    /// <summary>The Controls mapping defaults to F9 and its registered handler toggles native cursor ownership.</summary>
    [Fact]
    public void ConfigurableF9HandlerTogglesNativeDialog()
    {
        input.Verify(value => value.RegisterHotKey(HudOverlayEditInput.HotkeyCode, It.IsAny<string>(), GlKeys.F9,
            HotkeyType.GUIOrOtherControls, false, false, false), Times.Once);
        Assert.True(hotkey.Handler(new KeyCombination()));
        var dialog = Assert.IsType<HudOverlayEditDialog>(Assert.Single(loaded));
        Assert.True(dialog.IsOpened());
        Assert.True(dialog.PrefersUngrabbedMouse);
        Assert.True(dialog.DisableMouseGrab);
        Assert.False(dialog.ShouldReceiveMouseEvents());
        Assert.True(HudOverlayEditing.IsActive(api.Object));
        Assert.True(hotkey.Handler(new KeyCombination()));
        Assert.False(dialog.IsOpened());
        Assert.False(HudOverlayEditing.IsActive(api.Object));
    }

    /// <summary>Missing player, hidden HUD, and global disable prevent opening edit mode.</summary>
    [Theory]
    [InlineData("player")] [InlineData("hidden")] [InlineData("disabled")]
    public void UnreadyInputDoesNotOpenDialog(string cause)
    {
        ready = cause != "player";
        hidden = cause == "hidden";
        VanillaExpandedModSystem.Config.EnableHudOverlays = cause != "disabled";
        Assert.False(hotkey.Handler(new KeyCombination()));
        Assert.Empty(loaded);
    }

    /// <summary>World departure and disabling HUD overlays release explicit edit ownership.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SessionLossClosesDialog(bool disabled)
    {
        hotkey.Handler(new KeyCombination());
        if (disabled)
        {
            VanillaExpandedModSystem.Config.EnableHudOverlays = false;
            integration.Synchronize();
        }
        else events.Raise(value => value.LeaveWorld += null);
        Assert.False(Assert.Single(loaded).IsOpened());
    }

    /// <summary>Disposal closes the cursor owner, removes callbacks, and makes retained handlers inert.</summary>
    [Fact]
    public void DisposeDetachesCallbacksAndRetainedHandler()
    {
        var retained = hotkey.Handler;
        retained(new KeyCombination());
        integration.Dispose();
        integration.Dispose();
        Assert.Null(hotkey.Handler);
        Assert.False(Assert.Single(loaded).IsOpened());
        Assert.False(retained(new KeyCombination()));
        events.VerifyRemove(value => value.LeaveWorld -= It.IsAny<Action>(), Times.Once);
    }

    /// <summary>Teardown preserves a replacement handler registered by another owner.</summary>
    [Fact]
    public void DisposePreservesReplacementHandler()
    {
        ActionConsumable<KeyCombination> replacement = _ => true;
        hotkey.Handler = replacement;
        integration.Dispose();
        Assert.Same(replacement, hotkey.Handler);
    }

    /// <summary>Escape exits the unfocused edit owner while ordinary keys remain unhandled.</summary>
    [Fact]
    public void EscapeClosesUnfocusedEditingDialog()
    {
        hotkey.Handler(new KeyCombination());
        var dialog = Assert.IsType<HudOverlayEditDialog>(Assert.Single(loaded));
        Assert.False(dialog.Focusable);
        Assert.True(dialog.ShouldReceiveKeyboardEvents());
        var ordinary = new KeyEvent { KeyCode = (int)GlKeys.W };
        dialog.OnKeyDown(ordinary);
        Assert.False(ordinary.Handled);
        Assert.True(dialog.IsOpened());
        var escape = new KeyEvent { KeyCode = (int)GlKeys.Escape };
        dialog.OnKeyDown(escape);
        Assert.True(escape.Handled);
        Assert.False(dialog.IsOpened());
        Assert.False(dialog.ShouldReceiveKeyboardEvents());
    }

    /// <summary>Restores shared configuration after releasing integration callbacks.</summary>
    public void Dispose()
    {
        integration.Dispose();
        VanillaExpandedModSystem.Config.EnableHudOverlays = oldEnabled;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, oldLocale);
        if (english == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = english;
    }
    #endregion
}
