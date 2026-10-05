using Moq;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.Configuration;

/// <summary>Verifies optional ConfigLib restoration through its public lifecycle contract.</summary>
[Trait("Category", "Unit")]
public sealed class ConfigLibSavedSelectionRestoreTests
{
    #region Public API
    /// <summary>Restores only our saved selection after initialization and detaches at disposal.</summary>
    [Fact]
    public void Loaded_RestoresOurSelectionAndDisposalDetaches()
    {
        var config = new VanillaExpandedConfig();
        var saved = new PublicConfig(() => config.PerishableItemFreshnessIndicatorStyle = "freshness-outline");
        var unrelated = new PublicConfig(() => throw new InvalidOperationException("Unrelated configuration must not reload."));
        var provider = new PublicProvider(new() { [Constants.ModId] = saved, ["other"] = unrelated });
        var api = CreateApi(provider);
        using var restore = ConfigLibSavedSelectionRestore.Attach(api.Object);
        Assert.NotNull(restore);
        Assert.Equal(0, saved.Reloads);
        Assert.Equal(ItemSlotIndicatorRenderingStyle.SlotBackground, config.FreshnessRenderingStyle);

        provider.RaiseLoaded();
        Assert.Equal(1, saved.Reloads);
        Assert.Equal(ItemSlotIndicatorRenderingStyle.SlotOutline, config.FreshnessRenderingStyle);
        Assert.Equal(0, unrelated.Reloads);
        Assert.Equal([Constants.ModId], provider.RequestedDomains);

        restore.Dispose();
        provider.RaiseLoaded();
        Assert.Equal(1, saved.Reloads);
    }

    /// <summary>Leaves installations without ConfigLib untouched.</summary>
    [Fact]
    public void AbsentProvider_DoesNotAttach()
    {
        var api = new Mock<ICoreAPI>();
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        var loader = new Mock<IModLoader>();
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        Assert.Null(ConfigLibSavedSelectionRestore.Attach(api.Object));
        loader.Verify(value => value.GetModSystem(It.IsAny<string>()), Times.Never);
    }

    /// <summary>Contains public reload failures and reports them through the owning logger.</summary>
    [Fact]
    public void ReloadFailure_IsLoggedWithoutEscapingLoadedEvent()
    {
        var provider = new PublicProvider(new() { [Constants.ModId] = new PublicConfig(() => throw new InvalidOperationException("reload failed")) });
        var api = CreateApi(provider);
        var logger = new Mock<ILogger>();
        api.SetupGet(value => value.Logger).Returns(logger.Object);
        using var restore = ConfigLibSavedSelectionRestore.Attach(api.Object);
        provider.RaiseLoaded();
        logger.Verify(value => value.Warning(It.Is<string>(message => message.Contains("restore saved ConfigLib selections")), It.Is<object[]>(args => args.Length == 1 && Equals(args[0], "reload failed"))), Times.Once);
    }
    /// <summary>Coalesces startup loads and restores the latest configuration after client networking becomes ready.</summary>
    [Fact]
    public void ClientStartup_DefersAndCoalescesUntilFinalize()
    {
        var first = new PublicConfig(() => { });
        var restored = new VanillaExpandedConfig();
        var latest = new PublicConfig(() => restored.PerishableItemFreshnessIndicatorStyle = "freshness-outline");
        var configs = new Dictionary<string, PublicConfig> { [Constants.ModId] = first };
        var provider = new PublicProvider(configs);
        var (api, events) = CreateClientApi(provider, false);
        using var restore = ConfigLibSavedSelectionRestore.Attach(api.Object);
        provider.RaiseLoaded();
        configs[Constants.ModId] = latest;
        provider.RaiseLoaded();
        Assert.Empty(provider.RequestedDomains);
        Assert.Equal(0, first.Reloads);
        Assert.Equal(0, latest.Reloads);
        events.Raise(value => value.LevelFinalize += null);
        Assert.Equal(1, latest.Reloads);
        Assert.Equal(ItemSlotIndicatorRenderingStyle.SlotOutline, restored.FreshnessRenderingStyle);
        events.Raise(value => value.LevelFinalize += null);
        Assert.Equal(1, latest.Reloads);
        provider.RaiseLoaded();
        Assert.Equal(2, latest.Reloads);
    }

    /// <summary>Clears pending client restoration and both subscriptions at disposal.</summary>
    [Fact]
    public void ClientDisposal_CancelsPendingRestore()
    {
        var saved = new PublicConfig(() => { });
        var provider = new PublicProvider(new() { [Constants.ModId] = saved });
        var (api, events) = CreateClientApi(provider, false);
        var restore = ConfigLibSavedSelectionRestore.Attach(api.Object)!;
        provider.RaiseLoaded();
        restore.Dispose();
        events.Raise(value => value.LevelFinalize += null);
        provider.RaiseLoaded();
        Assert.Equal(0, saved.Reloads);
    }

    /// <summary>Restores immediately when attaching after player readiness has already fired.</summary>
    [Fact]
    public void ReadyClient_LoadedRestoresImmediately()
    {
        var saved = new PublicConfig(() => { });
        var provider = new PublicProvider(new() { [Constants.ModId] = saved });
        var (api, _) = CreateClientApi(provider, true);
        using var restore = ConfigLibSavedSelectionRestore.Attach(api.Object);
        provider.RaiseLoaded();
        Assert.Equal(1, saved.Reloads);
    }
    #endregion

    #region Private
    /// <summary>Creates the optional loader boundary without adding a ConfigLib assembly dependency.</summary>
    private static Mock<ICoreAPI> CreateApi(PublicProvider provider)
    {
        var api = new Mock<ICoreAPI>();
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.IsModEnabled("configlib")).Returns(true);
        loader.Setup(value => value.GetModSystem("ConfigLib.ConfigLibModSystem")).Returns(provider);
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        return api;
    }
    /// <summary>Creates the client's readiness and finalize-event boundaries.</summary>
    private static (Mock<ICoreClientAPI> Api, Mock<IClientEventAPI> Events) CreateClientApi(PublicProvider provider, bool ready)
    {
        var api = new Mock<ICoreClientAPI>();
        var loader = new Mock<IModLoader>();
        var events = new Mock<IClientEventAPI>();
        loader.Setup(value => value.IsModEnabled("configlib")).Returns(true);
        loader.Setup(value => value.GetModSystem("ConfigLib.ConfigLibModSystem")).Returns(provider);
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        api.SetupGet(value => value.Event).Returns(events.Object);
        api.SetupGet(value => value.PlayerReadyFired).Returns(ready);
        return (api, events);
    }
    #endregion

    /// <summary>Models ConfigLib's public loaded-event and domain lookup surface.</summary>
    public sealed class PublicProvider(Dictionary<string, PublicConfig> configs) : ModSystem
    {
        /// <summary>Signals that configuration construction and synchronization have completed.</summary>
        public event Action? ConfigsLoaded;
        /// <summary>Records domains requested by the bridge.</summary>
        public List<string> RequestedDomains { get; } = [];
        #region Public API
        /// <summary>Returns the public configuration object for the requested mod.</summary>
        public PublicConfig? GetConfig(string domain)
        {
            RequestedDomains.Add(domain);
            return configs.GetValueOrDefault(domain);
        }
        /// <summary>Dispatches the public initialization notification.</summary>
        public void RaiseLoaded() => ConfigsLoaded?.Invoke();
        #endregion
    }

    /// <summary>Models ConfigLib's public saved-file reload surface.</summary>
    public sealed class PublicConfig(Action applySavedValue)
    {
        /// <summary>Counts reloads to establish initialization and disposal ordering.</summary>
        public int Reloads { get; private set; }
        #region Public API
        /// <summary>Applies the fixture's saved configuration and reports successful reload.</summary>
        public bool TryReadFromFile()
        {
            Reloads++;
            applySavedValue();
            return true;
        }
        #endregion
    }
}




