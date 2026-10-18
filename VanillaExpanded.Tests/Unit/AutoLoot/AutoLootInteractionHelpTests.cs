using System.Reflection;
using Moq;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootInteractionHelpTests
{
    [Fact]
    public void EligibleHelp_AggregatesWithOtherHelpWithoutRequiringAnOpenSession()
    {
        using var test = new AutoLootTestCase();
        PrepareNativeHelp(test, []);
        test.Inventory.Close(test.Player.Object);
        test.Dialog.TryClose();
        test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns([]);
        var vanilla = new WorldInteraction { ActionLangCode = "game:loot" };
        var lootBehavior = test.LootBehavior;
        test.Corpse.RemoveBehavior(lootBehavior);
        test.Corpse.AddBehavior(new OtherHelp(test.Corpse, vanilla));
        test.Corpse.AddBehavior(lootBehavior);
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d() };

        WorldInteraction[] result = test.Corpse.GetInteractionHelp(test.Fixture.ClientApi.World, selection, test.Player.Object);

        Assert.Equal(2, result.Length);
        Assert.Same(vanilla, result[0]);
        Assert.Equal("vanillaexpanded:entityhelp-autoloot", result[1].ActionLangCode);
        Assert.Equal(EnumMouseButton.Right, result[1].MouseButton);
        Assert.True(result[1].HotKeyCodes is null or { Length: 0 });
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        Assert.Empty(test.Sent);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("alive")]
    [InlineData("unharvested")]
    [InlineData("empty")]
    [InlineData("server")]
    [InlineData("dimension")]
    [InlineData("range")]
    [InlineData("selection")]
    [InlineData("missing")]
    [InlineData("hit")]
    [InlineData("custom")]
    [InlineData("player")]
    [InlineData("disposed")]
    public void IneligibleHelp_ProducesNoAdditionalHelpOrHandling(string reason)
    {
        using var test = new AutoLootTestCase();
        EntityBehaviorAutoLootable behavior = test.LootBehavior;
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d() };
        switch (reason)
        {
            case "disabled": VanillaExpandedModSystem.Config.EnableAutoLoot = false; break;
            case "alive": test.Corpse.Alive = true; break;
            case "unharvested": test.Corpse.WatchedAttributes.SetBool("harvested", false); break;
            case "empty": test.Inventory[0].Itemstack = null; break;
            case "server": test.Fixture.WorldMock.Setup(world => world.Side).Returns(EnumAppSide.Server); break;
            case "dimension": test.Corpse.Pos.Dimension = 1; break;
            case "range": test.PlayerEntity.Pos.X = 5.001; break;
            case "selection": selection.Entity = new EntityAgent(); break;
            case "missing": test.WorldEntity = null; break;
            case "hit": selection.HitPosition = null!; break;
            case "custom":
                test.Corpse.RemoveBehavior(test.Behavior);
                test.Corpse.AddBehavior(new CustomHarvestable(test.Corpse));
                break;
            case "player":
                var corpse = new EntityPlayer { World = test.Fixture.World, Alive = false };
                typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(corpse,
                    new EntityProperties { Client = new EntityClientProperties([], new Dictionary<string, Vintagestory.API.Datastructures.JsonObject>()) });
                corpse.AddBehavior(new EntityBehaviorHarvestable(corpse));
                behavior = new EntityBehaviorAutoLootable(corpse);
                break;
            case "disposed": test.System.Dispose(); break;
        }
        EnumHandling handling = EnumHandling.PassThrough;
        Assert.Null(behavior.GetInteractionHelp(test.Fixture.ClientApi.World, selection, test.Player.Object, ref handling));
        Assert.Equal(EnumHandling.PassThrough, handling);
    }

    [Fact]
    public void HelpReach_UsesTheSelectedLocalHitAndIncludesFiveBlocks()
    {
        using var test = new AutoLootTestCase();
        test.PlayerEntity.Pos.X = 6;
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d(1, 0, 0) };
        EnumHandling handling = EnumHandling.PassThrough;
        WorldInteraction[]? result = test.LootBehavior.GetInteractionHelp(test.Fixture.ClientApi.World,
            selection, test.Player.Object, ref handling);
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void RealBehaviorHelp_PreservesHarvestHelpAndAddsLootHelpOnlyWhileEnabled()
    {
        using var test = new AutoLootTestCase();
        var harvestHelp = new WorldInteraction { ActionLangCode = "game:harvest" };
        PrepareNativeHelp(test, [harvestHelp]);
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d() };
        test.Corpse.WatchedAttributes.SetBool("harvested", false);
        WorldInteraction[] original = test.Corpse.GetInteractionHelp(test.Fixture.ClientApi.World,
            selection, test.Player.Object);
        Assert.Same(harvestHelp, Assert.Single(original));
        test.Corpse.WatchedAttributes.SetBool("harvested", true);
        WorldInteraction[] result = test.Corpse.GetInteractionHelp(test.Fixture.ClientApi.World,
            selection, test.Player.Object);
        Assert.Equal("vanillaexpanded:entityhelp-autoloot", Assert.Single(result).ActionLangCode);
        VanillaExpandedModSystem.Config.EnableAutoLoot = false;
        Assert.Empty(test.Corpse.GetInteractionHelp(test.Fixture.ClientApi.World, selection, test.Player.Object));
        VanillaExpandedModSystem.Config.EnableAutoLoot = true;
        test.System.Dispose();
        Assert.Empty(test.Corpse.GetInteractionHelp(test.Fixture.ClientApi.World, selection, test.Player.Object));
    }

    private static void PrepareNativeHelp(AutoLootTestCase test, WorldInteraction[] help)
    {
        test.Fixture.WorldMock.Setup(world => world.Api).Returns(test.Fixture.Api);
        test.Fixture.ApiMock.Setup(api => api.ObjectCache).Returns(new Dictionary<string, object>
        {
            ["harvestableEntityInteractions"] = help
        });
    }

    private sealed class CustomHarvestable(Entity entity) : EntityBehaviorHarvestable(entity);

    private sealed class OtherHelp(Entity entity, WorldInteraction help) : EntityBehavior(entity)
    {
        public override string PropertyName() => "test-help";
        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es,
            IClientPlayer player, ref EnumHandling handled) => [help];
    }
}
