using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Exercises popup ownership and settings routing without allocating native graphics.</summary>
public sealed class HudOverlayContextMenuTests
{
    #region Public API
    /// <summary>Rows reflect authoritative values and callbacks retire controls after native dispatch returns.</summary>
    [Theory]
    [InlineData("enabled", false)]
    [InlineData("lock", true)]
    public void ToggleSelectionDefersDisposal(string code, bool selected)
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Open(HudMenuPage.Toggles));
        Assert.Equal(new[] { "enabled", "lock", "style:0", "style:1" }, fixture.Rows.Select(row => row.Code));
        Assert.True(fixture.Rows[0].Selected); Assert.False(fixture.Rows[1].Selected);
        fixture.Popup.Press = () =>
        {
            fixture.Select!(code, selected);
            Assert.Equal(0, fixture.Popup.Disposals);
        };
        fixture.Menu.OnMouseDown(new MouseEvent(1, 1, EnumMouseButton.Left, 0));
        Assert.Equal(code == "enabled" ? selected : true, fixture.Enabled);
        Assert.Equal(code == "lock" && selected, fixture.Locked);
        Assert.False(fixture.Menu.IsOpened); Assert.Equal(1, fixture.Popup.Disposals);
    }

    /// <summary>Style choices and selection route through the feature-owned enum boundary.</summary>
    [Fact]
    public void StylesReflectAndMutateEnum()
    {
        using var fixture = new Fixture(); Assert.True(fixture.Open(HudMenuPage.Styles));
        Assert.Equal(Enum.GetValues<TestStyle>().Length, fixture.Rows.Count);
        Assert.True(fixture.Rows.Single(row => row.Code == "style:0").Selected);
        fixture.Popup.Press = () => fixture.Select!("style:1", true);
        fixture.Menu.OnMouseDown(new MouseEvent(1, 1, EnumMouseButton.Left, 0));
        Assert.Equal(TestStyle.Second, fixture.Style); Assert.False(fixture.Menu.IsOpened);
    }

    /// <summary>Unchanged frames and off-target pointer movement retain one cached popup and stable target.</summary>
    [Fact]
    public void StableTargetDoesNotRecomposeOnMotion()
    {
        using var fixture = new Fixture(); fixture.Open(HudMenuPage.Toggles);
        fixture.Menu.OnMouseMove(new MouseEvent(-1, -1, EnumMouseButton.None, 0));
        fixture.Menu.Synchronize(fixture.Environment); fixture.Menu.Draw(0);
        Assert.Equal("test:menu", fixture.Menu.TargetId); Assert.Equal(1, fixture.Creates);
        Assert.Equal(1, fixture.Popup.Moves); Assert.Equal(1, fixture.Popup.Draws);
    }

    [Fact]
    public void SizeStepsPersistWithoutClosingOrRecomposingTheMenu()
    {
        using var fixture = new Fixture(withSize: true);
        fixture.Open(HudMenuPage.Toggles);
        Assert.Equal(24, fixture.Rows.Single(row => row.Kind == HudMenuEntryKind.Number).Number);
        foreach (int value in new[] { 30, 36, 24 })
        {
            fixture.Popup.Press = () => fixture.Select!("size:" + value, true);
            fixture.Menu.OnMouseDown(new MouseEvent(1, 1, EnumMouseButton.Left, 0));
            fixture.Menu.Synchronize(fixture.Environment);
            Assert.True(fixture.Menu.IsOpened); Assert.Equal(value, fixture.Size);
            Assert.Equal(0, fixture.Popup.Disposals); Assert.Equal(1, fixture.Creates);
        }
    }

    /// <summary>Every environment invalidator retires resources rather than silently recomposing stale controls.</summary>
    [Theory]
    [InlineData("editing")]
    [InlineData("session")]
    [InlineData("viewport")]
    [InlineData("scale")]
    [InlineData("locale")]
    [InlineData("target")]
    [InlineData("settings")]
    [InlineData("focus")]
    public void ChangedContextCloses(string change)
    {
        using var fixture = new Fixture(); fixture.Open(HudMenuPage.Toggles);
        var next = fixture.Environment;
        switch (change)
        {
            case "editing": next = next with { EditingAvailable = false }; break;
            case "session": next = next with { Session = new object() }; break;
            case "viewport": next = next with { Viewport = RectangleF.Empty }; break;
            case "scale": next = next with { GuiScale = next.GuiScale * 2 }; break;
            case "locale": next = next with { Locale = "other" }; break;
            case "target": fixture.Available = false; break;
            case "settings": fixture.Enabled = false; break;
            case "focus": fixture.Popup.Opened = false; break;
        }
        fixture.Menu.Synchronize(next);
        Assert.False(fixture.Menu.IsOpened); Assert.Null(fixture.Menu.TargetId);
        Assert.Equal(1, fixture.Popup.Disposals); Assert.Equal(1, fixture.Creates);
    }

    /// <summary>Dismissal consumes input and cleanup remains idempotent.</summary>
    [Theory]
    [InlineData(true, -1, -1)]
    [InlineData(false, 1, 1)]
    public void OutsideAndEscapeDismiss(bool outside, int x, int y)
    {
        using var fixture = new Fixture(); fixture.Open(HudMenuPage.Toggles);
        if (outside)
        {
            var args = new MouseEvent(x, y, EnumMouseButton.Left, 0); fixture.Menu.OnMouseDown(args); Assert.True(args.Handled);
        }
        else
        {
            var args = new KeyEvent { KeyCode = (int)GlKeys.Escape }; fixture.Menu.OnKeyDown(args); Assert.True(args.Handled);
        }
        fixture.Menu.Close(); fixture.Menu.Dispose(); Assert.Equal(1, fixture.Popup.Disposals);
    }

    /// <summary>Missing or failed popup preparation leaves no stale target or owned surface.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoryFailureClearsTarget(bool throws)
    {
        using var fixture = new Fixture(); fixture.Fail = true; fixture.Throw = throws;
        if (throws) Assert.Throws<InvalidOperationException>(() => fixture.Open(HudMenuPage.Toggles));
        else Assert.False(fixture.Open(HudMenuPage.Toggles));
        Assert.False(fixture.Menu.IsOpened); Assert.Null(fixture.Menu.TargetId);
    }
    /// <summary>Callbacks from stale controls cannot overwrite externally changed settings.</summary>
    [Fact]
    public void StaleSelectionIsRejected()
    {
        using var fixture = new Fixture(); fixture.Open(HudMenuPage.Toggles);
        fixture.Enabled = false;
        fixture.Popup.Press = () => fixture.Select!("enabled", true);
        fixture.Menu.OnMouseDown(new MouseEvent(1, 1, EnumMouseButton.Left, 0));
        Assert.False(fixture.Enabled); Assert.False(fixture.Menu.IsOpened);
    }

    /// <summary>Exceptions during native dispatch still release the current popup.</summary>
    [Fact]
    public void DispatchFailureRetiresResources()
    {
        using var fixture = new Fixture(); fixture.Open(HudMenuPage.Toggles);
        fixture.Popup.Press = () => throw new InvalidOperationException();
        Assert.Throws<InvalidOperationException>(() => fixture.Menu.OnMouseDown(new MouseEvent(1, 1, EnumMouseButton.Left, 0)));
        Assert.False(fixture.Menu.IsOpened); Assert.Equal(1, fixture.Popup.Disposals);
    }

    #endregion

    #region Private
    /// <summary>Provides distinct defined styles for capability routing.</summary>
    private enum TestStyle { First, Second }

    /// <summary>Owns isolated mutable capabilities and an observable popup boundary.</summary>
    private sealed class Fixture : IDisposable
    {
        public bool Enabled = true, Locked, Available = true, Fail, Throw;
        public TestStyle Style;
        public int Creates;
        public int Size = 24;
        public IReadOnlyList<HudMenuEntry> Rows = Array.Empty<HudMenuEntry>();
        public Action<string, bool>? Select;
        public readonly Popup Popup = new();
        public readonly HudOverlayContextMenu Menu;
        public readonly HudMenuTarget Target;
        public readonly HudMenuEnvironment Environment = new(new object(), new RectangleF(PointF.Empty, new SizeF(100, 100)), 1, "en", true);
        #region Public API
        /// <summary>Connects live settings and a disposable fake surface.</summary>
        public Fixture(bool withSize = false)
        {
            var settings = new HudOverlayMenuSettings(new HudOverlaySetting<bool>(() => Enabled, value => Enabled = value, () => { }),
                new HudOverlayStyleSetting<TestStyle>(() => Style, value => Style = value, () => { }),
                withSize ? new HudOverlaySizeSetting(24, () => Size, value => Size = value, () => { }) : null);
            var registration = new HudOverlayRegistration("test:menu", Mock.Of<IHudOverlay>(), () => true, "test", settings);
            Target = new(registration, () => Available, () => Locked, value => Locked = value);
            Menu = new(Mock.Of<ICoreClientAPI>(), (_, rows, select, _, _) =>
            {
                Creates++; Rows = rows; Select = select;
                if (Throw) throw new InvalidOperationException();
                return Fail ? null : Popup;
            }, (locale, key) => locale + ":" + key);
        }
        /// <summary>Opens a page without rendering or delivering an opening click.</summary>
        public bool Open(HudMenuPage page) => Menu.Open(Target, page, PointF.Empty, Environment);
        /// <summary>Releases the menu after assertions.</summary>
        public void Dispose() => Menu.Dispose();
        #endregion
    }

    /// <summary>Records delegation and resource release while simulating native callback execution.</summary>
    private sealed class Popup : IHudMenuPopup
    {
        public bool Opened = true;
        public int Disposals, Moves, Draws;
        public Action? Press;
        public RectangleF VisibleBounds => new(PointF.Empty, new SizeF(10, 10));
        public bool IsOpened => Opened;
        /// <inheritdoc />
        public void Draw(float deltaTime) => Draws++;
        /// <inheritdoc />
        public void MouseDown(MouseEvent args) => Press?.Invoke();
        /// <inheritdoc />
        public void MouseUp(MouseEvent args) { }
        /// <inheritdoc />
        public void MouseMove(MouseEvent args) => Moves++;
        /// <inheritdoc />
        public void MouseWheel(MouseWheelEventArgs args) { }
        /// <inheritdoc />
        public void KeyDown(KeyEvent args) { }
        /// <inheritdoc />
        public void KeyPress(KeyEvent args) { }
        /// <inheritdoc />
        public void Dispose() => Disposals++;
    }
    #endregion
}
