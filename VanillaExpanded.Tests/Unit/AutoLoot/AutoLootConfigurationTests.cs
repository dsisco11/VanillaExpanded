using Moq;
using Newtonsoft.Json;
using VanillaExpanded.ModSystems;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootConfigurationTests
{
    [Fact]
    public void PersistedSettings_RoundTripAndOldFilesReceiveAutoLootDefaults()
    {
        var config = JsonConvert.DeserializeObject<VanillaExpandedConfig>("{\"EnableAutoStash\":false}")!;
        Assert.True(config.EnableAutoLoot);
        Assert.Equal(0.5f, config.AutoLootDelay);
        config.EnableAutoLoot = false;
        config.AutoLootDelay = 1.2f;
        var restored = JsonConvert.DeserializeObject<VanillaExpandedConfig>(JsonConvert.SerializeObject(config))!;
        Assert.False(restored.EnableAutoLoot);
        Assert.False(restored.EnableAutoStash);
        Assert.Equal(1.2f, restored.AutoLootDelay);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void ConfigLibDelayUpdate_ControlsExactCompletionThreshold(float delay)
    {
        using var test = new AutoLootTestCase();
        Assert.Equal(1, ConfigLibIntegrationModSystem.ApplyConfigLibSettings(VanillaExpandedModSystem.Config,
            new TreeAttribute
            {
                ["MappingKey"] = new StringAttribute(nameof(VanillaExpandedConfig.AutoLootDelay)),
                ["Value"] = new FloatAttribute(delay)
            }));
        test.Begin();
        float beforeCompletion = MathF.BitDecrement(delay);
        test.Tick(beforeCompletion);
        Assert.Empty(test.Sent);
        test.Tick(delay - beforeCompletion);
        Assert.Single(test.Sent);
    }

    [Theory]
    [InlineData("configlib:vanillaexpanded:setting-changed", false)]
    [InlineData("configlib:vanillaexpanded:config-saved", false)]
    [InlineData("configlib:vanillaexpanded:setting-loaded", false)]
    [InlineData("configlib:config-reload", false)]
    [InlineData("configlib:vanillaexpanded:setting-changed", true)]
    [InlineData("configlib:vanillaexpanded:config-saved", true)]
    [InlineData("configlib:vanillaexpanded:setting-loaded", true)]
    [InlineData("configlib:config-reload", true)]
    public void ConfigLibEvent_DisablesImmediatelyAndReenablesWithoutAffectingAutoStash(string eventName, bool stashEnabled)
    {
        using var test = new AutoLootTestCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        var integration = new ConfigLibIntegrationModSystem();
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.Systems).Returns([system]);
        test.Fixture.ApiMock.Setup(api => api.ModLoader).Returns(loader.Object);
        test.Fixture.ApiMock.Setup(api => api.Event).Returns(test.Events.Object);
        var listeners = new Dictionary<string, EventBusListenerDelegate>();
        test.Events.Setup(events => events.RegisterEventBusListener(It.IsAny<EventBusListenerDelegate>(),
                It.IsAny<double>(), It.IsAny<string>()))
            .Callback<EventBusListenerDelegate, double, string>((listener, _, filter) => listeners.Add(filter, listener));
        VanillaExpandedModSystem.Config.EnableAutoStash = stashEnabled;
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            integration.Start(test.Fixture.Api);
            system.Client!.OnInteract(test.Behavior, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.25f);
            EnumHandling handling = EnumHandling.PassThrough;
            listeners[eventName](eventName, ref handling, new StringAttribute("{\"EnableAutoLoot\":\"false\"}"));
            Assert.Equal(EnumHandling.PassThrough, handling);
            Assert.False(VanillaExpandedModSystem.Config.EnableAutoLoot);
            Assert.Equal(stashEnabled, VanillaExpandedModSystem.Config.EnableAutoStash);
            test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
            test.Tick(1);
            Assert.Empty(test.Sent);
            listeners[eventName](eventName, ref handling, new StringAttribute("{\"EnableAutoLoot\":\"true\"}"));
            test.Release();
            system.Client.OnInteract(test.Behavior, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.5f);
            Assert.Single(test.Sent);
            Assert.Equal(stashEnabled, VanillaExpandedModSystem.Config.EnableAutoStash);
        }
        finally
        {
            integration.Dispose();
            system.Dispose();
        }
    }

}
