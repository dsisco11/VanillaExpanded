using System.Numerics;

namespace VanillaExpanded.Tests.Unit.Extensions;

/// <summary>Checks reusable multicolor interpolation independently of indicator features.</summary>
[Trait("Category", "Unit")]
public sealed class ColorUtilExTests
{
    #region Public API
    /// <summary>An empty palette has no defined interpolation result.</summary>
    [Fact]
    public void MultiLerp_EmptyPalette_Throws()
    {
        Assert.Throws<ArgumentException>(() => ColorUtilEx.MultiLerp([], 0.5f));
    }

    /// <summary>A one-color palette remains constant for any amount.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0.5f)]
    [InlineData(2)]
    public void MultiLerp_SingleColor_IsConstant(float amount)
    {
        var color = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

        Assert.Equal(color, ColorUtilEx.MultiLerp([color], amount));
    }

    /// <summary>Two colors retain ordinary Vector4 linear interpolation, including alpha.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(0.125f)]
    [InlineData(0.5f)]
    [InlineData(0.999f)]
    [InlineData(1)]
    public void MultiLerp_TwoColors_MatchesLinearBlend(float amount)
    {
        var first = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
        var last = new Vector4(0.7f, 0.8f, 0.9f, 1);

        Assert.Equal(Vector4.Lerp(first, last, amount), ColorUtilEx.MultiLerp([first, last], amount));
    }

    /// <summary>Out-of-range and nonfinite inputs select defined endpoints.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(float.NegativeInfinity, 0)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(float.NaN, 0)]
    public void MultiLerp_ClampsAmount(float amount, float expected)
    {
        Assert.Equal(new Vector4(expected), ColorUtilEx.MultiLerp([Vector4.Zero, Vector4.One], amount));
    }

    /// <summary>Every evenly spaced intermediate stop is reached exactly in either interpolation mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiLerp_ReachesAllColorStops(bool smooth)
    {
        Vector4[] colors = [Vector4.Zero, new(1, 0, 0, 0.25f), new(1, 1, 0, 0.5f), Vector4.One];

        for (int index = 0; index < colors.Length; index++)
        {
            Assert.Equal(colors[index], ColorUtilEx.MultiLerp(colors, (float)index / (colors.Length - 1), smooth));
        }
    }

    /// <summary>Local segment interpolation works for palettes with more than four colors.</summary>
    [Fact]
    public void MultiLerp_FiveColors_BlendsAdjacentStops()
    {
        Vector4[] colors = [Vector4.Zero, new(0.2f), new(0.4f), new(0.6f), Vector4.One];

        Assert.Equal(Vector4.Lerp(colors[2], colors[3], 0.5f), ColorUtilEx.MultiLerp(colors, 0.625f));
    }

    /// <summary>Smoothstep eases each segment without changing endpoints or the alpha contract.</summary>
    [Fact]
    public void MultiLerp_Smooth_EasesWithinEachSegment()
    {
        Vector4[] colors = [Vector4.Zero, new(0.5f), Vector4.One];
        float expectedBlend = 0.25f * 0.25f * (3 - 2 * 0.25f);

        Assert.Equal(Vector4.Lerp(colors[1], colors[2], expectedBlend), ColorUtilEx.MultiLerp(colors, 0.625f, smooth: true));
        Assert.NotEqual(ColorUtilEx.MultiLerp(colors, 0.625f), ColorUtilEx.MultiLerp(colors, 0.625f, smooth: true));
    }
    #endregion
}