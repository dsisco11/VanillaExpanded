using System.Drawing;
using Vintagestory.API.Config;
using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks native list ownership without preparing GPU-backed artwork.</summary>
[Collection("HudOverlayGeometry")]
public sealed class HudOverlayListMenuTests
{
    #region Public API
    /// <summary>The adapter is a native list and can retire unprepared resources repeatedly without graphics calls.</summary>
    [Fact]
    public void UnpreparedNativeListDisposesSafely()
    {
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        var gui = new Mock<IGuiAPI>();
        gui.SetupGet(value => value.WindowBounds).Returns(ElementBounds.Empty);
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        var list = new HudOverlayListMenu(api.Object, new[] { "first", "second" }, new[] { "First", "Second" }, 1,
            (_, _) => Assert.Fail("No selection should occur before input."));
        Assert.IsAssignableFrom<GuiElementListMenu>(list);
        Assert.Equal(1, list.SelectedIndex); Assert.False(list.IsOpened);
        list.Dispose(); list.Dispose();
        IHudMenuPopup popup = list;
        popup.Draw(0); popup.MouseDown(new MouseEvent(0, 0)); popup.MouseUp(new MouseEvent(0, 0));
        popup.MouseMove(new MouseEvent(0, 0)); popup.MouseWheel(new MouseWheelEventArgs());
        popup.KeyDown(new KeyEvent()); popup.KeyPress(new KeyEvent());
        Assert.False(list.IsPositionInside(0, 0));
    }
    /// <summary>Actual native preparation fits its unscissored track and clips oversized content and native handles.</summary>
    [Theory]
    [InlineData(1, 17, 23, 45, 65, 200, 200)]
    [InlineData(1.5f, 31, 19, 55, 40, 0, 0)]
    [InlineData(2, 11, 13, 35, 12, 200, 200)]
    public void PreparedNativeDrawsStayWithinViewport(float scale, float x, float y, float width, float height, float px, float py)
    {
        float previous = RuntimeEnv.GUIScale; RuntimeEnv.GUIScale = scale;
        string previousLocale = Lang.CurrentLocale;
        var language = new Mock<ITranslationService>();
        Lang.AvailableLanguages.TryGetValue("en", out var previousLanguage);
        Lang.AvailableLanguages["en"] = language.Object;
        typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, "en");
        var api = new Mock<ICoreClientAPI>(); var gui = new Mock<IGuiAPI>(); var render = new Mock<IRenderAPI>();
        var scissors = new Stack<ElementBounds>();
        api.SetupGet(value => value.Gui).Returns(gui.Object); api.SetupGet(value => value.Render).Returns(render.Object);
        gui.SetupGet(value => value.WindowBounds).Returns(new Window());
        render.SetupGet(value => value.FrameWidth).Returns(800); render.SetupGet(value => value.FrameHeight).Returns(600);
        render.SetupGet(value => value.ScissorStack).Returns(scissors);
        render.Setup(value => value.PushScissor(It.IsAny<ElementBounds>(), It.IsAny<bool>()))
            .Callback<ElementBounds, bool>((bounds, _) => scissors.Push(bounds));
        render.Setup(value => value.PopScissor()).Callback(() => scissors.Pop());
        gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<Cairo.ImageSurface>(), true, ref It.Ref<LoadedTexture>.IsAny))
            .Callback(new Upload((Cairo.ImageSurface surface, bool _, ref LoadedTexture texture) =>
            { texture.Width = surface.Width; texture.Height = surface.Height; }));
        try
        {
            string? selected = null;
            using var list = new HudOverlayListMenu(api.Object, new[] { "a", "b", "c" },
                new[] { "A deliberately long localized label", "Another long label", "Final label" }, 0, (code, _) => selected = code);
            var viewport = new RectangleF(x, y, width, height);
            Assert.True(list.Prepare(new PointF(px, py), viewport));
            Assert.True(viewport.Contains(list.VisibleBounds));
            render.Invocations.Clear(); list.Draw(0);
            var active = new Stack<RectangleF>(); int draws = 0;
            foreach (var invocation in render.Invocations)
            {
                if (invocation.Method.Name == "PushScissor")
                {
                    var bounds = (ElementBounds)invocation.Arguments[0];
                    active.Push(new RectangleF((float)bounds.renderX, (float)bounds.renderY, (float)bounds.OuterWidth, (float)bounds.OuterHeight));
                }
                else if (invocation.Method.Name == "PopScissor") active.Pop();
                else if (invocation.Method.Name is "Render2DTexture" or "Render2DTexturePremultipliedAlpha")
                {
                    var draw = new RectangleF(Convert.ToSingle(invocation.Arguments[1]), Convert.ToSingle(invocation.Arguments[2]),
                        Convert.ToSingle(invocation.Arguments[3]), Convert.ToSingle(invocation.Arguments[4]));
                    foreach (var clip in active) draw = RectangleF.Intersect(draw, clip);
                    if (!draw.IsEmpty) Assert.True(viewport.Contains(draw), $"Native draw {draw} exceeds {viewport}.");
                    draws++;
                }
            }
            Assert.True(draws > 0); Assert.Empty(active); Assert.Empty(scissors);
            list.KeyDown(new KeyEvent { KeyCode = (int)GlKeys.End });
            list.KeyDown(new KeyEvent { KeyCode = (int)GlKeys.Enter });
            Assert.Equal("c", selected); Assert.False(list.IsOpened);

            // Exercise the actual embedded dropdown boundary with the same constrained native viewport.
            selected = null;
            gui.Object.WindowBounds.IsDrawingSurface = true;
            var field = ElementBounds.Fixed(x / scale, y / scale, width / scale, height / scale).WithParent(gui.Object.WindowBounds);
            using var dropdown = new HudOverlayStyleDropDown(api.Object, new[] { "a", "b", "c" }, new[] { "First", "Second", "Third" },
                0, (code, _) => selected = code, field, CairoFont.WhiteSmallText(), viewport);
            using var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, 800, 600);
            using var context = new Cairo.Context(surface);
            dropdown.ComposeElements(context, surface);
            dropdown.OnFocusGained();
            var opening = new MouseEvent((int)x + 1, (int)y + 1, Vintagestory.API.Common.EnumMouseButton.Left, 0);
            dropdown.OnMouseDown(api.Object, opening);
            Assert.True(opening.Handled); Assert.True(dropdown.listMenu.IsOpened); Assert.Null(selected);
            Assert.True(viewport.Contains(dropdown.ExpandedBounds));
            dropdown.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.End });
            dropdown.OnKeyDown(api.Object, new KeyEvent { KeyCode = (int)GlKeys.Enter });
            Assert.Equal("c", selected); Assert.Equal(selected, dropdown.SelectedValue);
            dropdown.Dispose(); dropdown.Dispose();
        }
        finally { RuntimeEnv.GUIScale = previous; typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!.SetValue(null, previousLocale);
            if (previousLanguage == null) Lang.AvailableLanguages.Remove("en"); else Lang.AvailableLanguages["en"] = previousLanguage; }
    }

    /// <summary>Describes the native ref texture upload boundary.</summary>
    private delegate void Upload(Cairo.ImageSurface surface, bool linear, ref LoadedTexture texture);

    /// <summary>Supplies a native initialized parent without opening a window.</summary>
    private sealed class Window : ElementBounds
    {
        /// <summary>Initializes synthetic physical screen dimensions.</summary>
        public Window() { IsWindowBounds = true; CalcWorldBounds(); }
        public override double renderX => 0;
        public override double renderY => 0;
        public override double absX => 0;
        public override double absY => 0;
        /// <summary>Provides the caller's test framebuffer without a platform.</summary>
        public override void CalcWorldBounds() { absInnerWidth = 800; absInnerHeight = 600; Initialized = true; }
    }
    #endregion
}
