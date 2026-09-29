using Moq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

[Trait("Category", "Unit")]
public class ContainerInteractionHelpTests
{
    [Fact]
    public void GetPlacedBlockInteractionHelp_MatchingItems_ShowsEachStashableItemType()
    {
        var firstMatchingItem = new MockItem(1);
        var secondMatchingItem = new MockItem(2);
        var unrelatedItem = new MockItem(3);
        var player = new MockPlayer()
            .WithBackpack(MockInventory.WithItems(firstMatchingItem, firstMatchingItem, unrelatedItem))
            .WithHotbar(MockInventory.WithItems(secondMatchingItem));
        var container = MockBlockEntityContainer.WithItems(firstMatchingItem, secondMatchingItem);
        var selection = new BlockSelection { Position = container.Position };

        var blockAccessor = new Mock<IBlockAccessor>();
        blockAccessor.Setup(value => value.GetBlockEntity(container.Position)).Returns(container.Object);
        var world = new Mock<IWorldAccessor>();
        world.Setup(value => value.BlockAccessor).Returns(blockAccessor.Object);

        var behavior = new BlockBehaviorAutoStashable(new Block());
        EnumHandling handling = EnumHandling.PassThrough;

        WorldInteraction interaction = Assert.Single(
            behavior.GetPlacedBlockInteractionHelp(world.Object, selection, player.Object, ref handling));

        Assert.Equal("vanillaexpanded:blockhelp-autostash-container", interaction.ActionLangCode);
        Assert.NotNull(interaction.Itemstacks);
        Assert.Equal([1, 2], interaction.Itemstacks.Select(stack => stack.Collectible.Id).Order().ToArray());
    }
}