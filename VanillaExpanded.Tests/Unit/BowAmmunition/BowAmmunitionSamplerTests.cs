using Moq;
using VanillaExpanded.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.BowAmmunition;

/// <summary>Exercises the installed selector and native inventory traversal without firing or mutating inventory.</summary>
public sealed class BowAmmunitionSamplerTests
{
    #region Public API
    /// <summary>The first arrow collectible determines the total, independent of stack attributes.</summary>
    [Fact]
    public void MixedStacksUseSelectedIdentityAndPreserveInventory()
    {
        var fixture = new Fixture();
        fixture.Personal[0].Itemstack = new ItemStack(fixture.Flint, 12);
        fixture.Personal[1].Itemstack = new ItemStack(fixture.Flint, 8);
        fixture.Personal[1].Itemstack!.Attributes.SetString("variant", "different");
        fixture.Personal[2].Itemstack = new ItemStack(fixture.Copper, 30);
        var stacks = fixture.Personal.Select(slot => slot.Itemstack).ToArray();
        var bytes = stacks.Select(stack => stack?.ToBytes()).ToArray();
        var sample = BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity);
        Assert.Same(fixture.Flint, sample.Icon!.Collectible);
        Assert.Equal(20L, sample.Quantity);
        Assert.NotSame(stacks[0], sample.Icon);
        for (int index = 0; index < stacks.Length; index++)
        {
            Assert.Same(stacks[index], fixture.Personal[index].Itemstack);
            Assert.Equal(bytes[index], fixture.Personal[index].Itemstack?.ToBytes());
        }
    }

    /// <summary>Creative and nonpositive stacks cannot select ammunition or contribute quantity.</summary>
    [Fact]
    public void CreativeZeroNegativeAndEmptySlotsAreExcluded()
    {
        var fixture = new Fixture();
        fixture.Creative[0].Itemstack = new ItemStack(fixture.Copper, 99);
        fixture.Personal[0].Itemstack = new ItemStack(fixture.Copper, 0);
        fixture.Personal[1].Itemstack = new ItemStack(fixture.Copper, -3);
        fixture.Personal[3].Itemstack = new ItemStack(fixture.Copper, 100);
        var empty = BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity);
        Assert.Null(empty.Icon); Assert.Equal(0, empty.Quantity);
        fixture.Personal[2].Itemstack = new ItemStack(fixture.Flint, 7);
        var selected = BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity);
        Assert.Same(fixture.Flint, selected.Icon!.Collectible); Assert.Equal(7, selected.Quantity);
    }

    /// <summary>Opened external inventories participate in the same ordering used by native selection.</summary>
    [Fact]
    public void ExternalMembershipAndOrderingFollowNativeTraversal()
    {
        var fixture = new Fixture();
        fixture.Personal[0].Itemstack = new ItemStack(fixture.Flint, 12);
        fixture.External[0].Itemstack = new ItemStack(fixture.Flint, 8);
        Assert.Equal(12, BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity).Quantity);
        fixture.External.openedByPlayerGUIds.Add(Fixture.Uid);
        Assert.Equal(20, BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity).Quantity);
        fixture.External[0].Itemstack = new ItemStack(fixture.Copper, 30);
        fixture.Ordered.Remove(fixture.External); fixture.Ordered.Insert(0, fixture.External);
        var selected = BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity);
        Assert.Same(fixture.Copper, selected.Icon!.Collectible); Assert.Equal(30, selected.Quantity);
        fixture.External.openedByPlayerGUIds.Clear();
        Assert.Same(fixture.Flint, BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity).Icon!.Collectible);
    }

    /// <summary>Totals use a wide accumulator and exact verified bow types only.</summary>
    [Fact]
    public void QuantityDoesNotOverflowAndCustomBowsAreUnsupported()
    {
        var fixture = new Fixture();
        fixture.Personal[0].Itemstack = new ItemStack(fixture.Flint, int.MaxValue);
        fixture.Personal[1].Itemstack = new ItemStack(fixture.Flint, int.MaxValue);
        Assert.Equal(2L * int.MaxValue, BowAmmunitionSampler.Sample(fixture.Bow, fixture.Entity).Quantity);
        Assert.True(BowAmmunitionSelector.IsSupported(fixture.Bow));
        Assert.False(BowAmmunitionSelector.IsSupported(new DerivedBow()));
        Assert.False(BowAmmunitionSelector.IsSupported(new Item { Tool = EnumTool.Bow }));
        Assert.False(BowAmmunitionSelector.IsSupported(null));
    }
    #endregion

    #region Private
    /// <summary>Models a custom subclass whose firing semantics have not been verified.</summary>
    private sealed class DerivedBow : ItemBow { }

    /// <summary>Supplies genuine EntityPlayer traversal, inventory slots, and a local client identity.</summary>
    internal sealed class Fixture
    {
        internal const string Uid = "bow-test";
        internal readonly ItemBow Bow = new() { Code = new AssetLocation("game:bow"), ItemId = 1 };
        internal readonly Item Flint = new() { Code = new AssetLocation("game:arrow-flint"), ItemId = 2 };
        internal readonly Item Copper = new() { Code = new AssetLocation("game:arrow-copper"), ItemId = 3 };
        internal readonly InventoryGeneric Personal = new(4, "hotbar", "bow-test", null!, (index, inventory) => index == 3 ? new ItemSlotCreative(inventory) : new ItemSlotSurvival(inventory));
        internal readonly InventoryGeneric External = new(2, "chest", "bow-test", null!);
        internal readonly InventoryGeneric Creative = new(1, "creative", "bow-test", null!);
        internal readonly List<InventoryBase> Ordered = new();
        internal readonly Mock<IPlayerInventoryManager> Manager = new();
        internal readonly Mock<ICoreClientAPI> Api = new();
        internal readonly Mock<IClientEventAPI> Events = new();
        internal readonly Mock<IClientWorldAccessor> World = new();
        internal readonly Mock<IClientPlayer> Player;
        internal EntityPlayer Entity;
        internal ItemSlot Hand = new DummySlot();
        #region Public API
        /// <summary>Wires native inventory traversal to controlled opened membership and active-hand state.</summary>
        public Fixture()
        {
            Player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
            Entity = new EntityPlayer { World = World.Object };
            Entity.WatchedAttributes.SetString("playerUID", Uid);
            Player.SetupGet(value => value.PlayerUID).Returns(Uid);
            Player.SetupGet(value => value.Entity).Returns(() => Entity);
            Player.SetupGet(value => value.InventoryManager).Returns(Manager.Object);
            Manager.SetupGet(value => value.InventoriesOrdered).Returns(Ordered);
            Manager.SetupGet(value => value.ActiveHotbarSlot).Returns(() => Hand);
            World.Setup(value => value.PlayerByUid(Uid)).Returns(Player.Object);
            World.SetupGet(value => value.Player).Returns(Player.Object);
            Api.SetupGet(value => value.World).Returns(World.Object);
            Api.SetupGet(value => value.Event).Returns(Events.Object);
            foreach (var inventory in new[] { Creative, Personal, External })
            {
                inventory.Api = Api.Object;
                Ordered.Add(inventory);
            }
            Creative.openedByPlayerGUIds.Add(Uid); Personal.openedByPlayerGUIds.Add(Uid);
            Hand.Itemstack = new ItemStack(Bow);
        }
        #endregion
    }
    #endregion
}
