using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.AutoLoot;
using Moq;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootSystemTests
{
    [Fact]
    public void ShouldLoad_IsClientOnlyRegardlessOfFeatureToggle()
    {
        using var test = new AutoLootTestCase();
        var system = new AutoLootSystem_Client();
        VanillaExpandedModSystem.Config.EnableAutoLoot = false;
        Assert.True(system.ShouldLoad(EnumAppSide.Client));
        Assert.False(system.ShouldLoad(EnumAppSide.Server));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void SynchronizedDefinition_GetsOneOwnedBehaviorAfterVanillaRegardlessOfToggleOrLife(bool enabled, bool alive)
    {
        using var test = new AutoLootTestCase();
        test.Corpse.RemoveBehavior(test.LootBehavior);
        EntityProperties definition = CreateDefinition("harvestable");
        test.EntityTypes.Clear();
        test.EntityTypes.Add(definition);
        VanillaExpandedModSystem.Config.EnableAutoLoot = enabled;
        test.Corpse.Alive = alive;
        test.Corpse.WatchedAttributes.SetBool("harvested", !alive);
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        JsonObject[] first = definition.Client.BehaviorsAsJsonObj;
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        Assert.Same(first, definition.Client.BehaviorsAsJsonObj);
        Assert.Equal(["harvestable", EntityBehaviorAutoLootable.RegistryId],
            first.Select(behavior => behavior["code"].AsString()));
        Assert.Same(test.Behavior, Assert.Single(test.Corpse.SidedProperties.Behaviors));
        test.Events.VerifyAdd(events => events.OnEntityLoaded += It.IsAny<EntityDelegate>(), Times.Never);
        test.Fixture.ClientApiMock!.Verify(api => api.RegisterEntityBehaviorClass(
            EntityBehaviorAutoLootable.RegistryId, typeof(EntityBehaviorAutoLootable)), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistryAliasesAndDerivedHarvestables_AreRecognized(bool derived)
    {
        using var test = new AutoLootTestCase();
        EntityProperties definition = CreateDefinition("modded-harvestable");
        test.ClassRegistry.Setup(registry => registry.GetEntityBehaviorClass("modded-harvestable"))
            .Returns(derived ? typeof(CustomHarvestable) : typeof(EntityBehaviorHarvestable));
        test.EntityTypes.Clear();
        test.EntityTypes.Add(definition);
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        Assert.Equal(["modded-harvestable", EntityBehaviorAutoLootable.RegistryId],
            definition.Client.BehaviorsAsJsonObj.Select(behavior => behavior["code"].AsString()));
    }

    [Theory]
    [InlineData("missing-client")]
    [InlineData("empty")]
    [InlineData("unknown")]
    [InlineData("other")]
    [InlineData("server-only")]
    public void DefinitionsWithoutClientHarvestable_AreNotAmended(string reason)
    {
        using var test = new AutoLootTestCase();
        EntityProperties definition = CreateDefinition();
        switch (reason)
        {
            case "missing-client": definition.Client = null!; break;
            case "unknown": definition = CreateDefinition("unknown"); break;
            case "other":
                definition = CreateDefinition("other");
                test.ClassRegistry.Setup(registry => registry.GetEntityBehaviorClass("other"))
                    .Returns(typeof(EntityBehaviorAutoLootable));
                break;
            case "server-only":
                definition.Server = new EntityServerProperties([JsonObject.FromJson("{\"code\":\"harvestable\"}")], null);
                break;
        }
        JsonObject[]? original = definition.Client?.BehaviorsAsJsonObj;
        test.EntityTypes.Clear();
        test.EntityTypes.Add(definition);
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        Assert.Same(original, definition.Client?.BehaviorsAsJsonObj);
        if (definition.Server is not null)
        {
            Assert.Equal("harvestable", Assert.Single(definition.Server.BehaviorsAsJsonObj)["code"].AsString());
        }
    }

    [Fact]
    public void SharedDefinitionArrays_PreserveServerAndExistingClientBehaviorOrder()
    {
        using var test = new AutoLootTestCase();
        EntityProperties definition = CreateDefinition("before", "harvestable", "after");
        JsonObject[] original = definition.Client.BehaviorsAsJsonObj;
        definition.Server = new EntityServerProperties([], null) { BehaviorsAsJsonObj = original };
        test.EntityTypes.Clear();
        test.EntityTypes.Add(definition);
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        Assert.Same(original, definition.Server.BehaviorsAsJsonObj);
        Assert.Equal(["before", "harvestable", "after"], original.Select(behavior => behavior["code"].AsString()));
        Assert.Equal(["before", "harvestable", "after", EntityBehaviorAutoLootable.RegistryId],
            definition.Client.BehaviorsAsJsonObj.Select(behavior => behavior["code"].AsString()));
        Assert.Empty(definition.Client.Behaviors);
    }

    [Fact]
    public void Startup_WaitsForSynchronizedDefinitionsBeforeReadingEntityTypes()
    {
        using var test = new AutoLootTestCase();
        test.System.Dispose();
        test.EntityTypes.Clear();
        test.Fixture.WorldMock.Invocations.Clear();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            test.Fixture.WorldMock.VerifyGet(world => world.EntityTypes, Times.Never);
            EntityProperties received = CreateDefinition("harvestable");
            test.EntityTypes.Add(received);
            test.Events.Raise(events => events.BlockTexturesLoaded += null);
            Assert.Equal(["harvestable", EntityBehaviorAutoLootable.RegistryId],
                received.Client.BehaviorsAsJsonObj.Select(behavior => behavior["code"].AsString()));
            test.Fixture.WorldMock.VerifyGet(world => world.EntityTypes, Times.Once);
        }
        finally
        {
            system.Dispose();
        }
    }

    [Fact]
    public void ExistingOwnedDefinition_IsNotDuplicatedOrReordered()
    {
        using var test = new AutoLootTestCase();
        EntityProperties definition = CreateDefinition(EntityBehaviorAutoLootable.RegistryId, "harvestable");
        JsonObject[] original = definition.Client.BehaviorsAsJsonObj;
        test.EntityTypes.Clear();
        test.EntityTypes.Add(definition);
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        Assert.Same(original, definition.Client.BehaviorsAsJsonObj);
        Assert.Equal([EntityBehaviorAutoLootable.RegistryId, "harvestable"],
            original.Select(behavior => behavior["code"].AsString()));
    }

    [Fact]
    public void ClonedDefinitions_CreateAndInitializePerEntityBehaviorsThroughNativeLoader()
    {
        using var test = new AutoLootTestCase();
        test.ClassRegistry.Setup(registry => registry.GetEntityBehaviorClass(EntityBehaviorAutoLootable.RegistryId))
            .Returns(typeof(EntityBehaviorAutoLootable));
        test.ClassRegistry.Setup(registry => registry.CreateEntityBehavior(It.IsAny<Entity>(), It.IsAny<string>()))
            .Returns((Entity entity, string code) => code == EntityBehaviorAutoLootable.RegistryId
                ? new EntityBehaviorAutoLootable(entity) : (EntityBehavior)new CustomHarvestable(entity));
        test.Fixture.WorldMock.Setup(world => world.ClassRegistry).Returns(test.ClassRegistry.Object);
        EntityProperties definition = test.EntityTypes[0];
        var instances = new List<EntityBehaviorAutoLootable>();
        for (int i = 0; i < 2; i++)
        {
            var properties = new EntityProperties { Client = (EntityClientProperties)definition.Client.Clone() };
            var entity = new EntityAgent { World = test.Fixture.World, Api = test.Fixture.Api };
            typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(entity, properties);
            properties.Client.loadBehaviors(entity, properties, test.Fixture.World);
            var harvestable = Assert.IsType<CustomHarvestable>(properties.Client.Behaviors[0]);
            Assert.True(harvestable.Initialized);
            var owned = Assert.IsType<EntityBehaviorAutoLootable>(properties.Client.Behaviors[1]);
            Assert.Same(entity, owned.entity);
            Assert.Equal(EntityBehaviorAutoLootable.RegistryId, owned.PropertyName());
            instances.Add(owned);
        }
        Assert.NotSame(instances[0], instances[1]);
        Assert.Empty(definition.Client.Behaviors);
        test.Events.VerifyAdd(events => events.OnEntityLoaded += It.IsAny<EntityDelegate>(), Times.Never);
    }

    [Fact]
    public void Dispose_DetachesDefinitionReadyObserverAndLeavesNoActiveController()
    {
        using var test = new AutoLootTestCase();
        EntityProperties definition = CreateDefinition("harvestable");
        test.EntityTypes.Clear();
        test.EntityTypes.Add(definition);
        test.System.Dispose();
        test.System.Dispose();
        test.Events.Raise(events => events.BlockTexturesLoaded += null);
        Assert.Equal("harvestable", Assert.Single(definition.Client.BehaviorsAsJsonObj)["code"].AsString());
        Assert.Null(AutoLootSystem_Client.Active);
        test.Events.VerifyRemove(events => events.BlockTexturesLoaded -= It.IsAny<Action>(), Times.Once);
    }

    [Fact]
    public void RealBehaviorDispatch_PreservesNativeOpeningThenStartsClientHoldWithoutPatches()
    {
        using var test = new AutoLootTestCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        var dialog = (GuiDialogCreatureContents)RuntimeHelpers.GetUninitializedObject(typeof(GuiDialogCreatureContents));
        typeof(GuiDialog).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, true);
        typeof(EntityBehaviorHarvestable).GetField("dlg", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(test.Behavior, dialog);
        typeof(EntityBehaviorContainer).GetField("Api", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(test.Behavior, test.Fixture.Api);
        test.PlayerEntity.WatchedAttributes.SetString("playerUID", test.Player.Object.PlayerUID);
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            Assert.Same(system, AutoLootSystem_Client.Active);
            test.Corpse.OnInteract(test.PlayerEntity, test.Fixture.HotbarInventory[0],
                new Vec3d(), EnumInteractMode.Interact);

            test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(test.Inventory), Times.Once);
            test.Tick(0.5f);
            Assert.Single(test.Sent);
            Assert.True(dialog.IsOpened());
        }
        finally
        {
            system.Dispose();
        }
        Assert.Null(AutoLootSystem_Client.Active);
        test.Fixture.ClientNetworkMock!.Verify(network => network.RegisterChannel(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LiveDisable_CancelsImmediatelyAndCanEnableWithoutReattachingBehavior()
    {
        using var test = new AutoLootTestCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        try
        {
            VanillaExpandedModSystem.Config.EnableAutoLoot = false;
            system.StartClientSide(test.Fixture.ClientApi);
            VanillaExpandedModSystem.Config.EnableAutoLoot = true;
            system.OnConfigReloaded(test.Fixture.Api);
            system.Client!.OnInteract(test.Behavior, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.25f);
            VanillaExpandedModSystem.Config.EnableAutoLoot = false;
            system.OnConfigReloaded(test.Fixture.Api);
            test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
            test.Tick(1);
            Assert.Empty(test.Sent);
            VanillaExpandedModSystem.Config.EnableAutoLoot = true;
            test.Release();
            system.Client.OnInteract(test.Behavior, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.5f);
            Assert.Single(test.Sent);
        }
        finally
        {
            system.Dispose();
        }
    }

    [Fact]
    public void AttachedAutoStash_TakesPrecedenceOverCorpseLootForTheSameHold()
    {
        using var test = new AutoLootTestCase();
        using var attached = new AttachedGestureCase();
        test.Controller.Dispose();
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            system.Client!.OnInteract(test.Behavior, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(0.25f);
            attached.Controller.Begin(attached.Attachable, 1);
            test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
            system.Client.OnInteract(test.Behavior, test.PlayerEntity, new Vec3d(), EnumInteractMode.Interact);
            test.Tick(1);
            Assert.Empty(test.Sent);
            attached.Tick(0.5f);
            Assert.Single(attached.Channel.SentPackets);
        }
        finally
        {
            system.Dispose();
        }
    }

    private static EntityProperties CreateDefinition(params string[] codes) => new()
    {
        Client = new EntityClientProperties(codes.Select(code =>
            JsonObject.FromJson("{\"code\":\"" + code + "\"}")).ToArray(), null)
    };

    private sealed class CustomHarvestable(Entity entity) : EntityBehaviorHarvestable(entity)
    {
        public bool Initialized { get; private set; }
        public override void Initialize(EntityProperties properties, JsonObject attributes) => Initialized = true;
    }
}
