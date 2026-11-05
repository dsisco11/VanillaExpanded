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
        var item = new Item { Attributes = new JsonObject(new JObject { ["ve-radial-menu-properties"] = JObject.Parse(json) }) };
        return new ToolHeadPresentationResolver((_, reason) => Assert.Fail(reason)).Resolve(item).Properties!;
    }
    #endregion
}
