using System.Numerics;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Rendering;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks clipping-safe extents, zero-value retention, and rectangle reuse without graphics resources.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorStyleLayoutTests
{
    #region Public API
    /// <summary>Outline submissions have finite, nonnegative extents contained by the clipped slot.</summary>
    [Theory]
    [InlineData(1, 48)]
    [InlineData(1.25f, 60)]
    [InlineData(2, 96)]
    [InlineData(2, 1)]
    public void OutlineExtentsRemainContainedBySlot(float scale, float size)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(-5.25, -9.5, size, new(1, Vector4.One), out var input));
        var layout = ItemSlotIndicatorStyleLayout.Create(input, scale);
        var strips = new[] { layout.OutlineTop, layout.OutlineBottom, layout.OutlineLeft, layout.OutlineRight };
        foreach (var strip in strips)
        {
            Assert.True(float.IsFinite(strip.X) && float.IsFinite(strip.Y)
                && float.IsFinite(strip.Z) && float.IsFinite(strip.W));
            Assert.InRange(strip.X, input.SlotBounds.X, input.SlotBounds.X + size);
            Assert.InRange(strip.Y, input.SlotBounds.Y, input.SlotBounds.Y + size);
            Assert.True(strip.Z >= 0 && strip.W >= 0);
            Assert.True(strip.X + strip.Z <= input.SlotBounds.X + size);
            Assert.True(strip.Y + strip.W <= input.SlotBounds.Y + size);
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
        Assert.Equal(0, empty.Fill);
        Assert.Equal(0, empty.ResourceFill);
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

    /// <summary>Caller-provided adjacent rectangles share a clipping boundary, and invalid scales remain rejected.</summary>
    [Fact]
    public void RectangleTransformPreservesSharedClipEdgeAndRejectsInvalidScale()
    {
        var left = ItemSlotIndicatorStyleLayout.Rectangle(new(-4.75f, 3.125f, 1.25f, 55), Vector4.One);
        var right = ItemSlotIndicatorStyleLayout.Rectangle(new(-3.5f, 3.125f, 1.75f, 55), Vector4.One);
        var inherited = Mat4f.Create();
        var leftMatrix = new float[16];
        var rightMatrix = new float[16];
        left.RectangleMatrix(inherited, leftMatrix);
        right.RectangleMatrix(inherited, rightMatrix);
        Assert.Equal(leftMatrix[12] + leftMatrix[0], rightMatrix[12] - rightMatrix[0], 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemSlotIndicatorStyleLayout.Create(left, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ItemSlotIndicatorStyleLayout.Create(right, 0));
    }
    #endregion
}
