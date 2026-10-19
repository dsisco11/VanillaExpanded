using HarmonyLib;
using Moq;
using VanillaExpanded.EcosystemMap;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises actual layer initialization, inactive rendering and ordered client teardown against a real session.</summary>
[Collection("Ecosystem receipt adapters")]
public sealed class MapLayerLifetimeTests
{
    #region Public API
    /// <summary>Presentation shader listeners detach through native layer/world callbacks before storage shutdown.</summary>
    [Fact]
    public void NativeLayerOwnsPresentationBeforeDurableSessionShutdown()
    {
        using var fixture = new StorageFixture();
        var world = new ClientReceiptFixture();
        var errors = new List<Exception>();
        using var receipt = new ClientReceiptAdapter(world.Game, errors.Add);
        var api = new Mock<ICoreClientAPI>();
        var events = new Mock<IClientEventAPI>();
        var loader = new Mock<IModLoader>();
        var commands = new ChatCommandApi(Mock.Of<ICoreAPI>());
        commands.Create("map").BeginSubCommand("purgedb").HandleWith(_ => TextCommandResult.Success());
        api.SetupGet(value => value.Event).Returns(events.Object);
        api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        api.SetupGet(value => value.ChatCommands).Returns(commands);
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        using var session = new EcosystemClientSession(api.Object, receipt, fixture.Open(), 32);
        var owner = new EcosystemMapSystem();
        AccessTools.Property(typeof(EcosystemMapSystem), "Client").SetValue(owner, session);
        loader.Setup(value => value.GetModSystem<EcosystemMapSystem>(true)).Returns(owner);
        var layer = new EcosystemMapLayer(api.Object, new WorldMapManager());
        layer.OnLoaded();
        events.VerifyAdd(value => value.ReloadShader += It.IsAny<ActionBoolReturn>(), Times.Once);
        // Inactive callbacks must not allocate graphics, inspect unexplored terrain or query the world.
        layer.Render(null!, 0);
        layer.OnMapClosedClient();
        layer.OnShutDown(); layer.Dispose();
        events.VerifyRemove(value => value.ReloadShader -= It.IsAny<ActionBoolReturn>(), Times.Once);
        Assert.Null(owner.Client);
        Assert.False(session.Storage.IsCurrent(session.Storage.CurrentEpoch));
        Assert.Empty(errors);
    }
    #endregion
}
