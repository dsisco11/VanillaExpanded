using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal enum CraftingDispatchStatus { Rejected, Interrupted, Dispatched }

// Dispatched is a local outcome, never an authoritative acknowledgement.
internal sealed record CraftingDispatchResult(CraftingDispatchStatus Status, string? Reason, int Packets);

internal sealed class CraftingTransferCoordinator : IDisposable
{
    internal const string Stale = "vanillaexpanded:craftingmenu-placement-stale";
    internal const string Busy = "vanillaexpanded:craftingmenu-placement-busy";
    internal const string Interrupted = "vanillaexpanded:craftingmenu-placement-interrupted";
    private readonly IPlayer player;
    private readonly IWorldAccessor world;
    private readonly CraftingAllocator allocator;
    private readonly Action<object> send;
    private readonly Action<CraftingDispatchResult> notify;
    private readonly Action<Action> enqueue;
    private readonly bool fixture;
    private readonly int thread;
    private readonly ICoreClientAPI? api;
    private readonly InventoryBase grid;
    private readonly List<(InventoryBase Inventory, Action<int> Handler)> subscriptions = new();
    private bool executing;
    private bool disposed;
    private bool refreshQueued;

    internal CraftingTransferCoordinator(ICoreClientAPI api, CraftingAllocator allocator,
        Action<CraftingDispatchResult> notify)
        : this(api.World.Player, api.World, allocator, api.Network.SendPacketClient,
            notify, action => api.Event.EnqueueMainThreadTask(action, "vanillaexpanded-crafting-inventory"),
            RuntimeEnv.MainThreadId, false)
    {
        this.api = api;
        api.Event.LeaveWorld += Dispose;
    }

    internal CraftingTransferCoordinator(IPlayer player, IWorldAccessor world, CraftingAllocator allocator,
        Action<object> send, Action<CraftingDispatchResult> notify, Action<Action> enqueue,
        int thread, bool allowGenericFixture)
    {
        CraftingInventorySnapshot.EnsureThread(thread);
        this.player = player;
        this.world = world;
        this.allocator = allocator;
        this.send = send;
        this.notify = notify;
        this.enqueue = enqueue;
        this.thread = thread;
        fixture = allowGenericFixture;
        CraftingInventorySnapshot.Capture(player, 3, 3, thread, fixture);
        grid = (InventoryBase)player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName);
        grid.OnInventoryClosed += OnClosed;
        Observe(GlobalConstants.hotBarInvClassName, 0, 10);
        Observe(GlobalConstants.backpackInvClassName, 4, int.MaxValue);
        Observe(GlobalConstants.craftingInvClassName, 0, 9);
    }

    internal event Action? InventoryChanged;

    internal CraftingDispatchResult Execute(CraftingRecipeEntry selected, bool shift)
    {
        CraftingInventorySnapshot.EnsureThread(thread);
        CraftingDispatchResult Report(CraftingDispatchStatus status, string? reason, int packets = 0)
        {
            var result = new CraftingDispatchResult(status, reason, packets);
            if (reason != null) notify(result);
            return result;
        }
        if (executing) return Report(CraftingDispatchStatus.Rejected, Busy);
        if (disposed) return Report(CraftingDispatchStatus.Rejected, Stale);
        executing = true;
        int sent = 0;
        try
        {
            CraftingInventorySnapshot snapshot;
            try { snapshot = CraftingInventorySnapshot.Capture(player, 3, 3, thread, fixture); }
            catch (NotSupportedException) { return Report(CraftingDispatchStatus.Rejected, CraftingTransferPlanner.Unsafe); }
            catch (InvalidOperationException) { return Report(CraftingDispatchStatus.Rejected, Stale); }
            var initial = new CraftingTransferState(player, world, snapshot, fixture);
            var allocation = allocator.Evaluate(selected, snapshot);
            var projected = new CraftingTransferState(player, world, snapshot, fixture);
            var plan = new CraftingTransferPlanner(world).Plan(projected, allocation, shift);
            if (!plan.IsValid) return Report(CraftingDispatchStatus.Rejected, plan.Reason);
            if (!initial.IsCurrent() || disposed)
                return Report(CraftingDispatchStatus.Rejected, Stale);

            foreach (var move in plan.Moves)
            {
                if (disposed || !initial.IsCurrent()
                    || initial.Capacity(initial.Slots[move.Source], initial.Slots[move.Destination]) < move.Quantity)
                    return Report(CraftingDispatchStatus.Interrupted, Interrupted, sent);
                var source = initial.Slots[move.Source];
                var destination = initial.Slots[move.Destination];
                initial.Project(move);
                var operation = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0,
                    EnumMergePriority.AutoMerge, move.Quantity) { ActingPlayer = player };
                object? packet = player.InventoryManager.TryTransferTo(source.Live, destination.Live, ref operation);
                // Even a partial native move must have its returned packet sent before stopping.
                if (packet != null) { send(packet); sent++; }
                if (operation.MovedQuantity != move.Quantity || packet == null
                    || disposed || !initial.IsCurrent())
                    return Report(CraftingDispatchStatus.Interrupted, Interrupted, sent);
            }
            return Report(CraftingDispatchStatus.Dispatched, null, sent);
        }
        finally { executing = false; }
    }

    private void Observe(string name, int start, int end)
    {
        if (player.InventoryManager.GetOwnInventory(name) is not InventoryBase inventory) return;
        void Changed(int index)
        {
            if (index >= start && index < end && index < inventory.Count) QueueRefresh();
        }
        inventory.SlotModified += Changed;
        subscriptions.Add((inventory, Changed));
    }

    private void QueueRefresh()
    {
        if (disposed || refreshQueued) return;
        CraftingInventorySnapshot.EnsureThread(thread);
        refreshQueued = true;
        enqueue(() =>
        {
            if (disposed) return;
            CraftingInventorySnapshot.EnsureThread(thread);
            refreshQueued = false;
            InventoryChanged?.Invoke();
        });
    }

    private void OnClosed(IPlayer closedBy)
    {
        if (ReferenceEquals(closedBy, player)) Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        CraftingInventorySnapshot.EnsureThread(thread);
        disposed = true;
        grid.OnInventoryClosed -= OnClosed;
        foreach (var (inventory, handler) in subscriptions) inventory.SlotModified -= handler;
        subscriptions.Clear();
        if (api != null) api.Event.LeaveWorld -= Dispose;
        InventoryChanged = null;
    }
}
