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
            CheckQuernGesture(false);
            CheckQuernGesture(true);
            CheckQuernOpeningOrder();
            CheckServerQuernGesture(false);
            CheckServerQuernGesture(true);
            foreach (string reason in new[] { "upper", "open", "disabled", "ineligible", "server" })
                CheckQuernVanillaDispatch(reason);
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
    private static void CheckQuernOpeningOrder()
    {
        using var test = new BlockGestureCase("quern");
        var block = (BlockQuern)test.Behavior.block;
        var container = new BlockBehaviorContainer(block);
        block.BlockBehaviors = [test.Behavior, container];
        block.CollectibleBehaviors = [test.Behavior, container];
        var claims = new Mock<ILandClaimAPI>();
        claims.Setup(value => value.TryAccess(test.Player.Object, test.Selection.Position, EnumBlockAccessFlags.Use)).Returns(true);
        test.Fixture.WorldMock.SetupGet(value => value.Claims).Returns(claims.Object);
        var events = new Mock<IClientEventAPI>();
        var queued = new Queue<Action>();
        events.Setup(value => value.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((action, _) => queued.Enqueue(action));
        test.Fixture.ClientApiMock!.SetupGet(value => value.Event).Returns(events.Object);
        var quern = Mock.Get((BlockEntityQuern)test.Fixture.World.BlockAccessor.GetBlockEntity(test.Selection.Position));
        var order = new List<string>();
        quern.Setup(value => value.OnPlayerRightClick(test.Player.Object, It.IsAny<BlockSelection>()))
            .Callback(() =>
            {
                order.Add("inventory-open");
                test.TargetInventory.Open(test.Player.Object);
            }).Returns(true);

        Assert.True(block.OnBlockInteractStart(test.Fixture.World, test.Player.Object, test.Selection));
        Assert.Empty(order);
        // The engine sends its normal interaction-start packet immediately after the block callback returns.
        order.Add("interaction-start");
        Assert.Single(queued);
        queued.Dequeue()();
        Assert.Equal(new[] { "interaction-start", "inventory-open" }, order);
        Assert.True(block.OnBlockInteractStep(.5f, test.Fixture.World, test.Player.Object, test.Selection));
        Assert.Single(test.Requests);
        block.OnBlockInteractStop(.5f, test.Fixture.World, test.Player.Object, test.Selection);
    }

    private static void CheckServerQuernGesture(bool cancel)
    {
        using var test = new BlockGestureCase("quern");
        var server = VanillaExpanded.Tests.Mocks.VsTestFixture.Server();
        server.WorldMock.SetupGet(value => value.BlockAccessor).Returns(test.Fixture.World.BlockAccessor);
        test.Fixture.WorldMock.SetupGet(value => value.Side).Returns(EnumAppSide.Server);
        test.Player.SetupGet(value => value.PlayerUID).Returns("quern-server-player");
        var block = (BlockQuern)test.Behavior.block;
        var quern = (BlockEntityQuern)test.Fixture.World.BlockAccessor.GetBlockEntity(test.Selection.Position);
        quern.Api = server.Api;
        var container = new BlockBehaviorContainer(block);
        block.BlockBehaviors = [test.Behavior, container];
        block.CollectibleBehaviors = [test.Behavior, container];
        var claims = new Mock<ILandClaimAPI>();
        claims.Setup(value => value.TryAccess(test.Player.Object, test.Selection.Position, EnumBlockAccessFlags.Use)).Returns(true);
        test.Fixture.WorldMock.SetupGet(value => value.Claims).Returns(claims.Object);
        var grinding = typeof(BlockEntityQuern).GetField("quantityPlayersGrinding",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.True(block.OnBlockInteractStart(test.Fixture.World, test.Player.Object, test.Selection));
        test.TargetInventory.Open(test.Player.Object);
        Assert.True(block.OnBlockInteractStep(.25f, test.Fixture.World, test.Player.Object, test.Selection));
        Assert.Equal(0, (int)grinding.GetValue(quern)!);
        if (cancel)
            block.OnBlockInteractCancel(.25f, test.Fixture.World, test.Player.Object, test.Selection,
                EnumItemUseCancelReason.ReleasedMouse);
        else block.OnBlockInteractStop(.25f, test.Fixture.World, test.Player.Object, test.Selection);

        // A fresh interaction with the window already open must still grind normally.
        Assert.True(block.OnBlockInteractStart(test.Fixture.World, test.Player.Object, test.Selection));
        Assert.True(block.OnBlockInteractStep(.25f, test.Fixture.World, test.Player.Object, test.Selection));
        Assert.Equal(1, (int)grinding.GetValue(quern)!);
        block.OnBlockInteractStop(.25f, test.Fixture.World, test.Player.Object, test.Selection);
        Assert.Equal(0, (int)grinding.GetValue(quern)!);
    }

    private static void CheckQuernGesture(bool cancel)
    {
        using var test = new BlockGestureCase("quern");
        var block = (BlockQuern)test.Behavior.block;
        var claims = new Mock<ILandClaimAPI>();
        claims.Setup(value => value.TryAccess(test.Player.Object, test.Selection.Position, EnumBlockAccessFlags.Use)).Returns(true);
        test.Fixture.WorldMock.SetupGet(value => value.Claims).Returns(claims.Object);
        var accessor = Mock.Get(test.Fixture.World.BlockAccessor);
        accessor.Invocations.Clear();

        Assert.True(block.OnBlockInteractStart(test.Fixture.World, test.Player.Object, test.Selection));
        Assert.True(test.Behavior.HasActiveGesture(test.Fixture.World));
        accessor.Verify(value => value.GetBlockEntity(test.Selection.Position), Times.Exactly(2));
        test.TargetInventory.Open(test.Player.Object);
        test.Fixture.InventoryManagerMock.SetupGet(value => value.OpenedInventories).Returns([test.TargetInventory]);
        accessor.Invocations.Clear();
        Assert.True(block.OnBlockInteractStep(.25f, test.Fixture.World, test.Player.Object, test.Selection));
        Assert.True(block.OnBlockInteractStep(.5f, test.Fixture.World, test.Player.Object, test.Selection));
        Assert.True(block.OnBlockInteractStep(.6f, test.Fixture.World, test.Player.Object, test.Selection));
        Assert.Single(test.Requests);
        accessor.Verify(value => value.GetBlockEntity(It.IsAny<BlockPos>()), Times.Never);
        if (cancel)
            Assert.True(block.OnBlockInteractCancel(.6f, test.Fixture.World, test.Player.Object,
                test.Selection, EnumItemUseCancelReason.ReleasedMouse));
        else block.OnBlockInteractStop(.6f, test.Fixture.World, test.Player.Object, test.Selection);
        Assert.False(test.Behavior.HasActiveGesture(test.Fixture.World));
        accessor.Verify(value => value.GetBlockEntity(It.IsAny<BlockPos>()), Times.Never);
        test.ProgressProvider.Verify(value => value.RemoveProgressBar(test.Progress.Object), Times.Once);
    }

    private static void CheckQuernVanillaDispatch(string reason)
    {
        using var test = new BlockGestureCase("quern");
        var block = (BlockQuern)test.Behavior.block;
        if (reason == "upper") test.Selection.SelectionBoxIndex = 1;
        if (reason == "open")
            test.Fixture.InventoryManagerMock.SetupGet(value => value.OpenedInventories).Returns([test.TargetInventory]);
        if (reason == "disabled") VanillaExpandedModSystem.Config.EnableAutoStash = false;
        if (reason == "ineligible") test.TargetInventory[0].Itemstack = null;
        if (reason == "server") test.Fixture.WorldMock.SetupGet(value => value.Side).Returns(EnumAppSide.Server);
        test.Start();
        Assert.False(test.Behavior.HasActiveGesture(test.Fixture.World));
        var accessor = Mock.Get(test.Fixture.World.BlockAccessor);
        var vanilla = new InvalidOperationException("vanilla quern lifecycle reached its world lookup");
        accessor.Setup(value => value.GetBlockEntity(test.Selection.Position)).Throws(vanilla);
        Assert.Same(vanilla, Assert.Throws<InvalidOperationException>(() =>
            block.OnBlockInteractStep(.5f, test.Fixture.World, test.Player.Object, test.Selection)));
        Assert.Same(vanilla, Assert.Throws<InvalidOperationException>(() =>
            block.OnBlockInteractStop(.5f, test.Fixture.World, test.Player.Object, test.Selection)));
        Assert.Same(vanilla, Assert.Throws<InvalidOperationException>(() =>
            block.OnBlockInteractCancel(.5f, test.Fixture.World, test.Player.Object, test.Selection,
                EnumItemUseCancelReason.ReleasedMouse)));
        Assert.Empty(test.Requests);
    }

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
            test.ProgressProvider.Verify(value => value.CreateProgressBar(It.IsAny<bool>()), Times.Never());
            test.Tick(.1f);
            test.ProgressProvider.Verify(value => value.CreateProgressBar(true), handled ? Times.Once() : Times.Never());
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
