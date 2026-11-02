using System;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;

namespace VanillaExpanded.RecipeBook;

internal sealed class RecipeBookTileGeometry
{
    internal const double TileSize = RecipeBookVisualMetrics.TileSize;
    internal const double Gap = RecipeBookVisualMetrics.TileGap;
    internal const double Pitch = TileSize + Gap;
    internal const double HorizontalPadding = RecipeBookVisualMetrics.ListHorizontalPadding;
    internal const double TopPadding = RecipeBookVisualMetrics.ListTopPadding;
    internal const double MinimumWidth = TileSize + 2 * HorizontalPadding;
    internal const double MinimumHeight = TopPadding + TileSize;
    internal const double PreviewInset = 1;
    internal const float PreviewScale = 1.5f;
    internal const double QuantityInset = 2;
    internal const float QuantityFontSize = 11;
    internal int Columns { get; }
    internal double Leading { get; }

    internal RecipeBookTileGeometry(double viewportWidth)
    {
        if (!double.IsFinite(viewportWidth) || viewportWidth < MinimumWidth)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth), "The recipe viewport must fit one padded tile.");
        double usable = viewportWidth - 2 * HorizontalPadding;
        Columns = (int)Math.Floor((usable + Gap) / Pitch);
        Leading = HorizontalPadding + (usable - (Columns * Pitch - Gap)) / 2;
    }

    internal double ContentHeight(int count) => count == 0 ? 0 : TopPadding + Math.Ceiling((double)count / Columns) * Pitch - Gap;

    internal void Place(ElementBounds tile, int column, int row, double scroll, double scale)
    {
        double left = Leading + column * Pitch;
        double top = TopPadding + row * Pitch - scroll;
        double x = Math.Round(left * scale, MidpointRounding.AwayFromZero);
        double y = Math.Round(top * scale, MidpointRounding.AwayFromZero);
        tile.WithFixedPosition(x / scale, y / scale).WithFixedSize(
            (Math.Round((left + TileSize) * scale, MidpointRounding.AwayFromZero) - x) / scale,
            (Math.Round((top + TileSize) * scale, MidpointRounding.AwayFromZero) - y) / scale);
    }

    internal static int PreviewSize(double scale) => Math.Max(2,
        (int)Math.Ceiling((Math.Round(TileSize * scale, MidpointRounding.AwayFromZero)
            - 2 * GuiControlDrawing.Pixels(PreviewInset, scale)) / 2) * 2);
}

internal sealed record RecipeBookTileView(int? AnchorOutput, int AnchorIndex, double RowOffset,
    int? FocusedOutput, int? InspectedOutput, bool HasFocus, bool KeyboardFocus);
