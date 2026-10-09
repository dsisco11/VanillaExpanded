using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.src.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects supported block selection, behavior order, identity preservation, and idempotent installation.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class BehaviorInstallationTests
{
    #region Public API
    /// <summary>Prepends shared behavior instances to both arrays while preserving every existing behavior and repeat identity.</summary>
    [Theory]
    [InlineData("container")]
    [InlineData("crate")]
    [InlineData("bloomery")]
    public void SupportedBlock_InstallsInOrderAndRemainsIdempotent(string kind)
    {
        var fixture = VsTestFixture.Server();
        Block block = kind == "bloomery" ? new BlockBloomery() : kind == "crate" ? new BlockCrate() : new Block();
        block.Code = new AssetLocation("game:installation-" + kind);
        block.EntityClass = kind == "crate" ? "Crate" : kind;
        var existing = new Mock<BlockBehavior>(block).Object;
        var container = new BlockBehaviorContainer(block);
        block.BlockBehaviors = kind == "bloomery" ? [existing] : [existing, container];
        block.CollectibleBehaviors = kind == "bloomery" ? [existing] : [existing, container];
        var beforeBlock = block.BlockBehaviors.ToArray();
        var beforeCollectible = block.CollectibleBehaviors.ToArray();
        fixture.WorldMock.SetupGet(value => value.Blocks).Returns([block]);

        AutoStashPatch.AmendContainerBehaviors(fixture.Api);

        Assert.IsType<BlockBehaviorAutoStashable>(block.BlockBehaviors[0]);
        Assert.Same(block.BlockBehaviors[0], block.CollectibleBehaviors[0]);
        int added = kind == "crate" ? 2 : 1;
        if (kind == "crate")
        {
            Assert.IsType<BehaviorCrateEntityEventBridge>(block.BlockBehaviors[1]);
            Assert.Same(block.BlockBehaviors[1], block.CollectibleBehaviors[1]);
        }
        Assert.Equal(beforeBlock.Length + added, block.BlockBehaviors.Length);
        Assert.Equal(beforeCollectible.Length + added, block.CollectibleBehaviors.Length);
        Assert.Equal(beforeBlock, block.BlockBehaviors.Skip(added));
        Assert.Equal(beforeCollectible, block.CollectibleBehaviors.Skip(added));
        var installedBlock = block.BlockBehaviors;
        var installedCollectible = block.CollectibleBehaviors;

        AutoStashPatch.AmendContainerBehaviors(fixture.Api);

        Assert.Same(installedBlock, block.BlockBehaviors);
        Assert.Same(installedCollectible, block.CollectibleBehaviors);
        Assert.Single(block.BlockBehaviors.OfType<BlockBehaviorAutoStashable>());
        Assert.Equal(kind == "crate" ? 1 : 0, block.BlockBehaviors.OfType<BehaviorCrateEntityEventBridge>().Count());
    }

    /// <summary>Leaves unsupported, null-code, and null entries untouched, including legitimate empty behavior arrays.</summary>
    [Fact]
    public void UnsupportedAndMissingCodes_AreExcluded()
    {
        var fixture = VsTestFixture.Server();
        var unsupported = new Block { Code = new AssetLocation("game:unsupported"), BlockBehaviors = [], CollectibleBehaviors = [] };
        var noCode = new Block { Code = null! };
        var existing = new BlockBehaviorContainer(noCode);
        noCode.BlockBehaviors = [existing];
        noCode.CollectibleBehaviors = [existing];
        var unsupportedBlocks = unsupported.BlockBehaviors;
        var unsupportedCollectibles = unsupported.CollectibleBehaviors;
        var noCodeBlocks = noCode.BlockBehaviors;
        var noCodeCollectibles = noCode.CollectibleBehaviors;
        fixture.WorldMock.SetupGet(value => value.Blocks).Returns([unsupported, noCode, null!]);

        AutoStashPatch.AmendContainerBehaviors(fixture.Api);
        AutoStashPatch.AmendContainerBehaviors(fixture.Api);

        Assert.Same(unsupportedBlocks, unsupported.BlockBehaviors);
        Assert.Same(unsupportedCollectibles, unsupported.CollectibleBehaviors);
        Assert.Empty(unsupported.BlockBehaviors);
        Assert.Same(noCodeBlocks, noCode.BlockBehaviors);
        Assert.Same(noCodeCollectibles, noCode.CollectibleBehaviors);
        Assert.Same(existing, Assert.Single(noCode.BlockBehaviors));
        Assert.Same(existing, Assert.Single(noCode.CollectibleBehaviors));
    }
    #endregion
}
