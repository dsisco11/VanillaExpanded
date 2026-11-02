using System.Reflection;
using Cairo;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.Tests.TestSupport.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.Gui;

internal static class GuiStyledControlsTestsTestSupport
{

internal sealed record Raster(int Width, int Height, int Stride, byte[] Pixels)
    {
        internal int Rgb(int x, int y)
        {
            int p = y * Stride + x * 4;
            return Pixels[p + 2] << 16 | Pixels[p + 1] << 8 | Pixels[p];
        }
        internal int MaxAlpha => Pixels.Where((_, i) => i % 4 == 3).Max(value => (int)value);
        internal bool HasColor(int rgb)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (Pixels[y * Stride + x * 4 + 3] == 255 && Rgb(x, y) == rgb) return true;
            return false;
        }
    }

    internal sealed class ControlsFixture : IDisposable
    {
        internal readonly RecipeBookRenderFixture Graphics;
        internal ICoreClientAPI Api => Graphics.Api.Object;
        private readonly string oldLocale = Lang.CurrentLocale;
        private readonly ITranslationService? oldEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        private readonly Dictionary<int, Raster> rasters = new();
        private int nextTexture = 10000;
        internal int Uploads;
        internal Moq.IInvocation[] Draws => Graphics.Render.Invocations.Where(call =>
            call.Method.Name.StartsWith("Render2DTexture", StringComparison.Ordinal)).ToArray();

        internal ControlsFixture(float scale = 1)
        {
            Graphics = new RecipeBookRenderFixture(scale);
            var translations = new Mock<ITranslationService>();
            translations.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>()))
                .Returns((string key, object[] _) => key);
            Lang.AvailableLanguages["en"] = translations.Object;
            Lang.ChangeLanguage("en");
            Graphics.Render.SetupGet(value => value.FrameWidth).Returns(1000);
            Graphics.Render.SetupGet(value => value.FrameHeight).Returns(1000);
            Graphics.Gui.Setup(value => value.LoadOrUpdateCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>(),
                ref It.Ref<LoadedTexture>.IsAny)).Callback(new Upload((ImageSurface image, bool _, ref LoadedTexture texture) =>
                {
                    if (texture.TextureId == 0) { texture.TextureId = ++nextTexture; Graphics.Textures.Add(texture.TextureId); }
                    texture.Width = image.Width;
                    texture.Height = image.Height;
                    image.Flush();
                    rasters[texture.TextureId] = new(image.Width, image.Height, image.Stride, image.Data.ToArray());
                    Uploads++;
                }));
            Graphics.Gui.Setup(value => value.LoadCairoTexture(It.IsAny<ImageSurface>(), It.IsAny<bool>()))
                .Returns(() => { int id = ++nextTexture; Graphics.Textures.Add(id); return id; });
            Graphics.Render.Setup(value => value.GLDeleteTexture(It.IsAny<int>()))
                .Callback<int>(id => Assert.True(Graphics.Textures.Remove(id)));
            Mock.Get(Api.Input).SetupGet(value => value.KeyboardKeyStateRaw).Returns(new bool[512]);
            Graphics.Api.SetupGet(value => value.ElapsedMilliseconds).Returns(1000);
            SetMouse(-100, -100);
        }

        internal GuiComposer NewComposer() => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            [Api, ElementBounds.Fixed(0, 0, 600, 300).WithEmptyParent(), "styled-controls"], null)!;

        internal GuiComposer Compose(GuiElement control)
        {
            var composer = NewComposer();
            composer.AddInteractiveElement(control, "control").Compose(false);
            return composer;
        }

        internal void Render(GuiElement control)
        {
            Graphics.Render.Invocations.Clear();
            control.RenderInteractiveElements(0);
        }
        internal Raster Drawn(int index) => rasters[(int)Draws[index].Arguments[0]];
        internal Raster Popup() => rasters[(int)Draws.Single(call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexture) && (float)call.Arguments[5] == 310).Arguments[0]];
        internal MouseEvent Mouse(double x, double y) => new((int)x, (int)y, EnumMouseButton.Left, 0);
        internal void SetMouse(double x, double y)
        {
            Mock.Get(Api.Input).SetupGet(value => value.MouseX).Returns((int)x);
            Mock.Get(Api.Input).SetupGet(value => value.MouseY).Returns((int)y);
        }
        public void Dispose()
        {
            if (oldEnglish == null) Lang.AvailableLanguages.Remove("en");
            else Lang.AvailableLanguages["en"] = oldEnglish;
            Lang.ChangeLanguage(oldLocale);
            Graphics.Dispose();
        }
        private delegate void Upload(ImageSurface image, bool linear, ref LoadedTexture texture);
    }

}
