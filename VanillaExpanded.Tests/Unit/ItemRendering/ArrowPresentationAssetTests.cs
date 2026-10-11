using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Checks authored arrow patches against the installed model hierarchy on the CPU.</summary>
public sealed class ArrowPresentationAssetTests
{
    #region Public API
    /// <summary>The installed bone arrow's minimal tip hierarchy accounts for every parent rotation and keeps its tail outside the icon.</summary>
    [Fact]
    public void BoneTipFitsAuthoredHeadPresentation()
    {
        var shape = JsonConvert.DeserializeObject<Shape>("{\"elements\":[{\"name\":\"origin\",\"from\":[8,0,8],\"to\":[8,0,8],\"rotationOrigin\":[8,0,8],\"children\":[{\"name\":\"main\",\"from\":[-9,0,-0.5],\"to\":[-5,1,0.5],\"rotationOrigin\":[-5,0,0],\"children\":[{\"name\":\"spine1\",\"from\":[4,0,0],\"to\":[8,1,1],\"rotationOrigin\":[4,0,0.5],\"rotationX\":0.1,\"rotationZ\":8,\"children\":[{\"name\":\"spine2\",\"from\":[4,0,0],\"to\":[8,1,1],\"rotationOrigin\":[4,1,0.5],\"rotationX\":-0.2,\"rotationZ\":-6,\"children\":[{\"name\":\"spine3\",\"from\":[4,0,0],\"to\":[7,1,1],\"rotationOrigin\":[4,1,0.5],\"rotationX\":0.1,\"rotationZ\":-14,\"children\":[{\"name\":\"spine4\",\"from\":[3,0,0],\"to\":[6,1,1],\"rotationOrigin\":[3,0,0.5],\"rotationX\":-0.1,\"rotationZ\":11,\"children\":[{\"name\":\"tip1\",\"from\":[2.9,0.2,0.2],\"to\":[4.9,0.8,0.8],\"rotationOrigin\":[2.9,0.5,0.5],\"rotationX\":-36,\"children\":[{\"name\":\"tip2\",\"from\":[2,0.1,0.1],\"to\":[4,0.5,0.5],\"rotationOrigin\":[2,0.3,0.3],\"rotationX\":-14}]}]}]}]}]}]}]}]}")!;
        var heads = Vertices(shape.Elements, Mat4f.Create()).Where(vertex => vertex.Name.StartsWith("tip")).Select(vertex => vertex.Point).ToArray();
        var center = Enumerable.Range(0, 3).Select(axis => (heads.Min(point => point[axis]) + heads.Max(point => point[axis])) / 2).ToArray();
        // Match the packaged asset as it is selected by the engine's ByType expansion.
        var patch = JArray.Parse(File.ReadAllText(FindPatch()));
        var item = new Item { Attributes = new JsonObject(new JObject { ["ve-radial-menu-properties"] = patch[0]!["value"]!["arrow-bone"]!.DeepClone() }) };
        var properties = new ToolHeadPresentationResolver((_, reason) => Assert.Fail(reason)).Resolve(item).Properties!;
        Assert.True(ToolHeadPresentationMatrix.TryCreate(properties, 0, 0, 24, 0, out var presentation, 50));
        var drawnCenter = Transform(presentation, center);
        Assert.InRange(Math.Abs(drawnCenter[0]), 0, .001f);
        Assert.InRange(Math.Abs(drawnCenter[1]), 0, .001f);
        var tail = Vertices(shape.Elements, Mat4f.Create()).First(vertex => vertex.Name == "main").Point;
        Assert.True(Math.Abs(Transform(presentation, tail)[1]) > 12);
        var drawn = heads.Select(point => Transform(presentation, point)).ToArray();
        Assert.True(drawn.All(point => Math.Abs(point[0]) <= 12 && Math.Abs(point[1]) <= 12),
            $"Bone center {string.Join(',', center)}; drawn X {drawn.Min(p=>p[0])}..{drawn.Max(p=>p[0])}, Y {drawn.Min(p=>p[1])}..{drawn.Max(p=>p[1])}");
    }
    /// <summary>Stone and metal heads face upward while the opposite shaft end lies outside the icon slot.</summary>
    [Theory]
    [InlineData("*", 1.0375f, .03125f, .5125f, 1.1875f, .5f)]
    [InlineData("arrow-erel", -.4f, .03125f, .5f, -.5f, .6f)]
    public void HeadPivotsFaceUpAndExcludeTail(string variant, float x, float y, float z, float tipX, float tailX)
    {
        var patch = JArray.Parse(File.ReadAllText(FindPatch()));
        var item = new Item { Attributes = new JsonObject(new JObject { ["ve-radial-menu-properties"] = patch[0]!["value"]![variant]!.DeepClone() }) };
        var properties = new ToolHeadPresentationResolver((_, reason) => Assert.Fail(reason)).Resolve(item).Properties!;
        Assert.True(ToolHeadPresentationMatrix.TryCreate(properties, 0, 0, 24, 0, out var presentation, 50));
        var center = Transform(presentation, [x, y, z]);
        Assert.InRange(Math.Abs(center[0]), 0, .001f);
        Assert.InRange(Math.Abs(center[1]), 0, .001f);
        Assert.InRange(Math.Abs(center[2] - 50), 0, .001f);
        Assert.True(Transform(presentation, [tipX, y, z])[1] < 0);
        Assert.True(Transform(presentation, [tailX, y, z])[1] > 12);
    }
    #endregion
    #region Private
    /// <summary>Locates the packaged patch from any test output directory.</summary>
    private static string FindPatch()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "VanillaExpanded/assets/vanillaexpanded/patches/toolheadpresentation/arrow.json");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Arrow presentation patch");
    }
    /// <summary>Composes native element matrices and emits local cuboid corners in world model units.</summary>
    private static IEnumerable<(string Name, float[] Point)> Vertices(ShapeElement[] elements, float[] parent)
    {
        foreach (var element in elements)
        {
            var matrix = Mat4f.Create();
            Mat4f.Mul(matrix, parent, element.GetLocalTransformMatrix(0));
            for (int corner = 0; corner < 8; corner++)
            {
                var point = Enumerable.Range(0, 3).Select(axis => (corner & (1 << axis)) == 0 ? 0f : (float)((element.To![axis] - element.From![axis]) / 16)).ToArray();
                yield return (element.Name!, Transform(matrix, point));
            }
            if (element.Children != null)
                foreach (var child in Vertices(element.Children, matrix)) yield return child;
        }
    }
    /// <summary>Evaluates model points independently using column-major affine matrix indexing.</summary>
    private static float[] Transform(float[] matrix, float[] point) => Enumerable.Range(0, 3)
        .Select(axis => matrix[axis] * point[0] + matrix[4 + axis] * point[1] + matrix[8 + axis] * point[2] + matrix[12 + axis]).ToArray();
    #endregion
}
