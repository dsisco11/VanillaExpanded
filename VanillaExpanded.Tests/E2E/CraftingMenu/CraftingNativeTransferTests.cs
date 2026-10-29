using Moq;
using VanillaExpanded.CraftingMenu;
using VanillaExpanded.Tests.Unit.CraftingMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.E2E.CraftingMenu;

[Trait("Category", "E2E")]
public sealed class CraftingNativeTransferTests
{
    [Fact]
    public void NativePartialRefusalSendsPartialPacketKeepsAcceptedItemsAndStops()
    {
        using var f = new TransferFixture(2, 4, positions: 2);
        f.F.Hotbar[0] = new PartialSlot(f.F.Hotbar) { Itemstack = f.Ingredient(4) };
        var result = f.Coordinator.Execute(f.Entry, false);
        Assert.Equal(CraftingDispatchStatus.Interrupted, result.Status);
        var packet = Assert.IsType<Packet_Client>(Assert.Single(f.Packets));
        Assert.Equal(1, packet.MoveItemstack.Quantity);
        Assert.Equal(1, f.F.Grid[0].StackSize);
        Assert.True(f.F.Grid[1].Empty);
        Assert.Equal(4, f.F.Grid[0].StackSize + f.F.Hotbar[0].StackSize);
        Assert.Single(f.Notifications);
        Assert.Equal(0, f.Refreshes);
    }

    [Fact]
    public void NativeSynchronizationRefreshesCurrentInventoryWithoutClassifyingServerOutcomes()
    {
        using var f = new TransferFixture(2, 4, positions: 2);
        var initial = f.Ingredient(4);
        var intermediate = f.Ingredient(2);
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        f.Refresh();
        Assert.Equal(1, f.Refreshes);
        var world = Resolver(initial);
        var network = new InventoryNetworkUtil(f.F.Hotbar, f.F.Api.Object);
        Apply(network, world, f.F.Hotbar, 0, intermediate);
        Assert.Empty(f.Notifications);
        Apply(network, world, f.F.Hotbar, 0, null);
        Assert.Empty(f.Notifications);
        f.Refresh();
        Assert.Equal(2, f.Refreshes);
        // A later click is legal even though grid updates for the first action are still in flight.
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, true).Status);
        var gridNetwork = new InventoryNetworkUtil(f.F.Grid, f.F.Api.Object);
        Apply(gridNetwork, world, f.F.Grid, 0, f.Ingredient(1));
        Assert.Empty(f.Notifications);
        Assert.Equal(1, f.F.Grid[0].StackSize);
        // Native updates continue normally; the feature neither classifies nor replays them.
        Apply(gridNetwork, world, f.F.Grid, 1, intermediate);
        Assert.Equal(2, f.F.Grid[1].StackSize);
        Assert.Empty(f.Notifications);
        f.Refresh();
        Assert.Equal(3, f.Refreshes);
    }

    [Fact]
    public void PlayerAndPausedNativeUpdatesUseOrdinarySlotNotifications()
    {
        using var f = new TransferFixture(2, 2);
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        f.Refresh();
        var world = Resolver(f.Ingredient(1));
        // No real client/settings startup: the update does not touch the selected hotbar.
        var network = new PlayerInventoryNetworkUtil(f.F.Backpack, f.F.Api.Object);
        Apply(network, world, f.F.Backpack, 4, f.Ingredient(1));
        f.Refresh();
        Assert.Equal(2, f.Refreshes);
        var plain = new InventoryNetworkUtil(f.F.Grid, f.F.Api.Object);
        plain.PauseInventoryUpdates = true;
        Apply(plain, world, f.F.Grid, 0, f.Ingredient(1));
        Assert.Equal(2, f.Refreshes);
        Assert.Empty(f.RefreshQueue);
        Assert.Equal(2, f.F.Grid[0].StackSize);
        // The native queue uses Api.World when released.
        f.F.Api.Setup(api => api.World).Returns(world);
        plain.PauseInventoryUpdates = false;
        f.Refresh();
        Assert.Equal(3, f.Refreshes);
        Assert.Equal(1, f.F.Grid[0].StackSize);
        Assert.Empty(f.Notifications);
    }

    [Fact]
    public void NativeContentsAndDoubleUpdatesScheduleCoalescedRefreshes()
    {
        using var f = new TransferFixture(2, 2);
        var world = Resolver(f.Ingredient(1));
        var network = new InventoryNetworkUtil(f.F.Grid, f.F.Api.Object);
        var contents = new Packet_InventoryContents();
        contents.SetItemstacks(new[] { StackConverter.ToPacket(f.Ingredient(1)) });
        network.UpdateFromPacket(world, contents);
        f.Refresh();
        Assert.Equal(1, f.Refreshes);
        var doubleUpdate = new Packet_InventoryDoubleUpdate
        {
            InventoryId1 = f.F.Grid.InventoryID, SlotId1 = 0, ItemStack1 = StackConverter.ToPacket(f.Ingredient(2)),
            InventoryId2 = f.F.Grid.InventoryID, SlotId2 = 1, ItemStack2 = StackConverter.ToPacket(f.Ingredient(1))
        };
        network.UpdateFromPacket(world, doubleUpdate);
        Assert.Single(f.RefreshQueue);
        f.Refresh();
        Assert.Equal(2, f.Refreshes);
        Assert.Equal(2, f.F.Grid[0].StackSize);
        Assert.Equal(1, f.F.Grid[1].StackSize);
        Assert.Empty(f.Notifications);
        f.Coordinator.Dispose();
        Apply(network, world, f.F.Grid, 0, null);
        f.Refresh();
        Assert.Equal(2, f.Refreshes);
    }

    [Fact]
    public void NativeCraftingContentsRefreshGapIsExplicitWithoutPacketInterception()
    {
        using var f = new TransferFixture(2, 2);
        f.F.Catalogue.Index.Clear();
        var grid = new InventoryCraftingGrid("craftinggrid-native", null!) { Api = f.F.Api.Object };
        grid.InvNetworkUtil.Api = f.F.Api.Object;
        var world = Resolver(f.Ingredient(1));
        int slotEvents = 0;
        int dirtyEvents = 0;
        grid.SlotModified += _ => slotEvents++;
        grid[0].MarkedDirty += () => { dirtyEvents++; return false; };
        grid[9].MarkedDirty += () => { dirtyEvents++; return false; };
        var contents = new Packet_InventoryContents();
        contents.SetItemstacks(new[] { StackConverter.ToPacket(f.Ingredient(1)) });
        ((CraftingInventoryNetworkUtil)grid.InvNetworkUtil).UpdateFromPacket(world, contents);
        Assert.Equal(1, grid[0].StackSize);
        Assert.Equal(0, slotEvents);
        Assert.Equal(0, dirtyEvents);
        Assert.Contains(9, grid.DirtySlots);
        Assert.True(grid[9].Empty);
    }

    [Fact]
    public void NativeCraftingContentsIsObservedBeforeNativeGuiClearsOutputDirtyState()
    {
        using var f = new TransferFixture(2, 2);
        f.F.Catalogue.Index.Clear();
        var grid = new InventoryCraftingGrid("craftinggrid-native", null!) { Api = f.F.Api.Object };
        grid.InvNetworkUtil.Api = f.F.Api.Object;
        var world = Resolver(f.Ingredient(1));
        var api = new Mock<ICoreClientAPI>();
        var gui = new Mock<IGuiAPI>();
        gui.SetupGet(value => value.Icons).Returns(new IconUtil(api.Object));
        api.SetupGet(value => value.Gui).Returns(gui.Object);
        var bridge = new GuiElementCraftingRefresh(api.Object, ElementBounds.Fixed(0, 0, 0, 0),
            grid, f.Coordinator.QueueRefresh);
        var output = new GuiElementItemSlotGrid(api.Object, grid, _ => { }, 1,
            new[] { 9 }, ElementBounds.Fixed(0, 0, 48, 48));
        // This is the flag set by native composer construction for the final grid element.
        typeof(GuiElementItemSlotGridBase).GetField("isLastSlotGridInComposite",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(output, true);
        var contents = new Packet_InventoryContents();
        contents.SetItemstacks(new[] { StackConverter.ToPacket(f.Ingredient(1)) });
        ((CraftingInventoryNetworkUtil)grid.InvNetworkUtil).UpdateFromPacket(world, contents);
        Assert.Contains(9, grid.DirtySlots);
        Assert.True(bridge.DrawOrder < output.DrawOrder);
        bridge.PostRenderInteractiveElements(0);
        bridge.PostRenderInteractiveElements(0);
        Assert.Single(f.RefreshQueue);
        // No composition or graphics context is needed for an empty native output.
        output.PostRenderInteractiveElements(0);
        Assert.DoesNotContain(9, grid.DirtySlots);
        f.Refresh();
        Assert.Equal(1, f.Refreshes);
        bridge.PostRenderInteractiveElements(0);
        Assert.Empty(f.RefreshQueue);
        f.Coordinator.Dispose();
        ((CraftingInventoryNetworkUtil)grid.InvNetworkUtil).UpdateFromPacket(world, contents);
        bridge.PostRenderInteractiveElements(0);
        Assert.Empty(f.RefreshQueue);
    }

    [Fact]
    public void ConcurrentInventoryChangeStopsDispatchWithoutCancellingInFlightPackets()
    {
        using var f = new TransferFixture(2, 4, positions: 2);
        var world = Resolver(f.Ingredient(1));
        var gridNetwork = new InventoryNetworkUtil(f.F.Grid, f.F.Api.Object);
        f.OnSend = _ => Apply(gridNetwork, world, f.F.Grid, 0, f.Ingredient(1));
        var result = f.Coordinator.Execute(f.Entry, false);
        Assert.Equal(CraftingDispatchStatus.Interrupted, result.Status);
        Assert.Single(f.Packets);
        Assert.Single(f.Notifications);
        Assert.True(f.F.Grid[1].Empty);
        Assert.Equal(1, f.F.Grid[0].StackSize);
        Apply(gridNetwork, world, f.F.Grid, 0, f.Ingredient(2));
        var sourceNetwork = new InventoryNetworkUtil(f.F.Hotbar, f.F.Api.Object);
        Apply(sourceNetwork, world, f.F.Hotbar, 0, f.Ingredient(2));
        Assert.Equal(4, f.F.Hotbar[0].StackSize + f.F.Grid[0].StackSize);
        Assert.Single(f.Notifications);
        f.OnSend = null;
        Assert.Equal(CraftingDispatchStatus.Dispatched, f.Coordinator.Execute(f.Entry, false).Status);
        Assert.Equal(2, f.F.Grid[1].StackSize);
    }

    private static IClientWorldAccessor Resolver(ItemStack sample)
    {
        var world = new Mock<IClientWorldAccessor>();
        world.Setup(resolver => resolver.GetItem(sample.Id)).Returns(sample.Item);
        return world.Object;
    }

    private static void Apply(InventoryNetworkUtil network, IWorldAccessor world, InventoryBase inventory,
        int index, ItemStack? stack) => network.UpdateFromPacket(world, new Packet_InventoryUpdate {
            InventoryId = inventory.InventoryID, SlotId = index,
            ItemStack = stack == null ? null : StackConverter.ToPacket(stack)
        });

    private sealed class PartialSlot(InventoryBase inventory) : ItemSlot(inventory)
    {
        public override int TryPutInto(ItemSlot sinkSlot, ref ItemStackMoveOperation op)
        {
            op.RequestedQuantity = 1;
            return base.TryPutInto(sinkSlot, ref op);
        }
    }

}
