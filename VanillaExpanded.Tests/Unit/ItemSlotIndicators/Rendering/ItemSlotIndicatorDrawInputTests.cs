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
    /// <summary>UI styles overlay slot depth, while only horizontal bars override nonzero sampled opacity.</summary>
    [Theory]
    [InlineData(0, 0.35f, false)]
    [InlineData(1, 0.35f, true)]
    [InlineData(2, 1f, true)]
    public void StyleInput_PreservesOutlineOpacityAndMakesBarsOpaque(int style, float alpha, bool overlays)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0.5f, new(0.2f, 0.3f, 0.4f, 0.35f)),
            out var input, (ItemSlotIndicatorRenderingStyle)style));
        Assert.Equal(alpha, input.Color.W);
        Assert.Equal(overlays, input.DrawOverSlotGui);
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0.5f, Vector4.Zero),
            out _, (ItemSlotIndicatorRenderingStyle)style));
    }
    /// <summary>Bounded mode maps resource endpoints while keeping empty visible and the resource fraction unchanged.</summary>
    [Theory]
    [InlineData(0, 0.2f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1, 0.8f)]
    [InlineData(-1, 0.2f)]
    [InlineData(2, 0.8f)]
    public void BoundedFill_MapsLevelsWithoutChangingResourceMeaning(float resource, float height)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48,
            new(resource, Vector4.One, new ItemSlotIndicatorDrawRange(0.2f, 0.8f)), out var input));
        Assert.Equal(height, input.Fill, 5);
        Assert.Equal(Math.Clamp(resource, 0, 1), input.ResourceFill);
        var matrix = new float[16];
        input.RectangleMatrix(Mat4f.Create(), matrix);
        Assert.Equal(48 * height / 2, matrix[5], 4);
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0, Vector4.One), out _));
    }

    /// <summary>Cues fade near endpoints, remain at the chosen level, and vanish through the middle of the resource range.</summary>
    [Theory]
    [InlineData(0, true, 0.6f, 0.2f)]
    [InlineData(0.075f, true, 0.3f, 0.2f)]
    [InlineData(0.15f, false, 0, 0)]
    [InlineData(0.5f, false, 0, 0)]
    [InlineData(0.925f, true, 0.3f, 0.8f)]
    [InlineData(1, true, 0.6f, 0.8f)]
    public void BoundaryCue_FadesAtFixedSlotLevels(float resource, bool visible, float opacity, float level)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48,
            new(resource, Vector4.One, new ItemSlotIndicatorDrawRange(0.2f, 0.8f)), out var input));
        Assert.Equal(visible, input.TryCreateBoundaryCue(out var cue));
        if (!visible) return;
        Assert.Equal(opacity, cue.Color.W, 5);
        Assert.Equal(48 * (1 - level) - 0.5f, cue.SlotBounds.Y, 4);
        Assert.Equal(1, cue.SlotBounds.W);
        Assert.Equal(1, cue.Fill);
    }

    /// <summary>Malformed ranges cannot enter rendering, and slot-edge cues remain inside the slot.</summary>
    [Fact]
    public void DrawRange_RejectsInvalidLevelsAndContainsEdgeCues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorDrawRange(-0.1f, 0.8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorDrawRange(0.8f, 0.2f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorDrawRange(0.5f, 0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemSlotIndicatorDrawRange(0.2f, float.NaN));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0.5f, Vector4.One, default(ItemSlotIndicatorDrawRange)), out _));
        foreach (float fill in new[] { 0f, 1f })
        {
            Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(fill, Vector4.One, new ItemSlotIndicatorDrawRange(0, 1)), out var input));
            Assert.True(input.TryCreateBoundaryCue(out var cue));
            Assert.InRange(cue.SlotBounds.Y, 0, 47);
        }
    }

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
