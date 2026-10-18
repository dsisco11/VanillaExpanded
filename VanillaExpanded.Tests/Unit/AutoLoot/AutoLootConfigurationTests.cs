using System.Text.Json;
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
    public void Settings_AreLocalGroupedLocalizedAndMatchPersistedDefaults()
    {
        string assets = Path.Combine(FindRoot(), "VanillaExpanded", "assets", "vanillaexpanded");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "config", "configlib-patches.json")));
        using var language = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "lang", "en.json")));
        JsonElement settings = document.RootElement.GetProperty("settings");
        JsonElement enabled = settings.GetProperty("boolean").GetProperty("EnableAutoLoot");
        JsonElement delay = settings.GetProperty("float").GetProperty("AutoLootDelay");
        var defaults = new VanillaExpandedConfig();
        Assert.True(defaults.EnableAutoLoot);
        Assert.Equal(0.5f, defaults.AutoLootDelay);
        Assert.Equal(defaults.EnableAutoLoot, enabled.GetProperty("default").GetBoolean());
        Assert.Equal(defaults.AutoLootDelay, delay.GetProperty("default").GetSingle());
        JsonElement range = delay.GetProperty("range");
        Assert.Equal(0.1f, range.GetProperty("min").GetSingle());
        Assert.Equal(2f, range.GetProperty("max").GetSingle());
        Assert.Equal(0.1f, range.GetProperty("step").GetSingle());
        JsonElement heading = document.RootElement.GetProperty("formatting").EnumerateArray().Single(entry =>
            entry.GetProperty("title").GetString() == "vanillaexpanded:config-section-auto-loot");
        float weight = heading.GetProperty("weight").GetSingle();
        foreach (JsonElement setting in new[] { enabled, delay })
        {
            Assert.True(setting.GetProperty("clientSide").GetBoolean());
            Assert.InRange(setting.GetProperty("weight").GetSingle(), weight + 0.01f, 1.99f);
            foreach (string field in new[] { "ingui", "comment" })
            {
                AssertLocalized(setting.GetProperty(field).GetString()!, language);
            }
        }
        AssertLocalized(heading.GetProperty("title").GetString()!, language);
        AssertLocalized("vanillaexpanded:autoloot-progress", language);
        AssertLocalized("vanillaexpanded:entityhelp-autoloot", language);
        Assert.Equal(2, settings.EnumerateObject().SelectMany(category => category.Value.EnumerateObject())
            .Count(setting => setting.Name.Contains("AutoLoot", StringComparison.Ordinal)));
    }

    [Fact]
    public void Manifest_RemainsUniversalWithNoServerOrConfigLibDependency()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRoot(), "VanillaExpanded", "modinfo.json")));
        Assert.Equal("Universal", document.RootElement.GetProperty("side").GetString());
        Assert.False(document.RootElement.GetProperty("requiredOnServer").GetBoolean());
        Assert.False(document.RootElement.GetProperty("dependencies").TryGetProperty("configlib", out _));
    }

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

    private static void AssertLocalized(string code, JsonDocument language)
        => Assert.False(string.IsNullOrWhiteSpace(language.RootElement.GetProperty(code.Split(':')[1]).GetString()));

    private static string FindRoot()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        return root?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
