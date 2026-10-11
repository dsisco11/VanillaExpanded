using System.Text.Json.Nodes;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Checks the shared numeric ordering namespace consumed by ConfigLib's block combiner.</summary>
[Trait("Category", "Unit")]
public sealed class ConfigLibOrderingTests
{
    #region Public API
    /// <summary>All setting categories and formatting blocks can be combined without duplicate numeric keys.</summary>
    [Fact]
    public void AssetWeightsAreUniqueAcrossSettingsAndFormatting()
    {
        var asset = ReadAsset();
        var blocks = CombineBlocks(asset);
        int settingCount = asset["settings"]!.AsObject().Sum(category => category.Value!.AsObject().Count);
        Assert.Equal(settingCount + asset["formatting"]!.AsArray().Count, blocks.Count);
        Assert.Contains("boolean/EnableBodyTemperatureOverlay", blocks.Values);
        Assert.Contains("integer/HeldItemStatusAnchor", blocks.Values);
        Assert.Contains("formatting/vanillaexpanded:config-section-hud-overlays", blocks.Values);
    }

    /// <summary>Restoring either original cross-category collision reproduces the runtime sorted-key rejection.</summary>
    [Theory]
    [InlineData("integer", "HeldItemStatusAnchor", 10.3)]
    [InlineData("float", "HeldItemStatusOffsetX", 10.4)]
    public void OriginalHudOrderingCollisionsAreRejected(string category, string setting, double oldWeight)
    {
        var asset = ReadAsset();
        asset["settings"]![category]![setting]!["weight"] = oldWeight;
        Assert.Throws<ArgumentException>(() => CombineBlocks(asset));
    }

    /// <summary>Formatting participates in the same ordering namespace as settings rather than a separate category.</summary>
    [Fact]
    public void FormattingCollisionWithSettingIsRejected()
    {
        var asset = ReadAsset();
        asset["formatting"]![0]!["weight"] = asset["settings"]!["boolean"]!["EnableHudOverlays"]!["weight"]!.DeepClone();
        Assert.Throws<ArgumentException>(() => CombineBlocks(asset));
    }
    #endregion

    #region Private
    /// <summary>Reads the repository asset directly using the established solution-root discovery convention.</summary>
    private static JsonObject ReadAsset()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "VanillaExpanded.sln"))) directory = directory.Parent;
        string repository = directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the VanillaExpanded repository root.");
        return JsonNode.Parse(File.ReadAllText(Path.Combine(repository, "VanillaExpanded/assets/vanillaexpanded/config/configlib-patches.json")))!.AsObject();
    }

    /// <summary>Exercises ConfigLib's sorted-key constraint across every setting type and formatting block.</summary>
    private static SortedDictionary<double, string> CombineBlocks(JsonObject asset)
    {
        var blocks = new SortedDictionary<double, string>();
        // ConfigLib combines typed settings and formatting into one numeric ordering map.
        foreach (var category in asset["settings"]!.AsObject())
            foreach (var setting in category.Value!.AsObject())
                blocks.Add(setting.Value!["weight"]!.GetValue<double>(), category.Key + "/" + setting.Key);
        foreach (var formatting in asset["formatting"]!.AsArray())
            blocks.Add(formatting!["weight"]!.GetValue<double>(), "formatting/" + formatting["title"]!.GetValue<string>());
        return blocks;
    }
    #endregion
}
