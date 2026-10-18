using System.Reflection;
using Moq;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.Tests.Unit.AutoLoot.Support;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoLoot;

[Collection("AutoLoot")]
[Trait("Category", "Unit")]
public sealed class AutoLootTargetTests
{
    [Theory]
    [InlineData("alive")]
    [InlineData("unharvested")]
    [InlineData("empty")]
    [InlineData("session")]
    [InlineData("manager")]
    [InlineData("closed")]
    [InlineData("missing")]
    [InlineData("dimension")]
    [InlineData("range")]
    [InlineData("behaviorremoved")]
    [InlineData("server")]
    public void IneligibleCorpse_DoesNotResolveOrStart(string reason)
    {
        using var test = new AutoLootTestCase();
        switch (reason)
        {
            case "alive": test.Corpse.Alive = true; break;
            case "unharvested": test.Corpse.WatchedAttributes.SetBool("harvested", false); break;
            case "empty": test.Inventory[0].Itemstack = null; break;
            case "session": test.Inventory.Close(test.Player.Object); break;
            case "manager": test.Fixture.InventoryManagerMock.Setup(manager => manager.OpenedInventories).Returns([]); break;
            case "closed": test.Dialog.TryClose(); break;
            case "missing": test.WorldEntity = null; break;
            case "dimension": test.Corpse.Pos.Dimension = 1; break;
            case "range": test.PlayerEntity.Pos.X = 5.001; break;
            case "behaviorremoved": test.Corpse.SidedProperties.Behaviors.Clear(); break;
            case "server": test.Fixture.WorldMock.Setup(world => world.Side).Returns(EnumAppSide.Server); break;
        }
        Assert.False(AutoLootTarget.TryCreate(test.Fixture.ClientApi, test.Player.Object,
            test.Behavior, new Vec3d(), out _));
        test.Begin();
        test.Tick(1);
        Assert.Empty(test.Sent);
        test.ProgressProvider.Verify(value => value.CreateProgressBar(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void PlayerAndCustomBehavior_AreNotAssumedCompatible()
    {
        using var test = new AutoLootTestCase();
        var playerCorpse = new EntityPlayer { World = test.Fixture.World, Api = test.Fixture.Api, Alive = false };
        Assert.False(AutoLootTarget.TryCreate(test.Fixture.ClientApi, test.Player.Object,
            new EntityBehaviorHarvestable(playerCorpse), new Vec3d(), out _));
        Assert.False(AutoLootTarget.TryCreate(test.Fixture.ClientApi, test.Player.Object,
            new NonStandardHarvestable(test.Corpse), new Vec3d(), out _));
    }

    [Fact]
    public void Reach_IsInclusiveAndUsesCopiedLocalHitAndPlayerEye()
    {
        using var test = new AutoLootTestCase();
        test.PlayerEntity.Pos.X = 6;
        var hit = new Vec3d(1, 0, 0);
        Assert.True(AutoLootTarget.TryCreate(test.Fixture.ClientApi, test.Player.Object,
            test.Behavior, hit, out AutoLootTarget? target));
        hit.X = -100;
        Assert.True(target.IsValid(test.Fixture.ClientApi, test.Player.Object));
        test.PlayerEntity.Pos.X = 6.001;
        Assert.False(target.IsValid(test.Fixture.ClientApi, test.Player.Object));
    }

    [Fact]
    public void ResolveAndValidation_DoNotModifyInventoryOrCorpseAttributes()
    {
        using var test = new AutoLootTestCase();
        ItemStack stack = Assert.IsType<ItemStack>(test.Inventory[0].Itemstack);
        string attributes = test.Corpse.WatchedAttributes.ToJsonToken().ToString();
        AutoLootTarget target = test.ResolveTarget();
        Assert.Same(test.Inventory, target.Inventory);
        Assert.True(target.IsValid(test.Fixture.ClientApi, test.Player.Object));
        Assert.Same(stack, test.Inventory[0].Itemstack);
        Assert.Equal(3, stack.StackSize);
        Assert.Equal(attributes, test.Corpse.WatchedAttributes.ToJsonToken().ToString());
        test.Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        Assert.Empty(test.Sent);
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("entityid")]
    [InlineData("behavior")]
    [InlineData("inventory")]
    [InlineData("player")]
    public void ReplacedIdentity_InvalidatesCapturedTargetAndCleansUpPendingHold(string replaced)
    {
        using var test = new AutoLootTestCase();
        AutoLootTarget target = test.ResolveTarget();
        test.Begin();
        test.Tick(0.25f);
        switch (replaced)
        {
            case "entity":
                test.WorldEntity = new EntityAgent { EntityId = test.Corpse.EntityId };
                break;
            case "entityid":
                test.Corpse.EntityId++;
                break;
            case "behavior":
                test.Corpse.SidedProperties.Behaviors.Remove(test.Behavior);
                test.Corpse.SidedProperties.Behaviors.Add(new EntityBehaviorHarvestable(test.Corpse));
                break;
            case "inventory":
                typeof(EntityBehaviorHarvestable).GetField("inv", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(test.Behavior, new InventoryGeneric(3, "harvestableContents", "42", null!));
                break;
            case "player":
                test.Player.Setup(player => player.Entity).Returns(new EntityPlayer
                {
                    World = test.Fixture.World, Api = test.Fixture.Api
                });
                break;
        }

        Assert.False(target.IsValid(test.Fixture.ClientApi, test.Player.Object));
        test.Tick(1);
        test.Tick(1);
        Assert.Empty(test.Attempts);
        Assert.Empty(test.Sent);
        Assert.Equal(3, test.Inventory[0].StackSize);
        test.ProgressProvider.Verify(provider => provider.RemoveProgressBar(test.Progress.Object), Times.Once);
    }

    private sealed class NonStandardHarvestable(Entity entity) : EntityBehaviorHarvestable(entity);
}
