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
    public void EligibleHelp_AppendsWithoutChangingVanillaEntriesOrRequiringAnOpenSession()
    {
        using var test = new AutoLootTestCase();
        test.Inventory.Close(test.Player.Object);
        test.Dialog.TryClose();
        test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns([]);
        var vanilla = new WorldInteraction { ActionLangCode = "game:loot" };
        WorldInteraction[] original = [vanilla];
        WorldInteraction[]? result = original;
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d() };

        AutoLootInteractionPatch.AppendInteractionHelp(test.Behavior, test.Fixture.ClientApi.World, selection,
            test.Player.Object, ref result);

        Assert.NotNull(result);
        Assert.NotSame(original, result);
        Assert.Equal(2, result.Length);
        Assert.Same(vanilla, result[0]);
        Assert.Same(vanilla, Assert.Single(original));
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
    public void IneligibleHelp_PreservesOriginalArray(string reason)
    {
        using var test = new AutoLootTestCase();
        EntityBehaviorHarvestable behavior = test.Behavior;
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
            case "custom": behavior = new CustomHarvestable(test.Corpse); break;
            case "player": behavior = new EntityBehaviorHarvestable(
                new EntityPlayer { World = test.Fixture.World, Alive = false }); break;
        }
        WorldInteraction[] original = [new WorldInteraction { ActionLangCode = "game:harvest" }];
        WorldInteraction[]? result = original;
        AutoLootInteractionPatch.AppendInteractionHelp(behavior, test.Fixture.ClientApi.World, selection,
            test.Player.Object, ref result);
        Assert.Same(original, result);
        Assert.NotNull(result);
        Assert.Equal("game:harvest", Assert.Single(result).ActionLangCode);
    }

    [Fact]
    public void HelpReach_UsesTheSelectedLocalHitAndIncludesFiveBlocks()
    {
        using var test = new AutoLootTestCase();
        test.PlayerEntity.Pos.X = 6;
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d(1, 0, 0) };
        WorldInteraction[]? result = null;
        AutoLootInteractionPatch.AppendInteractionHelp(test.Behavior, test.Fixture.ClientApi.World, selection,
            test.Player.Object, ref result);
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void RealHelpPostfix_PreservesHarvestHelpAndAddsLootHelpOnlyWhileEnabled()
    {
        using var test = new AutoLootTestCase();
        test.Controller.Dispose();
        var harvestHelp = new WorldInteraction { ActionLangCode = "game:harvest" };
        test.Fixture.WorldMock.Setup(world => world.Api).Returns(test.Fixture.Api);
        test.Fixture.ApiMock.Setup(api => api.ObjectCache).Returns(new Dictionary<string, object>
        {
            ["harvestableEntityInteractions"] = new[] { harvestHelp }
        });
        var system = new AutoLootSystem_Client(test.ProgressProvider.Object);
        var selection = new EntitySelection { Entity = test.Corpse, HitPosition = new Vec3d() };
        try
        {
            system.StartClientSide(test.Fixture.ClientApi);
            EnumHandling handling = EnumHandling.PassThrough;
            test.Corpse.WatchedAttributes.SetBool("harvested", false);
            WorldInteraction[] original = test.Behavior.GetInteractionHelp(test.Fixture.ClientApi.World,
                selection, test.Player.Object, ref handling);
            Assert.Same(harvestHelp, Assert.Single(original));
            test.Corpse.WatchedAttributes.SetBool("harvested", true);
            WorldInteraction[] result = test.Behavior.GetInteractionHelp(test.Fixture.ClientApi.World,
                selection, test.Player.Object, ref handling);
            Assert.Equal("vanillaexpanded:entityhelp-autoloot", Assert.Single(result).ActionLangCode);
            Assert.Equal(EnumHandling.PassThrough, handling);
            VanillaExpandedModSystem.Config.EnableAutoLoot = false;
            Assert.Null(test.Behavior.GetInteractionHelp(test.Fixture.ClientApi.World,
                selection, test.Player.Object, ref handling));
        }
        finally
        {
            system.Dispose();
        }
        VanillaExpandedModSystem.Config.EnableAutoLoot = true;
        EnumHandling restoredHandling = EnumHandling.PassThrough;
        Assert.Null(test.Behavior.GetInteractionHelp(test.Fixture.ClientApi.World,
            selection, test.Player.Object, ref restoredHandling));
    }

    private sealed class CustomHarvestable(Entity entity) : EntityBehaviorHarvestable(entity);
}
