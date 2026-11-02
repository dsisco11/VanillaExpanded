using Cairo;

namespace VanillaExpanded.Tests.TestSupport.Gui;

internal static class GuiTestPixels
{
    internal static int Pixel(ImageSurface surface, int x, int y)
    {
        surface.Flush();
        int offset = y * surface.Stride + x * 4;
        var pixels = surface.Data;
        return pixels[offset + 2] << 16 | pixels[offset + 1] << 8 | pixels[offset];
    }
}