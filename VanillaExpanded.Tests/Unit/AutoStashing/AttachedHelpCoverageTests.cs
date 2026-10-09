using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing;

/// <summary>Protects attached-help append gates while retaining pre-existing interaction identities and ordering.</summary>
[Trait("Category", "Unit")]
[Collection("AutoStash")]
public sealed class AttachedHelpCoverageTests
{
    #region Public API
    /// <summary>Appends exactly one Ctrl+Shift action for eligible contents and leaves rejected interaction arrays untouched.</summary>
    [Theory]
    [InlineData("eligible")]
    [InlineData("disabled")]
    [InlineData("body")]
    [InlineData("unmapped")]
    [InlineData("empty")]
    [InlineData("nonbag")]
    [InlineData("unmatched")]
    public void HelpAppend_PreservesExistingInteractionsAndGates(string condition)
    {
        using var test = new AttachedGestureCase();
        var originalAttachment = test.Attachable.Inventory[1].Itemstack;
        var originalSource = test.Fixture.BackpackInventory[0].Itemstack;
        try
        {
            if (condition == "disabled") VanillaExpandedModSystem.Config.EnableAutoStash = false;
            if (condition == "body") test.Selection.SelectionBoxIndex = 0;
            if (condition == "unmapped") test.SlotConfigurations[1].AttachmentPointCode = "not-present";
            if (condition == "empty") test.Attachable.Inventory[1].Itemstack = null;
            if (condition == "nonbag") test.Attachable.Inventory[1].Itemstack = test.Fixture.BackpackInventory[0].Itemstack!.Clone();
            if (condition == "unmatched") test.Fixture.BackpackInventory[0].Itemstack = null;
            var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, test.Attachable.Inventory);
            var first = new WorldInteraction { ActionLangCode = "existing-first", HotKeyCodes = ["existing-key"] };
            var second = new WorldInteraction { ActionLangCode = "existing-second" };
            WorldInteraction[] original = [first, second];
            var interactions = original;

            EntityAttachedContainerAutoStash.AppendInteractionHelp(test.Attachable, test.Fixture.ClientWorldMock!.Object,
                test.Selection, test.Player.Object, ref interactions);

            Assert.Same(first, interactions[0]);
            Assert.Same(second, interactions[1]);
            Assert.Equal(["existing-key"], first.HotKeyCodes);
            if (condition == "eligible")
            {
                Assert.NotSame(original, interactions);
                Assert.Equal(3, interactions.Length);
                Assert.Equal("vanillaexpanded:blockhelp-autostash-container", interactions[2].ActionLangCode);
                Assert.Equal(EnumMouseButton.Right, interactions[2].MouseButton);
                Assert.Equal(["ctrl", "shift"], interactions[2].HotKeyCodes);
                Assert.Null(interactions[2].Itemstacks);
            }
            else Assert.Same(original, interactions);
            before.AssertUnchangedExcept();
            before.AssertConserved();
            Assert.Empty(test.Channel.SentPackets);
            test.ProgressProvider.Verify(value => value.CreateProgressBar(), Moq.Times.Never);
            test.Fixture.InventoryManagerMock.Verify(value => value.OpenInventory(Moq.It.IsAny<IInventory>()), Moq.Times.Never);
            test.Fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(Moq.It.IsAny<IInventory>()), Moq.Times.Never);
        }
        finally
        {
            // Restore intentional scenario setup before the shared gesture fixture checks its constructor baseline.
            test.Attachable.Inventory[1].Itemstack = originalAttachment;
            test.Fixture.BackpackInventory[0].Itemstack = originalSource;
        }
    }
    #endregion
}
