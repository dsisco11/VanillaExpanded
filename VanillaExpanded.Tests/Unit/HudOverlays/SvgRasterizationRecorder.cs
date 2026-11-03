using Moq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Records production LoadSvg requests while returning correctly sized, headless textures.</summary>
internal sealed class SvgRasterizationRecorder
{
    public readonly record struct Request(string Path, int Width, int Height, int TextureWidth, int TextureHeight);

    public List<Request> Requests { get; } = new();
    public List<LoadedTexture> Textures { get; } = new();
    public System.Func<Request, LoadedTexture>? CreateTexture { get; set; }

    /// <summary>Installs the real default TextureElement loader boundary without invoking graphics APIs.</summary>
    public void Setup(Mock<IGuiAPI> gui, ICoreClientAPI api)
    {
        gui.Setup(value => value.LoadSvg(
                It.IsAny<AssetLocation>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
            .Callback<AssetLocation, int, int, int, int, int?>((location, width, height, textureWidth, textureHeight, _) =>
            {
                var request = new Request(location.Path, width, height, textureWidth, textureHeight);
                Requests.Add(request);
                var texture = CreateTexture?.Invoke(request)
                    ?? new LoadedTexture(api) { TextureId = 0, Width = width, Height = height };
                Textures.Add(texture);
            })
            .Returns(() => Textures[^1]);
    }
}
