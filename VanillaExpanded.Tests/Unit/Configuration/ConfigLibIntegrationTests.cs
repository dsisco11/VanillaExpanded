using VanillaExpanded.ModSystems;

using System.Reflection;
using System.Text.Json;

using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class ConfigLibIntegrationTests
{
    [Fact]
    public void ConfigLibAsset_DeclaresEveryConfigProperty()
    {
        string repositoryRoot = FindRepositoryRoot();
        string assetPath = Path.Combine(
            repositoryRoot,
            "VanillaExpanded",
            "assets",
            "vanillaexpanded",
            "config",
            "configlib-patches.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetPath));
        JsonElement settings = document.RootElement.GetProperty("settings");
        var declaredSettings = new Dictionary<string, string>();

        foreach (JsonProperty category in settings.EnumerateObject())
        {
            foreach (JsonProperty setting in category.Value.EnumerateObject())
            {
                declaredSettings.Add(setting.Name, setting.Value.GetProperty("code").GetString()!);
            }
        }

        string[] configProperties = typeof(VanillaExpandedConfig)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(configProperties, declaredSettings.Keys.Order());
        Assert.All(declaredSettings, setting => Assert.Equal(setting.Key, setting.Value));
    }

    [Fact]
    public void ApplyConfigLibSettings_SingleTreeUpdate_AppliesBoolean()
    {
        var config = new VanillaExpandedConfig { EnableAutoStash = true };
        var data = new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.EnableAutoStash)),
            ["Value"] = new BoolAttribute(false)
        };

        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, data);

        Assert.Equal(1, applied);
        Assert.False(config.EnableAutoStash);
    }

    [Fact]
    public void ApplyConfigLibSettings_BatchedTreeUpdate_AppliesEveryKnownSetting()
    {
        var config = new VanillaExpandedConfig();
        var settings = new TreeAttribute
        {
            ["autoStash"] = CreateSetting(nameof(VanillaExpandedConfig.EnableAutoStash), "false"),
            ["delay"] = CreateSetting(nameof(VanillaExpandedConfig.AutoStashDelay), "1.25")
        };
        var data = new TreeAttribute { ["Settings"] = settings };

        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, data);

        Assert.Equal(2, applied);
        Assert.False(config.EnableAutoStash);
        Assert.Equal(1.25f, config.AutoStashDelay);
    }

    [Fact]
    public void ApplyConfigLibSettings_StringPayload_AppliesKnownSettings()
    {
        var config = new VanillaExpandedConfig();
        var data = new StringAttribute(
            "{\"EnableIgnitionTools\":\"false\",\"SpawnDecalSize\":\"0.8\"}");

        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, data);

        Assert.Equal(2, applied);
        Assert.False(config.EnableIgnitionTools);
        Assert.Equal(0.8f, config.SpawnDecalSize);
    }

    [Fact]
    public void ApplyConfigLibSettings_UnknownOrInvalidSettings_IgnoresThem()
    {
        var config = new VanillaExpandedConfig();
        var data = new StringAttribute(
            "{\"UnknownSetting\":\"true\",\"AutoStashDelay\":\"not-a-number\"}");

        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, data);

        Assert.Equal(0, applied);
        Assert.Equal(0.5f, config.AutoStashDelay);
    }

    [Fact]
    public void ApplyConfigLibSettings_MalformedPayload_DoesNothing()
    {
        var config = new VanillaExpandedConfig();

        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(
            config,
            new StringAttribute("not-json"));

        Assert.Equal(0, applied);
    }

    private static TreeAttribute CreateSetting(string propertyName, string value)
    {
        return new TreeAttribute
        {
            ["MappingKey"] = new StringAttribute(propertyName),
            ["Value"] = new StringAttribute(value)
        };
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VanillaExpanded.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the VanillaExpanded repository root.");
    }
}
