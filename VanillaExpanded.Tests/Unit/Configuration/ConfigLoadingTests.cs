using Moq;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class ConfigLoadingTests
{
    [Fact]
    public void LoadConfig_ExistingConfig_ReturnsExistingInstanceWithoutPersisting()
    {
        var existing = new VanillaExpandedConfig { EnableAutoStash = false };
        var api = CreateApi();
        api.Setup(coreApi => coreApi.LoadModConfig<VanillaExpandedConfig>(Constants.ConfigFileName))
            .Returns(existing);

        VanillaExpandedConfig result = VanillaExpandedModSystem.LoadConfig(api.Object);

        Assert.Same(existing, result);
        api.Verify(
            coreApi => coreApi.StoreModConfig(It.IsAny<VanillaExpandedConfig>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public void LoadConfig_MissingConfig_CreatesAndPersistsDefaults()
    {
        var api = CreateApi();
        api.Setup(coreApi => coreApi.LoadModConfig<VanillaExpandedConfig>(Constants.ConfigFileName))
            .Returns((VanillaExpandedConfig)null!);

        VanillaExpandedConfig result = VanillaExpandedModSystem.LoadConfig(api.Object);

        Assert.True(result.EnableAutoStash);
        Assert.True(result.EnableIgnitionTools);
        Assert.True(result.EnableSpawnDecal);
        api.Verify(
            coreApi => coreApi.StoreModConfig(result, Constants.ConfigFileName),
            Times.Once);
    }

    [Fact]
    public void LoadConfig_LoadThrows_ReturnsDefaultsWithoutPersisting()
    {
        var api = CreateApi();
        api.Setup(coreApi => coreApi.LoadModConfig<VanillaExpandedConfig>(Constants.ConfigFileName))
            .Throws(new InvalidOperationException("invalid config"));

        VanillaExpandedConfig result = VanillaExpandedModSystem.LoadConfig(api.Object);

        Assert.True(result.EnableAutoStash);
        Assert.True(result.EnableIgnitionTools);
        Assert.True(result.EnableSpawnDecal);
        api.Verify(
            coreApi => coreApi.StoreModConfig(It.IsAny<VanillaExpandedConfig>(), It.IsAny<string>()),
            Times.Never);
    }

    private static Mock<ICoreAPI> CreateApi()
    {
        var api = new Mock<ICoreAPI>();
        api.Setup(coreApi => coreApi.Logger).Returns(Mock.Of<ILogger>());
        return api;
    }
}
