using Moq;
using VanillaExpanded.QuickTools;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.QuickTools;

/// <summary>Exercises input, cached menu content, and real native inventory operations together.</summary>
public sealed class QuickToolMenuControllerTests
{
    #region Interaction and content
    /// <summary>Selection closes once, blocks held repeats, and restores immediately after release without server callbacks.</summary>
    [Fact]
    public void SelectReleaseReopenRestore_UsesLocalInventoryImmediately()
    {
        using var f = new Fixture();
        ItemStack original = f.Put(0, 1);
        ItemStack pick = f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        Assert.Equal(new[] { "tool:Pickaxe" }, f.Menu.Layout!.WedgeIds);
        Assert.Equal("localized:quicktool-unequip", f.Entry("unequip").Label);
        Assert.Null(f.Entry("unequip").Description);
        Assert.True(f.Entry("unequip").Enabled);
        Assert.DoesNotContain("tool:Axe", f.Menu.Layout.WedgeIds);
        Assert.Equal("game-item-name:2", f.Entry("tool:Pickaxe").Label);
        Assert.True(f.Menu.Click("tool:Pickaxe"));
        Assert.False(f.Menu.Click("tool:Pickaxe"));
        Assert.False(f.Menu.IsOpen);
        Assert.True(f.Operations.HasSession);
        Assert.Same(pick, f.Hotbar[0].Itemstack);
        f.Controller.Press();
        Assert.Equal(1, f.Menu.OpenCount);
        f.Release();
        f.Open();
        Assert.True(f.Entry("unequip").Enabled);
        Assert.True(f.Menu.Click("unequip"));
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Equal(2, f.Packets.Count);
        Assert.Empty(f.Feedback);
    }

    /// <summary>Successive category actions preserve the original item across separate menu openings.</summary>
    [Fact]
    public void ChainAcrossOpenings_RestoresOriginalWithoutTicks()
    {
        using var f = new Fixture();
        ItemStack original = f.Put(0, 1);
        ItemStack pick = f.Put(1, 2, EnumTool.Pickaxe);
        ItemStack axe = f.Put(2, 3, EnumTool.Axe);
        foreach (string id in new[] { "tool:Pickaxe", "tool:Axe", "unequip" })
        {
            f.Open();
            Assert.True(f.Menu.Click(id));
            f.Release();
        }
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Same(pick, f.Hotbar[1].Itemstack);
        Assert.Same(axe, f.Hotbar[2].Itemstack);
        Assert.Equal(4, f.Packets.Count);
    }

    /// <summary>Polling never scans collectible metadata; a notified candidate removal resizes the open ring.</summary>
    [Fact]
    public void CacheRefresh_ResizesOpenLayoutWithoutInputRelease()
    {
        using var f = new Fixture();
        ItemStack light = f.Put(1, 2);
        light.Collectible.LightHsv = new byte[] { 0, 0, 15 };
        f.Open();
        Assert.True(f.Entry(QuickToolLayout.LightId).Enabled);
        Assert.NotNull(f.Entry(QuickToolLayout.LightId).Icon);
        Assert.Equal("game-item-name:2", f.Entry(QuickToolLayout.LightId).Label);
        RadialMenuLayout layout = f.Menu.Layout!;
        int refreshes = f.Cache.RefreshCount;
        for (int i = 0; i < 20; i++) f.Controller.PollInput();
        Assert.Equal(refreshes, f.Cache.RefreshCount);
        f.Hotbar[1].Itemstack = null;
        f.Hotbar[1].MarkDirty();
        f.Cache.RefreshPending();
        Assert.Empty(f.Menu.Layout!.WedgeIds);
        Assert.NotSame(layout, f.Menu.Layout);
        Assert.Equal(1, f.Menu.LayoutUpdateCount);
        Assert.True(f.Menu.IsOpen);
        Assert.Equal(1, f.Menu.OpenCount);
    }

    /// <summary>A newly available item adds one wedge in provider order and can be selected before release.</summary>
    [Fact]
    public void CacheRefresh_AddsSelectableWedgeWhileOpen()
    {
        using var f = new Fixture();
        f.Put(0, 1);
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        Assert.Equal(new[] { "tool:Pickaxe" }, f.Menu.Layout!.WedgeIds);
        f.Put(2, 3, EnumTool.Knife);
        f.Hotbar[2].MarkDirty();
        f.Cache.RefreshPending();
        Assert.Equal(new[] { "tool:Knife", "tool:Pickaxe" }, f.Menu.Layout!.WedgeIds);
        Assert.Equal(1, f.Menu.LayoutUpdateCount);
        Assert.True(f.Menu.Click("tool:Knife"));
        Assert.Equal(3, Assert.IsType<ItemStack>(f.Hotbar[0].Itemstack).Id);
        Assert.Single(f.Packets);
    }

    /// <summary>A stale visible candidate is rejected instead of substituting the new winner under the pointer.</summary>
    [Fact]
    public void CandidateReplacedAfterOpen_ReportsOnceWithoutMovement()
    {
        using var f = new Fixture();
        ItemStack original = f.Put(0, 1);
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        f.Put(1, 3, EnumTool.Pickaxe);
        Assert.True(f.Menu.Click("tool:Pickaxe"));
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.Empty(f.Packets);
        Assert.Equal(new[] { "localized:quicktool-rejected" }, f.Feedback);
    }

    /// <summary>A server-recreated stack with unchanged contents remains selectable from the visible menu snapshot.</summary>
    [Fact]
    public void CandidateRecreatedAfterOpen_RemainsSelectable()
    {
        using var f = new Fixture();
        f.Put(0, 1);
        ItemStack pick = f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();

        f.Hotbar[1].Itemstack = pick.Clone();

        Assert.True(f.Menu.Click("tool:Pickaxe"));
        Assert.Equal(2, f.Hotbar[0].Itemstack!.Id);
        Assert.Single(f.Packets);
        Assert.Empty(f.Feedback);
    }

    /// <summary>A uniquely recreated original stack remains restorable after availability validation.</summary>
    [Fact]
    public void OriginalRecreatedAfterOpen_RestoreRevalidates()
    {
        using var f = new Fixture();
        f.EquipPick();
        f.Open();
        Assert.True(f.Entry("unequip").Enabled);
        f.Put(1, 1);
        f.Menu.Click("unequip");
        Assert.Equal(2, f.Packets.Count);
        Assert.False(f.Operations.HasSession);
        Assert.Empty(f.Feedback);
    }

    /// <summary>An intact session with no safe return route does not enable the center.</summary>
    [Fact]
    public void BlockedReturnRoute_DisablesCenterDespiteSessionPresence()
    {
        using var f = new Fixture();
        f.EquipPick();
        f.Backpack[0].Itemstack = f.Hotbar[1].Itemstack;
        for (int i = 1; i < 5; i++) f.Put(i, 10 + i);
        for (int i = 1; i < f.Backpack.Count; i++) f.Backpack[i].Itemstack = new ItemStack(new TestItem(20 + i));
        f.Open();
        Assert.True(f.Operations.HasSession);
        Assert.False(f.Entry("unequip").Enabled);
        Assert.False(f.Menu.Click("unequip"));
        Assert.Single(f.Packets);
    }
    #endregion

    #region Input and lifecycle
    /// <summary>Release, ordinary dialog cancellation, and focus loss preserve restoration between menu openings.</summary>
    [Theory]
    [InlineData("release")]
    [InlineData("cancel")]
    [InlineData("focus")]
    public void MenuCancellation_PreservesEquipmentHistory(string reason)
    {
        using var f = new Fixture();
        f.EquipPick();
        f.Open();
        if (reason == "release") f.Release();
        if (reason == "cancel") f.Menu.Cancel();
        if (reason == "focus") { f.Focused = false; f.Controller.PollInput(); }
        Assert.False(f.Menu.IsOpen);
        Assert.True(f.Operations.HasSession);
        f.Focused = true;
        f.Controller.Press();
        if (reason != "release") Assert.Equal(2, f.Menu.OpenCount);
        f.Release();
        f.Open();
        Assert.True(f.Entry("unequip").Enabled);
        f.Menu.Click("unequip");
        Assert.Equal(2, f.Packets.Count);
    }

    /// <summary>Releasing the activation key selects the current enabled hover target when configured.</summary>
    [Fact]
    public void SelectOnRelease_SelectsHoveredEntry()
    {
        using var f = new Fixture { SelectOnRelease = true };
        f.Put(0, 1);
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        f.Menu.Hover("tool:Pickaxe");

        f.Release();

        Assert.False(f.Menu.IsOpen);
        Assert.Equal(2, Assert.IsType<ItemStack>(f.Hotbar[0].Itemstack).Id);
        Assert.Single(f.Packets);
    }

    /// <summary>Secondary keys and modifiers cancel when released; a held activation cannot reopen after rebinding.</summary>
    [Fact]
    public void ModifierSecondaryAndRebind_RequireActivationRelease()
    {
        using var f = new Fixture();
        f.Binding = new((int)GlKeys.K, (int)GlKeys.J, true, false, false, false);
        f.Down.Add((int)GlKeys.J);
        f.Down.Add((int)GlKeys.ControlRight);
        f.Open();
        f.Controller.PollInput((int)GlKeys.ControlRight);
        Assert.False(f.Menu.IsOpen);
        f.Controller.Press();
        Assert.Equal(1, f.Menu.OpenCount);
        f.Release();
        f.Down.Remove((int)GlKeys.J);
        f.Controller.PollInput();
        f.Down.Add((int)GlKeys.J);
        f.Open();
        Assert.Equal(2, f.Menu.OpenCount);
        f.Binding = new((int)GlKeys.L, 0, false, false, false, false);
        f.Down.Add((int)GlKeys.L);
        f.Controller.PollInput();
        Assert.False(f.Menu.IsOpen);
        f.Controller.Press();
        Assert.Equal(2, f.Menu.OpenCount);
        f.Down.Clear();
        f.Controller.PollInput();
        f.Open();
        Assert.Equal(3, f.Menu.OpenCount);
    }

    /// <summary>Ambiguous primary-mouse and release-only mappings never open an interaction.</summary>
    [Theory]
    [InlineData(KeyCombination.MouseStart, false)]
    [InlineData((int)GlKeys.K, true)]
    [InlineData(0, false)]
    public void UnsupportedMapping_DoesNotOpen(int primary, bool onKeyUp)
    {
        using var f = new Fixture();
        f.Binding = new(primary, 0, false, false, false, onKeyUp);
        Assert.False(f.Controller.Press());
        Assert.Equal(0, f.Menu.OpenCount);
        Assert.Empty(f.Packets);
    }

    /// <summary>Disablement and player-context replacement discard the old session and menu references.</summary>
    [Theory]
    [InlineData("disabled")]
    [InlineData("replacement")]
    [InlineData("entity")]
    [InlineData("world")]
    public void EquipmentLifecycle_ClearsHistory(string reason)
    {
        using var f = new Fixture();
        f.EquipPick();
        f.Open();
        if (reason == "disabled") { f.Ready = false; f.Controller.PollInput(); }
        if (reason == "replacement") { f.CurrentManager = new Mock<IPlayerInventoryManager>().Object; f.Controller.RefreshContext(); }
        if (reason == "entity") { f.PlayerIdentity = new object(); f.Controller.PollInput(); }
        if (reason == "world") f.Controller.ClearContext();
        Assert.False(f.Menu.IsOpen);
        Assert.False(f.Operations.HasSession);
        Assert.Single(f.Packets);
    }

    /// <summary>Unavailable context after opening yields one message even before the equipment result event could fire.</summary>
    [Fact]
    public void ReadinessLostAfterOpen_ReportsOnceWithoutMovement()
    {
        using var f = new Fixture();
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        f.Ready = false;
        f.Menu.Click("tool:Pickaxe");
        Assert.Single(f.Feedback);
        Assert.Empty(f.Packets);
    }

    /// <summary>An equipment early return is reported once even though LocalResult was never raised.</summary>
    [Fact]
    public void EquipmentEarlyRejection_UsesReturnValueWithoutResultEvent()
    {
        using var f = new Fixture();
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        int results = 0;
        f.Operations.LocalResult += _ => results++;
        f.EquipmentReady = false;
        f.Menu.Click("tool:Pickaxe");
        Assert.Equal(0, results);
        Assert.Equal(new[] { "localized:quicktool-rejected" }, f.Feedback);
        Assert.Empty(f.Packets);
    }

    /// <summary>A replacement entity reusing the same inventory cannot receive a selection from the old menu.</summary>
    [Fact]
    public void EntityReplacedBeforeClick_RejectsOldInteraction()
    {
        using var f = new Fixture();
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        f.PlayerIdentity = new object();
        f.Menu.Click("tool:Pickaxe");
        Assert.Empty(f.Packets);
        Assert.Single(f.Feedback);
    }

    /// <summary>Re-enabling while the activation remains held cannot reopen or recover discarded history.</summary>
    [Fact]
    public void Reenable_RequiresReleaseAndDropsOldHistory()
    {
        using var f = new Fixture();
        f.EquipPick();
        f.Open();
        f.Ready = false;
        f.Controller.PollInput();
        f.Ready = true;
        f.Controller.Press();
        Assert.False(f.Menu.IsOpen);
        Assert.False(f.Operations.HasSession);
        f.Release();
        f.Open();
        Assert.True(f.Entry("unequip").Enabled);
    }

    /// <summary>The action finds the original object at its new location after the menu was opened.</summary>
    [Fact]
    public void OriginalMovedAfterOpen_RestoreFindsCurrentSlot()
    {
        using var f = new Fixture();
        f.EquipPick();
        ItemStack original = Assert.IsType<ItemStack>(f.Hotbar[1].Itemstack);
        f.Open();
        f.Backpack[0].Itemstack = original;
        f.Hotbar[1].Itemstack = null;
        f.Menu.Click("unequip");
        Assert.Same(original, f.Hotbar[0].Itemstack);
        Assert.True(f.Backpack[0].Empty);
        Assert.Empty(f.Feedback);
    }

    /// <summary>Tool and virtual-light entries use the same native equipment history.</summary>
    [Fact]
    public void ToolLightChain_UsesSharedRestoration()
    {
        using var f = new Fixture();
        f.EquipPick();
        f.Put(2, 3).Collectible.LightHsv = new byte[] { 0, 0, 15 };
        f.Hotbar[2].MarkDirty();
        f.Open();
        Assert.True(f.Menu.Click(QuickToolLayout.LightId));
        f.Release();
        f.Open();
        Assert.True(f.Menu.Click("unequip"));
        Assert.Equal(1, Assert.IsType<ItemStack>(f.Hotbar[0].Itemstack).Id);
        Assert.Equal(2, Assert.IsType<ItemStack>(f.Hotbar[1].Itemstack).Id);
        Assert.Equal(3, Assert.IsType<ItemStack>(f.Hotbar[2].Itemstack).Id);
        Assert.Equal(4, f.Packets.Count);
    }

    /// <summary>A native callback cannot reopen or replay the selected menu action, even when it interrupts the operation.</summary>
    [Fact]
    public void NativeCallback_ReentrantPressAndLifecycleClearCannotRepeatAction()
    {
        using var f = new Fixture();
        f.Put(0, 1);
        f.Put(1, 2, EnumTool.Pickaxe);
        f.Open();
        f.Hotbar.SlotModified += _ =>
        {
            f.Controller.Press();
            f.Controller.ClearContext();
            Assert.True(f.Operations.IsOperating);
        };
        f.Menu.Click("tool:Pickaxe");
        Assert.Equal(1, f.Menu.OpenCount);
        Assert.Single(f.Packets);
        Assert.False(f.Operations.HasSession);
        Assert.Equal(new[] { "localized:quicktool-interrupted" }, f.Feedback);
    }
    #endregion

    #region Fixtures
    /// <summary>Combines installed inventory slots with narrow event, input, menu, and player-context seams.</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly InventoryGeneric Hotbar = new(6, "hotbar", "menu-test", null!,
            (i, inv) => i == 5 ? new ItemSlotOffhand(inv) : new ItemSlotSurvival(inv));
        internal readonly InventoryGeneric Backpack = new(3, "backpack", "menu-test", null!,
            (i, inv) => new ItemSlotBagContent(inv, 0, i, EnumItemStorageFlags.General));
        internal readonly Mock<IClientEventAPI> Events = new();
        internal readonly QuickToolCandidateCache Cache = new();
        internal readonly Menu Menu = new();
        internal readonly HashSet<int> Down = [];
        internal readonly List<object> Packets = [];
        internal readonly List<string> Feedback = [];
        internal readonly QuickToolClientOperations Operations;
        internal readonly QuickToolMenuController Controller;
        internal IPlayerInventoryManager CurrentManager;
        internal QuickToolBinding Binding = new((int)GlKeys.K, 0, false, false, false, false);
        internal bool Ready = true;
        internal bool EquipmentReady = true;
        internal bool Focused = true;
        internal bool SelectOnRelease;
        internal object PlayerIdentity = new();

        /// <summary>Configures real native packet creation and immediate inventory mutation.</summary>
        internal Fixture()
        {
            var api = new Mock<ICoreAPI>();
            api.Setup(x => x.World).Returns(new Mock<IWorldAccessor>().Object);
            Hotbar.Api = Backpack.Api = api.Object;
            var network = new Mock<IInventoryNetworkUtil>();
            network.Setup(x => x.GetFlipSlotsPacket(It.IsAny<IInventory>(), It.IsAny<int>(), It.IsAny<int>())).Returns(() => new object());
            Hotbar.InvNetworkUtil = Backpack.InvNetworkUtil = network.Object;
            var manager = new Mock<IPlayerInventoryManager>();
            manager.Setup(x => x.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(Hotbar);
            manager.Setup(x => x.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(Backpack);
            manager.Setup(x => x.ActiveHotbarSlot).Returns(() => Hotbar[0]);
            manager.Setup(x => x.OffhandHotbarSlot).Returns(Hotbar[5]);
            manager.Setup(x => x.ActiveHotbarSlotNumber).Returns(0);
            CurrentManager = manager.Object;
            Operations = new QuickToolClientOperations(Events.Object, () => Ready && EquipmentReady,
                () => (CurrentManager, Hotbar[5]), Packets.Add, allowGenericFixture: true);
            Controller = new QuickToolMenuController(Operations, Cache, Menu, Events.Object, () => Ready,
                () => (CurrentManager, Hotbar[5]), () => Binding, Down.Contains, () => Focused,
                () => SelectOnRelease,
                key => "localized:" + key, Feedback.Add, () => PlayerIdentity);
        }

        /// <summary>Places a deterministic stack without raising a synthetic server notification.</summary>
        internal ItemStack Put(int slot, int id, EnumTool? tool = null)
        {
            var stack = new ItemStack(new TestItem(id, tool));
            Hotbar[slot].Itemstack = stack;
            return stack;
        }
        /// <summary>Presses the activation key through the production coordinator.</summary>
        internal void Open()
        {
            Down.Add(Binding.Primary);
            Assert.True(Controller.Press());
            Assert.True(Menu.IsOpen);
        }
        /// <summary>Observes physical release before the next independent press.</summary>
        internal void Release()
        {
            Down.Remove(Binding.Primary);
            Controller.PollInput();
        }
        /// <summary>Creates restoration history through the complete menu-to-native-operation path.</summary>
        internal void EquipPick()
        {
            Put(0, 1);
            Put(1, 2, EnumTool.Pickaxe);
            Open();
            Assert.True(Menu.Click("tool:Pickaxe"));
            Release();
        }
        /// <summary>Returns currently visible content.</summary>
        internal RadialMenuEntry Entry(string id) => Menu.Interaction!.GetEntry(id);
        /// <summary>Disposes integration owners in production order.</summary>
        public void Dispose()
        {
            Controller.Dispose();
            Cache.Dispose();
            Operations.Dispose();
        }
    }

    /// <summary>Replaces the graphics host while retaining actual radial hit testing and single-selection behavior.</summary>
    private sealed class Menu : IRadialMenu
    {
        internal RadialMenuInteraction? Interaction;
        internal RadialMenuLayout? Layout;
        internal int OpenCount;
        internal int LayoutUpdateCount;
        /// <inheritdoc />
        public bool IsOpen => Interaction?.IsOpen == true;
        /// <inheritdoc />
        public bool Open(RadialMenuLayout layout, IEnumerable<RadialMenuEntry> entries, Action<string> selected, Action cancelled)
        {
            Layout = layout;
            Interaction = new RadialMenuInteraction(layout, entries);
            Interaction.Selected += selected;
            Interaction.Cancelled += cancelled;
            Interaction.Open();
            OpenCount++;
            return true;
        }
        /// <inheritdoc />
        public void UpdateEntries(IEnumerable<RadialMenuEntry> entries) => Interaction!.UpdateEntries(entries);
        /// <inheritdoc />
        public void UpdateLayout(RadialMenuLayout layout, IEnumerable<RadialMenuEntry> entries)
        {
            Interaction!.UpdateLayout(layout, entries);
            Layout = layout;
            LayoutUpdateCount++;
        }
        /// <inheritdoc />
        public bool SelectHovered() => Interaction?.SelectHovered() == true;
        /// <inheritdoc />
        public void Cancel() => Interaction?.Cancel();
        /// <summary>Clicks the real center or wedge center through shared hit testing.</summary>
        internal bool Click(string id)
        {
            Hover(id);
            return Interaction.SelectHovered();
        }
        /// <summary>Moves the shared interaction pointer to the specified center or wedge target.</summary>
        internal void Hover(string id)
        {
            int index = Layout!.WedgeIds.ToList().IndexOf(id);
            (double x, double y) = index < 0 ? (0, 0) : Layout.GetWedgeCenter(index, 0, 0, 100, 0.6);
            Interaction!.MovePointer(x, y, 0, 0, 100);
        }
    }

    /// <summary>Provides stable metadata for tools and ordinary whole-stack items.</summary>
    private sealed class TestItem(int id, EnumTool? tool = null) : MockItem(id)
    {
        /// <inheritdoc />
        public override EnumTool? GetTool(ItemSlot slot) => tool;
        /// <summary>Identifies the game item-name path independently of category translations.</summary>
        public override string GetHeldItemName(ItemStack stack) => "game-item-name:" + Id;
        /// <inheritdoc />
        public override int GetToolTier(ItemSlot slot) => 1;
        /// <inheritdoc />
        public override int GetMaxDurability(ItemStack stack) => tool is null ? 0 : 100;
        /// <inheritdoc />
        public override int GetRemainingDurability(ItemStack stack) => tool is null ? 0 : 20;
    }
    #endregion
}
