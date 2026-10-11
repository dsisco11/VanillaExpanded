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
    #endregion
}
