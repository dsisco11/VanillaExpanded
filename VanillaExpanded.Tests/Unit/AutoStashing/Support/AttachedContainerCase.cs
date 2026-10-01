using Moq;
using Newtonsoft.Json.Linq;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Wires real attached bag persistence, inventories, and entity ownership with Moq observations.</summary>
internal sealed class AttachedContainerCase
{
    public VsTestFixture Fixture { get; } = VsTestFixture.Server();
    public MockItem Item { get; }
    public MockItem Unrelated { get; }
    public MockItem Other { get; }
    public MockItem BagItem { get; }
    public ItemStack BagStack { get; private set; }
    public InventoryGeneric Attachments { get; }
    public Mock<InventoryGeneric> AttachmentsMock { get; }
    public Mock<EntityBehaviorAttachable> AttachmentMock { get; }
    public Mock<IHeldBag> BagMock { get; }
    public Mock<CollectibleBehaviorHeldBag>? VanillaMock { get; }
    public IHeldBag Bag => BagMock.Object;
    public Entity Host { get; }
    public IPlayer Player { get; }
    public AttachedContainerWorkspace? Workspace => VanillaMock?.Object.getContainerWorkspace(0, Host);
    private readonly InventoryGeneric persistedView;

    #region Public API
    #region Setup and persisted contents
    /// <summary>Creates either the real vanilla workspace path or a distinct interface-backed bag.</summary>
    public AttachedContainerCase(bool vanilla, int slots = 2)
    {
        var registry = new Mock<IClassRegistryAPI>();
        registry.Setup(value => value.CreateInvNetworkUtil(It.IsAny<InventoryBase>(), It.IsAny<ICoreAPI>())).Returns(Fixture.InvNetworkUtilMock.Object);
        Fixture.ApiMock.Setup(value => value.ClassRegistry).Returns(registry.Object);
        Fixture.ApiMock.Setup(value => value.ObjectCache).Returns(new Dictionary<string, object>());
        Fixture.WorldMock.Setup(value => value.Api).Returns(Fixture.Api);
        Fixture.WorldMock.Setup(value => value.Calendar).Returns(new Mock<IGameCalendar>().Object);
        Fixture.WorldMock.Setup(value => value.BlockAccessor).Returns(new Mock<IBlockAccessor>().Object);
        Item = new MockItem(1, api: Fixture.Api) { Code = new AssetLocation("game:attached-matching"), MaxStackSize = 64 };
        Unrelated = new MockItem(2, api: Fixture.Api) { Code = new AssetLocation("game:attached-unrelated"), MaxStackSize = 64 };
        Other = new MockItem(4, api: Fixture.Api) { Code = new AssetLocation("game:attached-other"), MaxStackSize = 64 };
        BagItem = new MockItem(3, api: Fixture.Api)
        {
            Code = new AssetLocation("game:attached-bag"),
            Attributes = new JsonObject(JObject.Parse("{\"backpack\":{\"quantitySlots\":" + slots + ",\"storageFlags\":511}}"))
        };
        foreach (var item in new[] { Item, Unrelated, BagItem, Other })
        {
            Fixture.WorldMock.Setup(value => value.GetItem(item.Id)).Returns(item);
            Fixture.WorldMock.Setup(value => value.GetItem(item.Code)).Returns(item);
        }
        Host = new EntityAgent { Api = Fixture.Api, World = Fixture.World, EntityId = 42, Code = new AssetLocation("game:test-host") };
        AttachmentsMock = new Mock<InventoryGeneric>(2, "wearablesInv", "42", (ICoreAPI)null!, (NewSlotDelegate)null!) { CallBase = true };
        Attachments = AttachmentsMock.Object;
        Attachments.Api = Fixture.Api;
        Attachments.InvNetworkUtil = Fixture.InvNetworkUtilMock.Object;
        AttachmentMock = new Mock<EntityBehaviorAttachable>(Host) { CallBase = true };
        AttachmentMock.SetupGet(value => value.Inventory).Returns(Attachments);
        var player = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IServerPlayer>();
        player.SetupGet(value => value.InventoryManager).Returns(Fixture.Player);
        player.SetupGet(value => value.PlayerName).Returns("test");
        Player = player.Object;
        if (vanilla)
        {
            VanillaMock = new Mock<CollectibleBehaviorHeldBag>(BagItem) { CallBase = true };
            BagMock = VanillaMock.As<IHeldBag>();
            BagItem.CollectibleBehaviors = [VanillaMock.Object];
        }
        else
        {
            var behavior = new Mock<CollectibleBehavior>(BagItem);
            BagMock = behavior.As<IHeldBag>();
            BagMock.Setup(value => value.GetQuantitySlots(It.IsAny<ItemStack>())).Returns(slots);
            BagMock.Setup(value => value.GetContents(It.IsAny<ItemStack>(), It.IsAny<IWorldAccessor>()))
                .Returns((ItemStack stack, IWorldAccessor world) => ReadContents(stack, world));
            BagMock.Setup(value => value.GetOrCreateSlots(It.IsAny<ItemStack>(), It.IsAny<InventoryBase>(), It.IsAny<int>(), It.IsAny<IWorldAccessor>()))
                .Returns((ItemStack stack, InventoryBase inventory, int bagIndex, IWorldAccessor world) =>
                    ReadContents(stack.Clone(), world).Select((content, index) => new ItemSlotBagContent(inventory, bagIndex, index, (EnumItemStorageFlags)511) { Itemstack = content }).ToList());
            BagMock.Setup(value => value.Store(It.IsAny<ItemStack>(), It.IsAny<ItemSlotBagContent>()))
                .Callback((ItemStack stack, ItemSlotBagContent slot) => stack.Attributes.GetTreeAttribute("backpack").GetTreeAttribute("slots")["slot-" + slot.SlotIndex] = new ItemstackAttribute(slot.Itemstack?.Clone()));
            BagItem.CollectibleBehaviors = [behavior.Object];
        }
        BagStack = new ItemStack(BagItem);
        Attachments[0].Itemstack = BagStack;
        Attachments[1].Itemstack = Stack(Other, 1);
        SeedContents(new ItemStack?[slots]);
        persistedView = new InventoryGeneric(slots, "persisted", "assertions", null!);
    }

    /// <summary>Seeds independent persisted slots in the engine bag attribute format.</summary>
    public void SeedContents(params ItemStack?[] contents)
    {
        var stored = new TreeAttribute();
        for (int index = 0; index < contents.Length; index++) stored["slot-" + index] = new ItemstackAttribute(contents[index]?.Clone());
        var backpack = new TreeAttribute();
        backpack["slots"] = stored;
        BagStack.Attributes["backpack"] = backpack;
    }

    /// <summary>Reads independent persisted stacks after resolving their actual engine collectible identities.</summary>
    public ItemStack?[] ReadPersisted() => ReadContents(BagStack.Clone(), Fixture.World);

    /// <summary>Seeds a relevant attribute to detect lost or overwritten stack data.</summary>
    public ItemStack Stack(CollectibleObject item, int count)
    {
        var stack = new ItemStack(item, count);
        stack.Attributes.SetString("fixture", "preserved");
        return stack;
    }

    /// <summary>Loads the actual workspace before session-ownership and stable-slot tests.</summary>
    public AttachedContainerWorkspace LoadWorkspace()
    {
        Assert.NotNull(VanillaMock);
        var workspace = VanillaMock.Object.getOrCreateContainerWorkspace(0, Host, AttachmentMock.Object.storeInv);
        Assert.True(workspace.TryLoadInv(Attachments[0], 0, Host));
        return workspace;
    }

    /// <summary>Reconstructs the attached stack from the real owner's serialized inventory.</summary>
    public void ReloadOwnerBag()
    {
        var restored = new InventoryGeneric(Attachments.Count, "wearablesInv", "reload", null!) { Api = Fixture.Api };
        restored.FromTreeAttributes(Host.WatchedAttributes.GetTreeAttribute(AttachmentMock.Object.InventoryClassName));
        restored.ResolveBlocksOrItems();
        BagStack = restored[0].Itemstack!;
        Assert.NotNull(BagStack);
        Attachments[0].Itemstack = BagStack;
    }

    #endregion
    #region Execution and assertions
    /// <summary>Starts a new observation interval without changing bag state, workspace identity or configured behavior.</summary>
    public void ClearObservations()
    {
        BagMock.Invocations.Clear();
        AttachmentMock.Invocations.Clear();
        AttachmentsMock.Invocations.Clear();
        Fixture.InventoryManagerMock.Invocations.Clear();
    }

    /// <summary>Captures source, attachment and independently cloned persistent contents without counting workspace aliases twice.</summary>
    public InventorySnapshot Snapshot()
    {
        RefreshPersistedView();
        return new InventorySnapshot(Fixture.BackpackInventory, Fixture.HotbarInventory, Attachments, persistedView);
    }

    /// <summary>Exercises the complete attached-container operation against its real entity and attachment.</summary>
    public bool Run(int index = 0) => EntityAttachedContainerAutoStash.TryAutoStash(Fixture.World, Player, Host, AttachmentMock.Object, index);

    /// <summary>Asserts exact persisted identities, quantities and seeded attributes independently of workspace slots.</summary>
    public void AssertPersisted(int matching, int unrelated)
    {
        ItemStack?[] contents = ReadPersisted();
        Assert.Equal(2, contents.Length);
        Assert.Same(Item, contents[0]!.Collectible);
        Assert.Equal(matching, contents[0]!.StackSize);
        Assert.Same(Unrelated, contents[1]!.Collectible);
        Assert.Equal(unrelated, contents[1]!.StackSize);
        Assert.All(contents, stack => Assert.Equal("preserved", stack!.Attributes.GetString("fixture")));
    }

    /// <summary>Checks durable conservation, untouched slots, persistence calls and exact session ownership.</summary>
    public void Verify(InventorySnapshot before, bool moved, int sessions, params ItemSlot[] changedSources)
    {
        RefreshPersistedView();
        before.AssertUnchangedExcept(moved ? [.. changedSources, Attachments[0], persistedView[0]] : []);
        before.AssertConserved();
        BagMock.Verify(bag => bag.Store(It.IsAny<ItemStack>(), It.IsAny<ItemSlotBagContent>()),
            moved ? Times.AtLeastOnce() : Times.Never());
        if (moved)
        {
            // Finalization stores every content slot, including unchanged contents, through the bag contract.
            for (int index = 0; index < persistedView.Count; index++)
            {
                int expected = index;
                BagMock.Verify(bag => bag.Store(BagStack, It.Is<ItemSlotBagContent>(slot => slot.SlotIndex == expected)), Times.AtLeastOnce());
            }
        }
        AttachmentsMock.Verify(inventory => inventory.MarkSlotDirty(0), moved ? Times.Once() : Times.Never());
        AttachmentsMock.Verify(inventory => inventory.MarkSlotDirty(It.IsAny<int>()), moved ? Times.Once() : Times.Never());
        AttachmentMock.Verify(attachment => attachment.storeInv(), moved ? Times.AtLeastOnce() : Times.Never());
        Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(It.IsAny<IInventory>()), Times.Exactly(sessions));
        Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(It.IsAny<IInventory>()), Times.Exactly(sessions));
        if (sessions != 0)
        {
            Assert.NotNull(Workspace);
            Fixture.InventoryManagerMock.Verify(manager => manager.OpenInventory(Workspace.WrapperInv), Times.Exactly(sessions));
            Fixture.InventoryManagerMock.Verify(manager => manager.CloseInventoryAndSync(Workspace.WrapperInv), Times.Exactly(sessions));
        }
        Fixture.InventoryManagerMock.Verify(manager => manager.TryTransferTo(It.IsAny<ItemSlot>(), It.IsAny<ItemSlot>(),
            ref It.Ref<ItemStackMoveOperation>.IsAny), Times.Never());
        if (moved)
        {
            // Read the real owner serialization, not merely the mutable attachment stack.
            var restored = new InventoryGeneric(Attachments.Count, "wearablesInv", "restored", null!) { Api = Fixture.Api };
            restored.FromTreeAttributes(Host.WatchedAttributes.GetTreeAttribute(AttachmentMock.Object.InventoryClassName));
            Assert.NotNull(restored[0].Itemstack);
            Assert.Equal(BagStack.Attributes.ToJsonToken().ToString(), restored[0].Itemstack!.Attributes.ToJsonToken().ToString());
            restored.ResolveBlocksOrItems();
            InventorySnapshot.AssertStack(restored[1], Other, 1);
            Assert.Equal("preserved", restored[1].Itemstack!.Attributes.GetString("fixture"));
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Refreshes only the independent assertion inventory from persisted stack data.</summary>
    private void RefreshPersistedView()
    {
        ItemStack?[] contents = ReadPersisted();
        for (int index = 0; index < persistedView.Count; index++) persistedView[index].Itemstack = contents[index];
    }
    /// <summary>Resolves cloned stack attributes at the storage boundary without simulating inventory movement.</summary>
    private static ItemStack?[] ReadContents(ItemStack stack, IWorldAccessor world)
    {
        return stack.Attributes.GetTreeAttribute("backpack").GetTreeAttribute("slots").SortedCopy()
            .Select(value => ((ItemstackAttribute)value.Value).value)
            .Select(content => { content?.ResolveBlockOrItem(world); return content; }).ToArray();
    }
    #endregion
}
