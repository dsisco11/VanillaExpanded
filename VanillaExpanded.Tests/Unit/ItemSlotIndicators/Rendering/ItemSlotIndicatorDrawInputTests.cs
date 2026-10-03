using System.Numerics;

using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Rendering;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks sanitation, clipping-compatible bounds, and legacy transforms against engine matrix composition.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorDrawInputTests
{
    #region Public API
    /// <summary>Invalid/invisible presentations skip submission while finite components clamp and negative coordinates remain valid.</summary>
    [Fact]
    public void InputSanitation_PreservesClippedSlotsAndProviderDefinedFullWarnings()
    {
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(double.NaN, 0, 48, new(1, Vector4.One), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(0, double.PositiveInfinity, 48, new(1, Vector4.One), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(0, 0, 0, new(1, Vector4.One), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(0, 0, float.NaN, new(1, Vector4.One), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(0, 0, 48, new(float.NaN, Vector4.One), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(0, 0, 48, new(-1, Vector4.One), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(0, 0, 48, new(1, new(1, 1, 1, float.NaN)), out _));
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(-5, 0, 48, new(2, new(-1, float.NaN, 2, 0.6f)), out var input));
        Assert.Equal(1, input.Fill);
        Assert.Equal(new Vector4(-29, -24, 48, 48), input.SlotBounds);
        Assert.Equal(new Vector4(0, 0, 1, 0.6f), input.Color);
        Assert.False(ItemSlotIndicatorDrawInput.ValidMatrix(null));
        Assert.False(ItemSlotIndicatorDrawInput.ValidMatrix([1]));
        var matrix = Mat4f.Create();
        Assert.True(ItemSlotIndicatorDrawInput.ValidMatrix(matrix));
        matrix[5] = float.NaN;
        Assert.False(ItemSlotIndicatorDrawInput.ValidMatrix(matrix));
    }

    /// <summary>Scratch composition matches the engine helper's truncation, bottom alignment, local Z80, and inherited dialog transform.</summary>
    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void RectangleTransform_MatchesEngineMatrixOperationsWithoutMutatingCaller(float fill)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(15.25, 24.75, 61.5f, new(fill, Vector4.One), out var input));
        var inherited = Mat4f.Create();
        Mat4f.Translate(inherited, inherited, [5, 7, -100]);
        Mat4f.RotateY(inherited, inherited, 0.1f);
        var original = (float[])inherited.Clone();
        var expected = (float[])inherited.Clone();
        var bounds = ItemSlotIndicatorRenderer.CalculateBounds(15.25, 24.75, 61.5f, fill);
        Mat4f.Translate(expected, expected, [(int)bounds.X, (int)bounds.Y, 80]);
        Mat4f.Scale(expected, expected, [bounds.Width, bounds.Height, 0]);
        Mat4f.Scale(expected, expected, [0.5f, 0.5f, 0]);
        Mat4f.Translate(expected, expected, [1, 1, 0]);
        var actual = new float[16];
        input.RectangleMatrix(inherited, actual);
        for (int index = 0; index < 16; index++) Assert.Equal(expected[index], actual[index], 4);
        Assert.Equal(original, inherited);
    }
    #endregion
}
