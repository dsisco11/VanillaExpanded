using HarmonyLib;
using Moq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.src.AutoStashing;
using VanillaExpanded.Tests.Unit.AutoStashing.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.IntegrationHarness;

/// <summary>Runs production Harmony patches only when explicitly invoked by the separate integration test assembly.</summary>
/// <remarks>The reverse-patched stub is process-local and intentionally confined to that integration host's lifetime.</remarks>
public static class EngineDispatchHarness
{
    #region Public API
    /// <summary>Installs actual production patches, exercises engine entries, and removes ordinary owners even after assertion failure.</summary>
    public static void RunIsolatedDispatchChecks()
    {
        const string owner = "vanillaexpanded.tests.engine-dispatch";
        var harmony = new Harmony(owner);
        Assert.False(Harmony.HasAnyPatches(owner));
        try
        {
            harmony.CreateClassProcessor(typeof(AutoStashPatch)).Patch();
            Assert.True(Harmony.HasAnyPatches(owner));
            foreach (string kind in new[] { "crate", "bloomery" })
            {
                CheckBlockEntry(kind, true);
                CheckBlockEntry(kind, false);
            }
            CheckAttachedEntry(true);
            CheckAttachedEntry(false);
            CheckAttachedHelp();
        }
        finally
        {
            harmony.UnpatchAll(owner);
            Assert.False(Harmony.HasAnyPatches(owner));
            Assert.DoesNotContain(Harmony.GetAllPatchedMethods(), method => Harmony.GetPatchInfo(method)?.Owners.Contains(owner) == true);
        }
    }
    #endregion

    #region Private
    /// <summary>Calls each real engine block override, observing suppression or a sentinel raised at its first vanilla world access.</summary>
    private static void CheckBlockEntry(string kind, bool handled)
    {
        using var test = new BlockGestureCase(kind);
        Block block = test.Behavior.block;
        Assert.Equal(kind == "crate" ? typeof(BlockCrate) : typeof(BlockBloomery), block.GetType());
        var claims = new Mock<ILandClaimAPI>();
        claims.Setup(value => value.TryAccess(test.Player.Object, test.Selection.Position, EnumBlockAccessFlags.Use)).Returns(true);
        test.Fixture.WorldMock.SetupGet(value => value.Claims).Returns(claims.Object);
        block.BlockBehaviors = [test.Behavior];
        block.CollectibleBehaviors = [test.Behavior];
        var accessor = Mock.Get(test.Fixture.World.BlockAccessor);
        var target = accessor.Object.GetBlockEntity(test.Selection.Position);
        var inventory = target is Vintagestory.GameContent.BlockEntityBloomery bloomery
            ? BloomeryAccessor.GetInventory(bloomery) : ((BlockEntityContainer)target).Inventory;
        Assert.NotNull(inventory);
        var before = new InventorySnapshot(test.Fixture.BackpackInventory, test.Fixture.HotbarInventory, inventory);
        accessor.Invocations.Clear();
        var vanilla = new InvalidOperationException("vanilla block interaction reached its world lookup");
        if (!handled)
        {
            VanillaExpandedModSystem.Config.EnableAutoStash = false;
            accessor.Setup(value => value.GetBlockEntity(test.Selection.Position)).Throws(vanilla);
        }

        if (handled)
        {
            Assert.True(block.OnBlockInteractStart(test.Fixture.World, test.Player.Object, test.Selection));
            Assert.Equal(EStashingState.PreStashGracePeriod, test.Behavior.State);
            // AutoStash eligibility uses one lookup; the original override would perform another.
            accessor.Verify(value => value.GetBlockEntity(test.Selection.Position), Times.Once);
        }
        else Assert.Same(vanilla, Assert.Throws<InvalidOperationException>(() =>
            block.OnBlockInteractStart(test.Fixture.World, test.Player.Object, test.Selection)));
        before.AssertUnchangedExcept();
        before.AssertConserved();
        Assert.Empty(test.Requests);
        test.Fixture.InventoryManagerMock.Verify(value => value.OpenInventory(It.IsAny<IInventory>()), Times.Never);
        test.Fixture.InventoryManagerMock.Verify(value => value.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Never);
    }

    /// <summary>Uses a narrow attached-item callback to prove the original entity interaction runs only when the production prefix allows it.</summary>
    private static void CheckAttachedEntry(bool handled)
    {
        using var test = new AttachedGestureCase();
        ItemStack attachment = test.Attachable.Inventory[1].Itemstack!;
        var originalBehaviors = attachment.Collectible.CollectibleBehaviors;
        var behavior = new Mock<CollectibleBehavior>(attachment.Collectible);
        var bag = behavior.As<IHeldBag>();
        var interaction = behavior.As<IAttachedInteractions>();
        bag.Setup(value => value.GetContents(It.IsAny<ItemStack>(), It.IsAny<IWorldAccessor>()))
            .Returns([test.Fixture.BackpackInventory[0].Itemstack!]);
        attachment.Collectible.CollectibleBehaviors = [behavior.Object];
        try
        {
            if (!handled) VanillaExpandedModSystem.Config.EnableAutoStash = false;
            EnumHandling handling = EnumHandling.PassThrough;
            test.Attachable.OnInteract(test.PlayerEntity, test.Fixture.HotbarInventory[0], new Vec3d(),
                handled ? EnumInteractMode.Interact : EnumInteractMode.Attack, ref handling);
            interaction.Verify(value => value.OnInteract(test.Attachable.Inventory[1], 0, test.Host, test.PlayerEntity,
                It.IsAny<Vec3d>(), It.IsAny<EnumInteractMode>(), ref It.Ref<EnumHandling>.IsAny, It.IsAny<Action>()),
                handled ? Times.Never() : Times.Once());
            Assert.Equal(handled ? EnumHandling.PreventSubsequent : EnumHandling.PassThrough, handling);
            Assert.Empty(test.Channel.SentPackets);
            test.ProgressProvider.Verify(value => value.CreateProgressBar(), Times.Never());
            test.Tick(.1f);
            test.ProgressProvider.Verify(value => value.CreateProgressBar(), handled ? Times.Once() : Times.Never());
            Assert.Empty(test.Channel.SentPackets);
        }
        finally { attachment.Collectible.CollectibleBehaviors = originalBehaviors; }
    }

    /// <summary>Calls actual engine help creation and verifies the production postfix appends to the vanilla detach action.</summary>
    private static void CheckAttachedHelp()
    {
        using var test = new AttachedGestureCase();
        test.Host.Code = new AssetLocation("game:integration-host");
        var cache = new Dictionary<string, object>
        {
            ["interactionhelp-attachable-game:integration-host-0"] = new List<ItemStack> { test.Attachable.Inventory[1].Itemstack! }
        };
        test.Fixture.ApiMock.SetupGet(value => value.ObjectCache).Returns(cache);
        EnumHandling handling = EnumHandling.PassThrough;
        var interactions = test.Attachable.GetInteractionHelp(test.Fixture.ClientWorldMock!.Object,
            test.Selection, test.Player.Object, ref handling);
        Assert.Equal(2, interactions.Length);
        Assert.Equal("attachableentity-detach", interactions[0].ActionLangCode);
        Assert.Equal("vanillaexpanded:blockhelp-autostash-container", interactions[1].ActionLangCode);
        Assert.Equal(["ctrl", "shift"], interactions[1].HotKeyCodes);
        VanillaExpandedModSystem.Config.EnableAutoStash = false;
        var originalOnly = test.Attachable.GetInteractionHelp(test.Fixture.ClientWorldMock.Object,
            test.Selection, test.Player.Object, ref handling);
        Assert.Equal("attachableentity-detach", Assert.Single(originalOnly).ActionLangCode);
        Assert.Empty(test.Channel.SentPackets);
    }
    #endregion
}
