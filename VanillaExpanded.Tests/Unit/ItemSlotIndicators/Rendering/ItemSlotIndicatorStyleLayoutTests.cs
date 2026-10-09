using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks style geometry, zero-value retention, and rectangle reuse without graphics resources.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorStyleLayoutTests
{
    #region Public API
    /// <summary>Resource endpoints and partial values produce distinct background heights and bar lengths.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.5f)]
    [InlineData(1)]
    public void Layout_UsesMappedBackgroundAndActualBarFraction(float fraction)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48,
            new(fraction, Vector4.One, new(0.2f, 0.8f)), out var input));
        var layout = ItemSlotIndicatorStyleLayout.Create(input, 1);
        float height = 48 * (0.2f + fraction * 0.6f);
        Assert.Equal(height, layout.Background.W, 4);
        Assert.Equal(48 - height, layout.Background.Y, 4);
        Assert.Equal(new Vector4(4, 43, 40, 2), layout.BarTrack);
        Assert.Equal(new Vector4(4, 43, 40 * fraction, 2), layout.BarFill);
        Assert.Equal(new Vector4(0, 0, 48, 2.25f), layout.OutlineTop);
        Assert.Equal(new Vector4(0, 45.75f, 48, 2.25f), layout.OutlineBottom);
    }

    /// <summary>All border strips remain inside the slot and share edges without overlapping translucent corners.</summary>
    [Theory]
    [InlineData(1, 48)]
    [InlineData(1.25f, 60)]
    [InlineData(2, 96)]
    [InlineData(2, 1)]
    public void Outline_ScalesAndNeverOverlaps(float scale, float size)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(-5.25, -9.5, size, new(1, Vector4.One), out var input));
        var layout = ItemSlotIndicatorStyleLayout.Create(input, scale);
        var strips = new[] { layout.OutlineTop, layout.OutlineBottom, layout.OutlineLeft, layout.OutlineRight };
        foreach (var strip in strips)
        {
            Assert.InRange(strip.X, input.SlotBounds.X, input.SlotBounds.X + size);
            Assert.InRange(strip.Y, input.SlotBounds.Y, input.SlotBounds.Y + size);
            Assert.True(strip.Z >= 0 && strip.W >= 0);
            Assert.True(strip.X + strip.Z <= input.SlotBounds.X + size);
            Assert.True(strip.Y + strip.W <= input.SlotBounds.Y + size);
        }
        for (int a = 0; a < strips.Length; a++)
            for (int b = a + 1; b < strips.Length; b++)
                Assert.False(strips[a].X < strips[b].X + strips[b].Z && strips[b].X < strips[a].X + strips[a].Z
                    && strips[a].Y < strips[b].Y + strips[b].W && strips[b].Y < strips[a].Y + strips[a].W);
        float outerWidth = layout.OutlineTop.Z;
        float outerHeight = layout.OutlineBottom.Y + layout.OutlineBottom.W - layout.OutlineTop.Y;
        float thickness = layout.OutlineTop.W;
        // Disjoint strips must also cover the entire border, rather than avoiding overlap by leaving gaps.
        Assert.Equal(outerWidth * outerHeight - (outerWidth - 2 * thickness) * (outerHeight - 2 * thickness),
            strips.Sum(strip => strip.Z * strip.W), 4);
        if (size > 1)
        {
            Assert.Equal(2.25f * scale, layout.OutlineTop.W);
            Assert.Equal(2 * scale, layout.BarTrack.W);
            Assert.Equal(input.SlotBounds.X + 4 * scale, layout.BarTrack.X);
        }
    }

    /// <summary>Applicable outline and bar presentations retain zero while invisible or malformed inputs stay rejected.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Input_RetainsZeroWithoutApplyingBackgroundRanges(int selectedStyle)
    {
        var style = (ItemSlotIndicatorRenderingStyle)selectedStyle;
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0, Vector4.One), out var empty, style));
        var layout = ItemSlotIndicatorStyleLayout.Create(empty, 1);
        Assert.True(layout.OutlineTop.W > 0 && layout.BarTrack.W > 0);
        Assert.Equal(0, layout.BarFill.Z);
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48,
            new(0.5f, Vector4.One, new(0.2f, 0.6f)), out var input, style));
        Assert.Equal(0.5f, input.Fill);
        Assert.Null(input.DrawRange);
        Assert.False(input.TryCreateBoundaryCue(out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0, Vector4.Zero), out _, style));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 0, new(0, Vector4.One), out _, style));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48,
            new(0, Vector4.One, default(ItemSlotIndicatorDrawRange)), out _, style));
    }

    /// <summary>Style rectangles use the existing transform with exact shared edges and inherited GUI depth.</summary>
    [Fact]
    public void Rectangle_PreservesFractionalBoundsInExistingMatrixPath()
    {
        var bounds = new Vector4(-4.75f, 3.125f, 1.25f, 55);
        var rectangle = ItemSlotIndicatorStyleLayout.Rectangle(bounds, Vector4.One);
        var inherited = Mat4f.Create();
        Mat4f.Translate(inherited, inherited, [7, 9, -100]);
        var matrix = new float[16];
        rectangle.RectangleMatrix(inherited, matrix);
        Assert.Equal(bounds.X + 7, matrix[12] - matrix[0]);
        Assert.Equal(bounds.Y + 9, matrix[13] - matrix[5]);
        Assert.Equal(bounds.Z, matrix[0] * 2);
        Assert.Equal(bounds.W, matrix[5] * 2);
        Assert.Equal(-20, matrix[14]);
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemSlotIndicatorStyleLayout.Create(rectangle, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemSlotIndicatorStyleLayout.Create(rectangle, 0));
    }
    #endregion
}
