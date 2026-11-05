using Newtonsoft.Json.Linq;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Checks fallback for invalid effective dimensions and overflowing compositions.</summary>
public sealed class ToolHeadPresentationMatrixTests
{
    #region Public API
    /// <summary>Caller depth moves the model pivot without altering authored presentation or screen placement.</summary>
    [Theory]
    [InlineData(49d)]
    [InlineData(50d)]
    [InlineData(100d)]
    public void ExplicitDepthPreservesPivotPlacement(double depth)
    {
        Assert.True(ToolHeadPresentationMatrix.TryCreate(Settings("{}"), 123, 234, 24, 0, out var matrix, depth));
        AssertPoint(matrix, .5f, .5f, .5f, 123, 234, (float)depth);
    }
    /// <summary>Rejects invalid effective draw dimensions and final composition overflow.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    public void InvalidOrOverflowingSizeFallsBack(float size)
    {
        Assert.False(ToolHeadPresentationMatrix.TryCreate(Settings("{\"transform\":{\"scale\":2}}"), 100, 200, size, 0, out _));
    }
    #endregion
    #region Private
    /// <summary>Resolves real asset inputs for matrix tests.</summary>
    private static ToolHeadPresentationProperties Settings(string json)
    {
        var item = new Item { Attributes = new JsonObject(new JObject { ["ve-item-icon-properties"] = JObject.Parse(json) }) };
        return new ToolHeadPresentationResolver((_, reason) => Assert.Fail(reason)).Resolve(item).Properties!;
    }
    /// <summary>Transforms a point using column-vector indexing rather than production matrix helpers.</summary>
    private static void AssertPoint(float[] m, float x, float y, float z, float ex, float ey, float ez)
    {
        Assert.InRange(Math.Abs(ex - (m[0] * x + m[4] * y + m[8] * z + m[12])), 0, .001f);
        Assert.InRange(Math.Abs(ey - (m[1] * x + m[5] * y + m[9] * z + m[13])), 0, .001f);
        Assert.InRange(Math.Abs(ez - (m[2] * x + m[6] * y + m[10] * z + m[14])), 0, .001f);
    }
    #endregion
}
