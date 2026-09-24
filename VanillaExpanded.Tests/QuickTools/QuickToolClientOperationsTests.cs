using Moq;
using VanillaExpanded.QuickTools;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.QuickTools;

/// <summary>Exercises immediate client flips and later correction handling with installed slots.</summary>
public sealed class QuickToolClientOperationsTests
{
    /// <summary>A completed local flip permits immediate restoration without server slot callbacks.</summary>
    [Fact]
    public void LocalFlip_AllowsImmediateRestoreWithoutServerUpdate()
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        Assert.False(f.Operations.IsOperating);
        Assert.True(f.Operations.HasSession);
        Assert.Single(f.Packets);
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Equal(2, f.Packets.Count);
    }

    /// <summary>Successive selections and restoration resolve current references without updates or ticks.</summary>
    [Fact]
    public void ConsecutiveSelections_CompleteWithoutServerCallbacks()
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutPick(f.Hotbar[1], 2);
        var axe = new ItemStack(new TestItem(3, EnumTool.Axe));
        f.Backpack[1].Itemstack = axe;
        QuickToolCandidate pickCandidate = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        QuickToolCandidate axeCandidate = new ToolCandidateProvider(EnumTool.Axe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", pickCandidate));
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Axe", axeCandidate));
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(axe, f.Backpack[1].Itemstack);
        Assert.Equal(4, f.Packets.Count);
    }

    /// <summary>Disablement clears context on the next API call and re-enable starts without old history.</summary>
    [Fact]
    public void UnavailablePlayer_ClearsBeforeAction()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Ready = false;
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Operations.Restore());
        Assert.False(f.Operations.HasSession);
        f.Ready = true;
        Assert.False(f.Operations.ValidateRestoration());
        Assert.Single(f.Packets);
    }

    /// <summary>A changed player inventory manager clears old history before another movement.</summary>
    [Fact]
    public void PlayerReplacement_ClearsBeforeAction()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.CurrentManager = new Mock<IPlayerInventoryManager>().Object;
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Operations.Restore());
        Assert.False(f.Operations.HasSession);
        Assert.Single(f.Packets);
    }

    /// <summary>Clearing context inside a native callback cannot unlock reentrant client operations.</summary>
    [Fact]
    public void ClearInsideNativeCallback_KeepsReentrancyGuard()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        QuickToolEquipmentResult nested = QuickToolEquipmentResult.NoOp;
        f.Hotbar.SlotModified += _ =>
        {
            f.Operations.Clear();
            Assert.True(f.Operations.IsOperating);
            nested = f.Operations.Select("tool:Pickaxe", displayed);
        };
        Assert.Equal(QuickToolEquipmentResult.Interrupted, f.Operations.Select("tool:Pickaxe", displayed));
        Assert.Equal(QuickToolEquipmentResult.Rejected, nested);
        Assert.Single(f.Packets);
        Assert.False(f.Operations.HasSession);
        Assert.False(f.Operations.IsOperating);
    }

    /// <summary>Slot callbacks and ticks do not reconcile sessions; menu validation discovers replacement.</summary>
    [Fact]
    public void CorrectiveReplacement_InvalidatesLocalSession()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        ItemStack? previous = f.Hotbar[0].Itemstack;
        f.PutPlain(f.Hotbar[0], 3);
        f.Hotbar[0].OnItemSlotModified(previous!);
        Assert.True(f.Operations.HasSession);
        f.Tick();
        Assert.True(f.Operations.HasSession);
        Assert.False(f.Operations.ValidateRestoration());
        Assert.False(f.Operations.HasSession);
        Assert.Equal(QuickToolEquipmentResult.Rejected, f.Operations.Restore());
    }

    /// <summary>An unrelated edit preserves history, while world exit discards it.</summary>
    [Fact]
    public void UnrelatedUpdateAndWorldExit_PreserveThenDiscardSession()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.PutPlain(f.Backpack[0], 3);
        f.Backpack[0].OnItemSlotModified(null!);
        f.Tick();
        Assert.True(f.Operations.HasSession);
        f.Events.Raise(e => e.LeaveWorld += null!);
        Assert.False(f.Operations.IsOperating);
        Assert.False(f.Operations.HasSession);
    }

    /// <summary>A manual active-slot event ends restoration without sending another inventory packet.</summary>
    [Fact]
    public void ManualActiveSlotEvent_DiscardsOnlySession()
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Events.Raise(e => e.AfterActiveSlotChanged += null!, new ActiveSlotChangeEventArgs(0, 1));
        Assert.False(f.Operations.HasSession);
        Assert.Single(f.Packets);
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        Assert.Same(original, f.Hotbar[1].Itemstack);
    }

    /// <summary>A refresh of the selected hotbar position keeps the exact-stack restoration session.</summary>
    [Fact]
    public void SameActiveSlotEvent_PreservesRestoration()
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Events.Raise(e => e.AfterActiveSlotChanged += null!, new ActiveSlotChangeEventArgs(0, 0));
        Assert.True(f.Operations.ValidateRestoration());
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
    }

    /// <summary>Unrelated bag topology changes do not end an intact reference-based session.</summary>
    [Fact]
    public void BackpackSlotReplacement_PreservesIntactReferences()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Backpack[0] = new ItemSlotBagContent(f.Backpack, 0, 0, EnumItemStorageFlags.General);
        f.Tick();
        Assert.True(f.Operations.HasSession);
        Assert.True(f.Operations.ValidateRestoration());
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Restore());
        Assert.False(f.Operations.IsOperating);
    }

    /// <summary>The original stack can move with or without callbacks and be restored without a tick.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovedOriginal_IsFoundAtActionTime(bool notify)
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Hotbar[1].Itemstack = null;
        f.Backpack[1].Itemstack = original;
        if (notify)
        {
            f.Hotbar[1].OnItemSlotModified(original);
            f.Backpack[1].OnItemSlotModified(null!);
        }
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Null(f.Backpack[1].Itemstack);
    }

    /// <summary>Recreated home slots are resolved by address while item identity follows the original object.</summary>
    [Fact]
    public void RecreatedHomeSlot_IsResolvedBeforeRestoration()
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        ItemStack pick = f.PutPick(f.Backpack[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Backpack[1] = new ItemSlotBagContent(f.Backpack, 0, 1, EnumItemStorageFlags.General) { Itemstack = original };
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Restore());
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Backpack[1].Itemstack);
    }

    /// <summary>A replacement without callbacks is rejected by the action itself, with no tick required.</summary>
    [Fact]
    public void SilentReplacement_InvalidatesOnRestore()
    {
        using var f = new Fixture();
        f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        ItemStack replacement = f.PutPlain(f.Hotbar[1], 1);
        Assert.Equal(QuickToolEquipmentResult.SessionInvalidated, f.Operations.Restore());
        Assert.Same(replacement, f.Hotbar[1].Itemstack);
        Assert.Single(f.Packets);
    }

    /// <summary>Availability validation checks the route and performs no movement.</summary>
    [Fact]
    public void MenuValidation_WithNoReturnRoute_DisablesRestoreWithoutDiscardingReferences()
    {
        using var f = new Fixture();
        ItemStack original = f.PutPlain(f.Hotbar[0], 1);
        f.PutPick(f.Hotbar[1], 2);
        QuickToolCandidate displayed = new ToolCandidateProvider(EnumTool.Pickaxe).Resolve(f.Manager.Object, f.Offhand)!;
        Assert.Equal(QuickToolEquipmentResult.LocallyApplied, f.Operations.Select("tool:Pickaxe", displayed));
        f.Backpack[0].Itemstack = original;
        for (int i = 1; i < 5; i++) f.PutPlain(f.Hotbar[i], 10 + i);
        for (int i = 1; i < f.Backpack.Count; i++) f.PutPlain(f.Backpack[i], 20 + i);
        Assert.False(f.Operations.ValidateRestoration());
        Assert.True(f.Operations.HasSession);
        Assert.Single(f.Packets);
    }

    /// <summary>Supplies a narrow client context with real native slot callbacks and packet capture.</summary>
    private sealed class Fixture : IDisposable
    {
        private Action<float>? tick;
        public InventoryGeneric Hotbar { get; } = new(6, "hotbar", "client-operations-test", null!,
            (i, inv) => i == 5 ? new ItemSlotOffhand(inv) : new ItemSlotSurvival(inv));
        public InventoryGeneric Backpack { get; } = new(3, "backpack", "client-operations-test", null!,
            (i, inv) => new ItemSlotBagContent(inv, 0, i, EnumItemStorageFlags.General));
        public Mock<IPlayerInventoryManager> Manager { get; } = new();
        public Mock<IClientEventAPI> Events { get; } = new();
        public ItemSlot Offhand => Hotbar[5];
        public List<object> Packets { get; } = [];
        public QuickToolClientOperations Operations { get; }
        public bool Ready { get; set; } = true;
        public IPlayerInventoryManager CurrentManager { get; set; }

        /// <summary>Wires installed inventory notifications and the native packet factory.</summary>
        public Fixture()
        {
            Events.Setup(x => x.RegisterGameTickListener(It.IsAny<Action<float>>(), 500, It.IsAny<int>()))
                .Callback<Action<float>, int, int>((callback, _, _) => tick = callback)
                .Returns(1);
            var api = new Mock<ICoreAPI>();
            api.Setup(x => x.World).Returns(new Mock<IWorldAccessor>().Object);
            Hotbar.Api = api.Object;
            Backpack.Api = api.Object;
            var network = new Mock<IInventoryNetworkUtil>();
            network.Setup(x => x.GetFlipSlotsPacket(It.IsAny<IInventory>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns(() => new object());
            Hotbar.InvNetworkUtil = network.Object;
            Backpack.InvNetworkUtil = network.Object;
            Manager.Setup(x => x.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(Hotbar);
            Manager.Setup(x => x.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(Backpack);
            Manager.Setup(x => x.ActiveHotbarSlot).Returns(() => Hotbar[0]);
            Manager.Setup(x => x.OffhandHotbarSlot).Returns(() => Offhand);
            Manager.Setup(x => x.ActiveHotbarSlotNumber).Returns(0);
            CurrentManager = Manager.Object;
            Operations = new QuickToolClientOperations(Events.Object, () => Ready,
                () => (CurrentManager, Offhand), packet => Packets.Add(packet),
                allowGenericFixture: true);
        }

        /// <summary>Advances only player-context lifecycle checks through the registered tick.</summary>
        public void Tick() => tick?.Invoke(0.5f);

        /// <summary>Places one ordinary item in a fixture slot.</summary>
        public ItemStack PutPlain(ItemSlot slot, int id)
        {
            var stack = new ItemStack(new TestItem(id));
            slot.Itemstack = stack;
            return stack;
        }

        /// <summary>Places a usable pickaxe in a fixture slot.</summary>
        public ItemStack PutPick(ItemSlot slot, int id)
        {
            var stack = new ItemStack(new TestItem(id, EnumTool.Pickaxe));
            slot.Itemstack = stack;
            return stack;
        }

        /// <summary>Releases subscriptions after each event fixture.</summary>
        public void Dispose() => Operations.Dispose();
    }

    /// <summary>Supplies deterministic collectible metadata for native client tests.</summary>
    private sealed class TestItem(int id, EnumTool? category = null) : MockItem(id)
    {
        /// <summary>Returns the optional tool category.</summary>
        public override EnumTool? GetTool(ItemSlot slot) => category;
        /// <summary>Returns a fixed tier for ranked tools.</summary>
        public override int GetToolTier(ItemSlot slot) => 1;
        /// <summary>Returns durable metadata only for tools.</summary>
        public override int GetMaxDurability(ItemStack stack) => category is null ? 0 : 100;
        /// <summary>Returns a usable durability value.</summary>
        public override int GetRemainingDurability(ItemStack stack) => category is null ? 0 : 20;
    }
}
