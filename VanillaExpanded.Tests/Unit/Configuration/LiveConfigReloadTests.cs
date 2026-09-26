using Moq;

using VanillaExpanded.ModSystems;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.Configuration;

[Trait("Category", "Unit")]
public class LiveConfigReloadTests
{
    [Fact]
    public void NotifyAll_NotifiesEveryLiveConfigurableSystem()
    {
        var first = new RecordingLiveConfigurable();
        var second = new RecordingLiveConfigurable();
        ICoreAPI api = CreateApi(first, new NonLiveSystem(), second);

        LiveConfigReload.NotifyAll(api);

        Assert.Equal(1, first.ReloadCount);
        Assert.Equal(1, second.ReloadCount);
    }

    [Fact]
    public void NotifyAll_WhenSystemThrows_ContinuesNotifyingRemainingSystems()
    {
        var remaining = new RecordingLiveConfigurable();
        ICoreAPI api = CreateApi(new ThrowingLiveConfigurable(), remaining);

        LiveConfigReload.NotifyAll(api);

        Assert.Equal(1, remaining.ReloadCount);
    }

    private static ICoreAPI CreateApi(params ModSystem[] systems)
    {
        var modLoader = new Mock<IModLoader>();
        modLoader.Setup(loader => loader.Systems).Returns(systems);

        var api = new Mock<ICoreAPI>();
        api.Setup(coreApi => coreApi.ModLoader).Returns(modLoader.Object);
        api.Setup(coreApi => coreApi.Logger).Returns(Mock.Of<ILogger>());
        return api.Object;
    }

    private sealed class RecordingLiveConfigurable : ModSystem, ILiveConfigurable
    {
        public int ReloadCount { get; private set; }

        public void OnConfigReloaded(ICoreAPI api)
        {
            ReloadCount++;
        }
    }

    private sealed class ThrowingLiveConfigurable : ModSystem, ILiveConfigurable
    {
        public void OnConfigReloaded(ICoreAPI api)
        {
            throw new InvalidOperationException("reload failed");
        }
    }

    private sealed class NonLiveSystem : ModSystem;
}
