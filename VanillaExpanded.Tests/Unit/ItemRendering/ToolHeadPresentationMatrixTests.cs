using Newtonsoft.Json.Linq;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Verifies screen placement independently against transformed model points.</summary>
public sealed class ToolHeadPresentationMatrixTests
{
    private static readonly float turnCos = (float)Math.Cos(5 * Math.PI / 180);
    private static readonly float turnSin = (float)Math.Sin(5 * Math.PI / 180);

    #region Public API
    /// <summary>Checks pivot placement, Y inversion, and effective size without additional GUI scaling.</summary>
    [Theory]
    [InlineData(40f)]
    [InlineData(80f)]
    [InlineData(96f)]
    public void IdentityMapsPivotToAnchorAndUsesEffectiveSize(float size)
    {
        Assert.True(ToolHeadPresentationMatrix.TryCreate(Settings("{}"), 123, 234, size, 90, out var m));
        AssertPoint(m, 0.5f, 0.5f, 0.5f, 123, 234, 100);
        AssertPoint(m, 1.5f, 1.5f, 0.5f, 123 + size, 234 - size, 100);
    }

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

    /// <summary>Checks clockwise screen angles around the anchor and authored offset.</summary>
    [Theory]
    [InlineData(0d, 0f, 0f, -40f)]
    [InlineData(90d, 0f, 40f, 0f)]
    [InlineData(180d, 0f, 0f, 40f)]
    [InlineData(270d, 0f, -40f, 0f)]
    [InlineData(0d, 90f, 40f, 0f)]
    public void SuppliedAngleTracksWedge(double wedge, float offset, float dx, float dy)
    {
        Assert.True(ToolHeadPresentationMatrix.TryCreate(Settings("{\"wedgeRotationDegrees\":" + offset + "}"), 100, 200, 40, wedge, out var m));
        AssertPoint(m, 0.5f, 1.5f, 0.5f, 100 + dx, 200 + dy, 100);
    }

    /// <summary>Checks model scale then rotation then translation with pivot preserved.</summary>
    [Fact]
    public void AuthoredTransformUsesDocumentedOrder()
    {
        Assert.True(ToolHeadPresentationMatrix.TryCreate(Settings("{\"transform\":{\"rotation\":{\"z\":90},\"translation\":{\"x\":0.25,\"y\":0.5},\"scale\":2}}"), 100, 200, 40, 0, out var m));
        AssertPoint(m, 0.5f, 0.5f, 0.5f, 110, 180, 100);
        AssertPoint(m, 1.5f, 0.5f, 0.5f, 110, 100, 100);
    }

    /// <summary>Uses the authored pivot and nonuniform engine scales without replacing their semantics.</summary>
    [Fact]
    public void AuthoredPivotAndScaleAxesComposeCorrectly()
    {
        Assert.True(ToolHeadPresentationMatrix.TryCreate(Settings("{\"transform\":{\"origin\":{\"x\":0.2,\"y\":0.3,\"z\":0.4},\"scaleXYZ\":{\"x\":2,\"y\":3,\"z\":4}}}"), 100, 200, 40, 0, out var m));
        AssertPoint(m, 0.2f, 0.3f, 0.4f, 100, 200, 100);
        AssertPoint(m, 1.2f, 1.3f, 1.4f, 180, 80, 260);
    }
    /// <summary>An authored turn exposes thickness while the normal and anchor follow each radial wedge.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(90d)]
    [InlineData(180d)]
    [InlineData(270d)]
    public void AuthoredTurnExposesThicknessAcrossWedges(double wedge)
    {
        Assert.True(ToolHeadPresentationMatrix.TryCreate(Settings("{\"transform\":{\"rotation\":{\"y\":5}},\"wedgeRotationDegrees\":0}"), 100, 200, 40, wedge, out var matrix));
        double angle = wedge * Math.PI / 180;
        AssertPoint(matrix, .5f, .5f, .5f, 100, 200, 100);
        AssertPoint(matrix, .5f, .5f, 1.5f,
            100 + 40 * turnSin * (float)Math.Cos(angle),
            200 + 40 * turnSin * (float)Math.Sin(angle),
            100 + 40 * turnCos);
        double normalLength = Math.Sqrt(matrix[8] * matrix[8] + matrix[9] * matrix[9] + matrix[10] * matrix[10]);
        Assert.Equal(40d, normalLength, 3);
        Assert.True(matrix[10] > 0);
        Assert.True(Math.Abs(matrix[8]) + Math.Abs(matrix[9]) > 3);
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
        var item = new Item { Attributes = new JsonObject(new JObject { ["ve-radial-menu-properties"] = JObject.Parse(json) }) };
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

