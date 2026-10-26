using System.Drawing;
using System.Reflection;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering.Native;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Supplies real native composition with mocked uploads and synthetic texture dimensions.</summary>
internal sealed class HudNativeComponentFixture : IDisposable
{
    private readonly float previousScale = RuntimeEnv.GUIScale;
    private int nextTexture = 100;

    #region Public API
    public Mock<ICoreClientAPI> Api { get; } = new();
    public Mock<IGuiAPI> Gui { get; } = new();
    public Mock<IRenderAPI> Render { get; } = new();
    public IconReadingContent Content { get; set; } = new("art.png", new(27, 29), "reading", 7);
    public int TextCreates { get; private set; }
    public int IconLoads { get; private set; }
    public List<Exception> Failures { get; } = new();
    public List<LoadedTexture> Glyphs { get; } = new();
    public List<LoadedTexture> Artwork { get; } = new();
    public bool OwnArtwork { get; set; }
    public bool ThrowText { get; set; }
    /// <summary>Provides the native context used by independently created views.</summary>
    public HudOverlayPreparationContext Context => new(Api.Object, RuntimeEnv.GUIScale, "en");
    /// <summary>Initializes borrowed services while leaving Cairo/native bounds and composition real.</summary>
    public HudNativeComponentFixture(float scale = 1)
    {
        RuntimeEnv.GUIScale = scale;
        Api.SetupGet(api => api.Gui).Returns(Gui.Object);
        Api.SetupGet(api => api.Render).Returns(Render.Object);
        Api.SetupGet(api => api.Input).Returns(new Mock<IInputAPI>().Object);
        Api.SetupGet(api => api.Logger).Returns(new Mock<ILogger>().Object);
    }
    /// <summary>Creates unique component instances with observable owned/borrowed resources.</summary>
    public IconReadingView CreateView() => new(Api.Object, () => Content, Failures.Add,
        (context, value, _) =>
        {
            if (ThrowText) throw new InvalidOperationException("glyph failure");
            TextCreates++;
            var texture = new LoadedTexture(context.Api, nextTexture++, (int)(value.Length * 7 * context.GuiScale), (int)(13 * context.GuiScale));
            Glyphs.Add(texture);
            return texture;
        },
        (context, _) =>
        {
            IconLoads++;
            var texture = new LoadedTexture(context.Api, nextTexture++, 37, 43) { IgnoreUndisposed = !OwnArtwork };
            Artwork.Add(texture);
            return new(texture, OwnArtwork);
        });
    /// <summary>Constructs the supported native composer without starting the game.</summary>
    public GuiComposer CreateComposer()
    {
        var bounds = ElementBounds.Fixed(0, 0, 240, 210).WithEmptyParent();
        return ((GuiComposer)Activator.CreateInstance(typeof(GuiComposer), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { Api.Object, bounds, "native-components" }, null)!).OnlyDynamic();
    }
    /// <summary>Mounts and composes native components, then performs the host's explicit geometry finalization.</summary>
    public void Mount(IconReadingView view, GuiComposer composer, string prefix = "test")
    {
        view.Prepare(Context);
        view.AddTo(composer, ElementBounds.Fixed(19, 31, 150, 120), prefix);
        composer.Compose(false);
        view.LayoutCompleted(new RectangleF(0, 0, 500, 500));
    }
    /// <summary>Restores the shared native scale without claiming live graphics acceptance.</summary>
    public void Dispose() => RuntimeEnv.GUIScale = previousScale;
    #endregion
}
