using System.Numerics;
using Newtonsoft.Json.Linq;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Verifies authored presentation ownership, engine deserialization, and bounded fallback diagnostics.</summary>
[Trait("Category", "Unit")]
public sealed class ToolHeadPresentationResolverTests
{
    #region Public API
    #region Valid settings
    /// <summary>Checks independent identity defaults and omitted versus explicit wedge zero.</summary>
    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"transform\":{}}", null)]
    [InlineData("{\"wedgeRotationDegrees\":0}", 0f)]
    public void IdentityDefaultsAreIndependent(string json, float? angle)
    {
        Item item = ItemWith(JObject.Parse(json));
        item.GuiTransform = ModelTransform.ItemDefaultGui();
        item.GuiTransform.Scale = 42;
        var settings = Valid(item);
        var t = settings.CreateModelTransform();
        Assert.Equal((0f, 0f, 0f), (t.Rotation.X, t.Rotation.Y, t.Rotation.Z));
        Assert.Equal((0f, 0f, 0f), (t.Translation.X, t.Translation.Y, t.Translation.Z));
        Assert.Equal((0.5f, 0.5f, 0.5f), (t.Origin.X, t.Origin.Y, t.Origin.Z));
        Assert.Equal((1f, 1f, 1f), (t.ScaleXYZ.X, t.ScaleXYZ.Y, t.ScaleXYZ.Z));
        Assert.False(t.Rotate);
        Assert.Equal(angle, settings.WedgeRotationDegrees);
        Assert.Equal(42, item.GuiTransform.ScaleXYZ.X);
    }

    /// <summary>Checks authored values, partial defaults, and asset ownership instead of stack data.</summary>
    [Theory]
    [InlineData("{\"transform\":{\"rotation\":{\"x\":15,\"y\":-30,\"z\":270},\"translation\":{\"x\":0.25,\"y\":-0.5,\"z\":2},\"scale\":3},\"wedgeRotationDegrees\":-45}", 15f, -30f, 270f, 0.25f, -0.5f, 2f, 3f)]
    [InlineData("{\"transform\":{\"rotation\":{\"y\":12},\"translation\":{\"z\":-2}},\"wedgeRotationDegrees\":-45}", 0f, 12f, 0f, 0f, 0f, -2f, 1f)]
    public void AuthoredValuesComeFromCollectible(string json, float rx, float ry, float rz, float tx, float ty, float tz, float scale)
    {
        Item item = ItemWith(JObject.Parse(json));
        var stack = new ItemStack(item);
        stack.Attributes.SetString("ve-item-icon-properties", "invalid stack override");
        var settings = Valid(stack.Collectible);
        var t = settings.CreateModelTransform();
        Assert.Equal((rx, ry, rz), (t.Rotation.X, t.Rotation.Y, t.Rotation.Z));
        Assert.Equal((tx, ty, tz), (t.Translation.X, t.Translation.Y, t.Translation.Z));
        Assert.Equal((scale, scale, scale), (t.ScaleXYZ.X, t.ScaleXYZ.Y, t.ScaleXYZ.Z));
        Assert.Equal(-45f, settings.WedgeRotationDegrees);
    }

    /// <summary>Preserves explicitly authored values matching the engine's missing-vector sentinel.</summary>
    [Fact]
    public void AuthoredSentinelValuesAreNotReplacedByDefaults()
    {
        var settings = Valid(ItemWith(JObject.Parse("{\"transform\":{\"rotation\":{\"x\":-0.000099,\"y\":-0.000099,\"z\":-0.000099},\"translation\":{\"x\":-0.000099,\"y\":-0.000099,\"z\":-0.000099}}}")));
        var t = settings.CreateModelTransform();
        Assert.Equal((-0.000099f, -0.000099f, -0.000099f), (t.Rotation.X, t.Rotation.Y, t.Rotation.Z));
        Assert.Equal((-0.000099f, -0.000099f, -0.000099f), (t.Translation.X, t.Translation.Y, t.Translation.Z));
    }
    /// <summary>Checks immutable snapshots while permitting fresh resolutions after asset correction.</summary>
    [Fact]
    public void SourceAndCallerChangesCannotMutateSnapshot()
    {
        Item item = ItemWith(JObject.Parse("{\"transform\":{\"rotation\":{\"x\":25},\"scale\":2}}"));
        var settings = Valid(item);
        item.Attributes.Token!["ve-item-icon-properties"]!["transform"]!["rotation"]!["x"] = 99;
        var caller = settings.CreateModelTransform();
        caller.Rotation.X = -10;
        caller.Translation.Y = 100;
        caller.Origin.X = 0;
        caller.Scale = 10;
        var preserved = settings.CreateModelTransform();
        Assert.Equal(25, preserved.Rotation.X);
        Assert.Equal(0, preserved.Translation.Y);
        Assert.Equal(0.5f, preserved.Origin.X);
        Assert.Equal(2, preserved.ScaleXYZ.X);
        Assert.Equal(99, Valid(item).CreateModelTransform().Rotation.X);
    }

    #endregion
    #region Fallback and diagnostics
    /// <summary>Checks missing assets and argument contracts.</summary>
    [Fact]
    public void MissingAndNullArgumentsHaveExplicitContracts()
    {
        var resolver = new ToolHeadPresentationResolver((_, reason) => Assert.Fail(reason));
        foreach (Item item in new[] { new Item(), new Item { Attributes = new JsonObject(new JObject()) } })
        {
            var result = resolver.Resolve(item);
            Assert.Equal(ToolHeadPresentationStatus.Missing, result.Status);
            Assert.Null(result.Properties);
        }
        Assert.Throws<ArgumentNullException>(() => new ToolHeadPresentationResolver(null!));
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null!));
    }

    /// <summary>Checks malformed supplied values select fallback with a meaningful warning.</summary>
    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void InvalidSettingsSelectFallback(JToken token)
    {
        int warnings = 0;
        var resolver = new ToolHeadPresentationResolver((_, reason) => { Assert.False(string.IsNullOrWhiteSpace(reason)); warnings++; });
        var result = resolver.Resolve(ItemWith(token));
        Assert.Equal(ToolHeadPresentationStatus.Invalid, result.Status);
        Assert.Null(result.Properties);
        Assert.Equal(1, warnings);
    }

    /// <summary>Checks warning identity, repeat frames, reentrancy, and correction without stale caching.</summary>
    [Fact]
    public void DiagnosticsAreBoundedAndCorrectionsReRead()
    {
        Item first = ItemWith(JValue.CreateNull());
        Item second = ItemWith(JValue.CreateNull());
        first.Code = second.Code = new AssetLocation("game:same-code");
        int warnings = 0;
        ToolHeadPresentationResolver? resolver = null;
        resolver = new ToolHeadPresentationResolver((item, _) => { warnings++; Assert.Equal(ToolHeadPresentationStatus.Invalid, resolver!.Resolve(item).Status); });
        for (int i = 0; i < 100; i++) resolver.Resolve(first);
        resolver.Resolve(second);
        Assert.Equal(2, warnings);
        first.Attributes.Token!["ve-item-icon-properties"] = new JObject();
        Assert.Equal(ToolHeadPresentationStatus.Valid, resolver.Resolve(first).Status);
        first.Attributes.Token!["ve-item-icon-properties"] = JValue.CreateNull();
        Assert.Equal(ToolHeadPresentationStatus.Invalid, resolver.Resolve(first).Status);
        Assert.Equal(2, warnings);
    }

    #endregion
    #region Invalid inputs
    /// <summary>Provides input that cannot deserialize or yields unsafe engine transforms.</summary>
    public static IEnumerable<object[]> InvalidConfigurations()
    {
        yield return [JObject.Parse("{\"transform\":{\"rotation\":{\"z\":180},\"translation\":{\"x\":3e38},\"scale\":3e38}}")];
        foreach (string path in new[] { "", "transform", "transform.rotation", "transform.translation", "transform.origin", "transform.scaleXYZ" })
            foreach (JToken value in new JToken[] { new JArray(), new JValue("invalid") }) yield return [AtPath(path, value)];
        foreach (string path in new[] { "", "transform", "transform.rotation", "transform.translation", "transform.origin", "transform.scaleXYZ" }) yield return [AtPath(path, JValue.CreateNull())];
        string[] numbers = ["transform.rotation.x", "transform.rotation.y", "transform.rotation.z", "transform.translation.x", "transform.translation.y", "transform.translation.z", "transform.origin.x", "transform.origin.y", "transform.origin.z", "transform.scale", "transform.scaleXYZ.x", "transform.scaleXYZ.y", "transform.scaleXYZ.z", "wedgeRotationDegrees"];
        // Exercise every rendering numeric family, including nonuniform scales and authored pivot values.
        foreach (string path in numbers)
            foreach (JToken value in new JToken[] { new JObject(), new JArray(), new JValue("invalid"), new JValue(double.NaN), new JValue(double.PositiveInfinity), new JValue(double.NegativeInfinity), new JValue(double.MaxValue), new JValue(BigInteger.Pow(10, 400)) }) yield return [AtPath(path, value)];
        foreach (string path in new[] { "transform.scale", "transform.scaleXYZ.x", "transform.scaleXYZ.y", "transform.scaleXYZ.z" })
            foreach (double scale in new[] { 0d, -1d, double.Epsilon }) yield return [AtPath(path, new JValue(scale))];
    }

    /// <summary>Allows engine coercion and unknown fields rather than imposing a duplicate JSON schema.</summary>
    [Fact]
    public void EngineFieldsAndCoercionsArePreserved()
    {
        var settings = Valid(ItemWith(JObject.Parse("{\"unknown\":true,\"transform\":{\"unknown\":{},\"rotation\":{\"x\":\"15\"},\"translation\":{\"y\":\"0.25\"},\"origin\":{\"x\":0.2,\"y\":0.3,\"z\":0.4},\"scaleXYZ\":{\"x\":2,\"y\":3,\"z\":4},\"rotate\":true},\"wedgeRotationDegrees\":\"45\"}")));
        var t = settings.CreateModelTransform();
        Assert.Equal(15, t.Rotation.X);
        Assert.Equal(0.25f, t.Translation.Y);
        Assert.Equal((0.2f, 0.3f, 0.4f), (t.Origin.X, t.Origin.Y, t.Origin.Z));
        Assert.Equal((2f, 3f, 4f), (t.ScaleXYZ.X, t.ScaleXYZ.Y, t.ScaleXYZ.Z));
        Assert.True(t.Rotate);
        Assert.Equal(45f, settings.WedgeRotationDegrees);
    }
    /// <summary>Preserves positive representable scale extremes without an undocumented tuning clamp.</summary>
    [Theory]
    [InlineData(1e-30)]
    [InlineData(3e38)]
    public void PositiveFiniteScalesArePreserved(double scale)
    {
        Assert.Equal((float)scale, Valid(ItemWith(AtPath("transform.scale", new JValue(scale)))).CreateModelTransform().ScaleXYZ.X);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates a collectible with dedicated asset properties.</summary>
    private static Item ItemWith(JToken token) => new() { Attributes = new JsonObject(new JObject { ["ve-item-icon-properties"] = token }) };

    /// <summary>Checks successful resolution without warning and returns owned settings.</summary>
    private static ToolHeadPresentationProperties Valid(CollectibleObject item)
    {
        var result = new ToolHeadPresentationResolver((_, reason) => Assert.Fail(reason)).Resolve(item);
        Assert.Equal(ToolHeadPresentationStatus.Valid, result.Status);
        return Assert.IsType<ToolHeadPresentationProperties>(result.Properties);
    }

    /// <summary>Builds nested test data around a single supplied field.</summary>
    private static JToken AtPath(string path, JToken value)
    {
        if (path.Length == 0) return value.DeepClone();
        var root = new JObject();
        JObject current = root;
        string[] segments = path.Split('.');
        foreach (string segment in segments[..^1])
        {
            var child = new JObject();
            current[segment] = child;
            current = child;
        }
        current[segments[^1]] = value.DeepClone();
        return root;
    }
    #endregion
}






