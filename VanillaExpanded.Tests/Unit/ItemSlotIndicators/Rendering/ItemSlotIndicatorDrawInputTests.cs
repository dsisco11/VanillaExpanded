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
    /// <summary>Overlay scheduling follows the selected layer and transparent inputs remain rejected.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void StyleInputSchedulesOverlaysAndRejectsTransparentInputs(int style, bool overlays)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0.5f, new(0.2f, 0.3f, 0.4f, 0.35f)),
            out var input, (ItemSlotIndicatorRenderingStyle)style));
        Assert.Equal(overlays, input.DrawOverSlotGui);
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0.5f, Vector4.Zero),
            out _, (ItemSlotIndicatorRenderingStyle)style));
    }
    /// <summary>Bounded inputs preserve sanitized resource fractions and reject unusable geometry.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(float.NaN, 0)]
    public void BoundedInputPreservesSanitizedResourceFraction(float resource, float expectedResource)
    {
        var range = new ItemSlotIndicatorDrawRange(0.2f, 0.8f);
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48,
            new(resource, Vector4.One, range), out var input));
        Assert.Equal(expectedResource, input.ResourceFill);
        Assert.Equal(range, input.DrawRange);
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 0,
            new(resource, Vector4.One, range), out _));
        Assert.False(ItemSlotIndicatorDrawInput.TryCreate(24, 24, 48, new(0, Vector4.One), out _));
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
            Assert.True(cue.SlotBounds.Y >= input.SlotBounds.Y);
            Assert.True(cue.SlotBounds.Y + cue.SlotBounds.W <= input.SlotBounds.Y + input.SlotBounds.W);
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

    /// <summary>Rectangle composition does not mutate the caller's inherited matrix.</summary>
    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void RectangleTransformDoesNotMutateCallerMatrix(float fill)
    {
        Assert.True(ItemSlotIndicatorDrawInput.TryCreate(15.25, 24.75, 61.5f, new(fill, Vector4.One), out var input));
        var inherited = Mat4f.Create();
        Mat4f.Translate(inherited, inherited, [5, 7, -100]);
        Mat4f.RotateY(inherited, inherited, 0.1f);
        var original = (float[])inherited.Clone();
        var actual = new float[16];
        input.RectangleMatrix(inherited, actual);
        Assert.Equal(original, inherited);
    }
    #endregion
}
