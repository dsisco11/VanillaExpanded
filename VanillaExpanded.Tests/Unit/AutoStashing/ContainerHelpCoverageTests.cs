using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects container help actions, crate modifiers, representative ordering, and independent display stacks.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class ContainerHelpCoverageTests
{
    #region Public API
    /// <summary>Distinguishes full matching targets from unmatched contents and a disabled feature for both container policies.</summary>
    [Theory]
    [InlineData(false, "full")]
    [InlineData(true, "full")]
    [InlineData(false, "unmatched")]
    [InlineData(true, "unmatched")]
    [InlineData(false, "disabled")]
    [InlineData(true, "disabled")]
    public void HelpGate_UsesExactActionAndDisplayContract(bool crate, string condition)
    {
        using var scope = new AutoStashTestScope();
        var fixture = VsTestFixture.Client();
        var item = Item(fixture, 1);
        var other = Item(fixture, 2);
        var inventory = new InventoryGeneric(1, "help", "target", null!) { Api = fixture.Api };
        inventory[0].Itemstack = Stack(item, condition == "full" ? 64 : 10);
        fixture.BackpackInventory[0].Itemstack = Stack(condition == "unmatched" ? other : item, 3);
        var target = new Mock<BlockEntityContainer>();
        target.SetupGet(value => value.Inventory).Returns(inventory);
        var selection = new BlockSelection { Position = new BlockPos(0) };
        var accessor = new Mock<IBlockAccessor>();
        accessor.Setup(value => value.GetBlockEntity(selection.Position)).Returns(target.Object);
        fixture.WorldMock.SetupGet(value => value.BlockAccessor).Returns(accessor.Object);
        var behavior = new BlockBehaviorAutoStashable(new Block { EntityClass = crate ? "Crate" : "Container" });
        VanillaExpandedModSystem.Config.EnableAutoStash = condition != "disabled";
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, inventory);
        EnumHandling handling = EnumHandling.PassThrough;

        var result = behavior.GetPlacedBlockInteractionHelp(fixture.World, selection, fixture.Player, ref handling);

        if (condition == "full")
        {
            var interaction = Assert.Single(result);
            Assert.Equal("vanillaexpanded:blockhelp-autostash-full", interaction.ActionLangCode);
            Assert.Equal(EnumMouseButton.Right, interaction.MouseButton);
            Assert.Null(interaction.Itemstacks);
            if (crate) Assert.Equal(["ctrl", "shift"], interaction.HotKeyCodes);
            else Assert.Null(interaction.HotKeyCodes);
        }
        else Assert.Empty(result);
        Assert.Equal(EnumHandling.PassThrough, handling);
        before.AssertUnchangedExcept();
        before.AssertConserved();
        Assert.Empty(inventory.DirtySlots);
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
    }

    /// <summary>Uses first backpack representatives, deduplicates all sources, and clones both stack data and attribute trees.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Representatives_AreDeduplicatedIndependentClones(bool crate)
    {
        using var scope = new AutoStashTestScope();
        var fixture = VsTestFixture.Client();
        var first = Item(fixture, 1);
        var second = Item(fixture, 2);
        var unrelated = Item(fixture, 3);
        var inventory = new InventoryGeneric(2, "help", "target", null!) { Api = fixture.Api };
        inventory[0].Itemstack = Stack(first, 10);
        inventory[1].Itemstack = Stack(second, 10);
        fixture.BackpackInventory[0].Itemstack = Stack(first, 3);
        fixture.BackpackInventory[1].Itemstack = Stack(first, 4);
        fixture.HotbarInventory[0].Itemstack = Stack(first, 5);
        fixture.HotbarInventory[1].Itemstack = Stack(second, 6);
        fixture.HotbarInventory[9].Itemstack = Stack(unrelated, 7);
        var target = new Mock<BlockEntityContainer>();
        target.SetupGet(value => value.Inventory).Returns(inventory);
        var selection = new BlockSelection { Position = new BlockPos(0) };
        var accessor = new Mock<IBlockAccessor>();
        accessor.Setup(value => value.GetBlockEntity(selection.Position)).Returns(target.Object);
        fixture.WorldMock.SetupGet(value => value.BlockAccessor).Returns(accessor.Object);
        var behavior = new BlockBehaviorAutoStashable(new Block { EntityClass = crate ? "Crate" : "Container" });
        var before = new InventorySnapshot(fixture.BackpackInventory, fixture.HotbarInventory, inventory);
        EnumHandling handling = EnumHandling.PassThrough;

        var interaction = Assert.Single(behavior.GetPlacedBlockInteractionHelp(fixture.World, selection, fixture.Player, ref handling));

        Assert.Equal("vanillaexpanded:blockhelp-autostash-container", interaction.ActionLangCode);
        Assert.Equal(EnumMouseButton.Right, interaction.MouseButton);
        if (crate) Assert.Equal(["ctrl", "shift"], interaction.HotKeyCodes);
        else Assert.Null(interaction.HotKeyCodes);
        Assert.NotNull(interaction.Itemstacks);
        Assert.Equal([first.Id, second.Id], interaction.Itemstacks.Select(value => value.Collectible.Id));
        Assert.Equal([3, 6], interaction.Itemstacks.Select(value => value.StackSize));
        Assert.Same(first, interaction.Itemstacks[0].Collectible);
        Assert.Same(second, interaction.Itemstacks[1].Collectible);
        Assert.NotSame(fixture.BackpackInventory[0].Itemstack, interaction.Itemstacks[0]);
        Assert.NotSame(fixture.HotbarInventory[1].Itemstack, interaction.Itemstacks[1]);
        foreach (var stack in interaction.Itemstacks)
        {
            Assert.Equal("preserved", stack.Attributes.GetString("fixture"));
            // Engine Clone copies persistent attributes; display stacks start with independent empty transient data.
            Assert.Null(stack.TempAttributes.GetString("fixture"));
            stack.StackSize = 99;
            stack.Attributes.SetString("fixture", "changed");
            stack.TempAttributes.SetString("fixture", "changed");
        }
        before.AssertUnchangedExcept();
        before.AssertConserved();
        Assert.Empty(inventory.DirtySlots);
        target.Verify(value => value.MarkDirty(It.IsAny<bool>(), It.IsAny<IPlayer>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
    }
    #endregion

    #region Private
    /// <summary>Creates distinct real collectible identities with stable item codes.</summary>
    private static MockItem Item(VsTestFixture fixture, int id) => new(id, api: fixture.Api) { Code = new AssetLocation("game:help-" + id), MaxStackSize = 64 };

    /// <summary>Seeds persistent and temporary attributes whose mutation must not alias inventories.</summary>
    private static ItemStack Stack(CollectibleObject item, int quantity)
    {
        var stack = new ItemStack(item, quantity);
        stack.Attributes.SetString("fixture", "preserved");
        stack.TempAttributes.SetString("fixture", "temporary");
        return stack;
    }
    #endregion
}
