using VanillaExpanded.ModSystems;
using VanillaExpanded.Tests;

using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.Configuration;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
public class ConfigLibIntegrationTests
{
    [Fact]
    public void ApplyConfigLibSettings_RemovedQuickToolsToggle_IsIgnored()
    {
        var config = new VanillaExpandedConfig();
        var data = new StringAttribute("{\"EnableQuickTools\":\"false\"}");

        int applied = ConfigLibIntegrationModSystem.ApplyConfigLibSettings(config, data);

        Assert.Equal(0, applied);
        Assert.Null(typeof(VanillaExpandedConfig).GetProperty("EnableQuickTools"));
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
}
