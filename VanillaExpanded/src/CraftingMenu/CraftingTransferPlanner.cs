using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.CraftingMenu;

internal sealed record CraftingTransfer(CraftingSlotId Source, CraftingSlotId Destination, int Quantity);

internal sealed record CraftingTransferPlan(ImmutableArray<CraftingTransfer> Moves, string? Reason)
{
    internal bool IsValid => Reason == null;
    internal static CraftingTransferPlan Refused(string reason) => new(ImmutableArray<CraftingTransfer>.Empty, reason);
}

// Detached slots retain the verified native acceptance rules, but never run modification callbacks.
internal sealed class CraftingTransferState
{
    internal sealed record Slot(CraftingSlotId Id, ItemSlot Live, ItemSlot Shadow, bool Grid);
    internal readonly Dictionary<CraftingSlotId, Slot> Slots = new();
    internal readonly List<Slot> Grid = new();
    internal readonly List<Slot> Storage = new();
    private readonly IPlayer player;
    private readonly IWorldAccessor world;
    private readonly bool fixture;
    private readonly int thread;
    private readonly Dictionary<string, InventoryBase> inventories = new();

    internal CraftingTransferState(IPlayer player, IWorldAccessor world, CraftingInventorySnapshot snapshot, bool fixture)
    {
        this.player = player;
        this.world = world;
        this.fixture = fixture;
        thread = snapshot.OwnerThreadId;
        Add(GlobalConstants.hotBarInvClassName, 0, 10, false);
        var backpack = player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        Add(GlobalConstants.backpackInvClassName, 4, backpack?.Count ?? 4, false);
        Add(GlobalConstants.craftingInvClassName, 0, snapshot.Width * snapshot.Height, true);
    }

    private void Add(string name, int start, int end, bool grid)
    {
        if (player.InventoryManager.GetOwnInventory(name) is not InventoryBase inventory) return;
        inventories.Add(name, inventory);
        for (int i = start; i < end; i++)
        {
            var live = inventory[i];
            ItemSlot shadow = live switch
            {
                ItemSlotBagContent bag => new ItemSlotBagContent(inventory, bag.BagIndex, bag.SlotIndex, bag.StorageType),
                ItemSlotSurvival => new ItemSlotSurvival(inventory),
                _ => new ItemSlot(inventory)
            };
            shadow.MaxSlotStackSize = live.MaxSlotStackSize;
            if (live is not ItemSlotBagContent) shadow.StorageType = live.StorageType;
            shadow.CanStoreTags = live.CanStoreTags;
            shadow.Itemstack = Copy(live.Itemstack);
            var slot = new Slot(new(inventory.InventoryID, i), live, shadow, grid);
            Slots.Add(slot.Id, slot);
            (grid ? Grid : Storage).Add(slot);
        }
    }

    internal bool IsCurrent()
    {
        CraftingInventorySnapshot.EnsureThread(thread);
        // Capture again to validate native topology, physical aliases, access and source eligibility.
        try { CraftingInventorySnapshot.Capture(player, 3, 3, thread, fixture); }
        catch (NotSupportedException) { return false; }
        catch (InvalidOperationException) { return false; }
        foreach (var (name, inventory) in inventories)
            if (!ReferenceEquals(player.InventoryManager.GetOwnInventory(name), inventory)) return false;
        return Slots.Values.All(slot => slot.Id.Index < slot.Live.Inventory.Count
            && ReferenceEquals(slot.Live.Inventory[slot.Id.Index], slot.Live)
            && slot.Live.MaxSlotStackSize == slot.Shadow.MaxSlotStackSize
            && slot.Live.StorageType == slot.Shadow.StorageType
            && slot.Live.CanStoreTags.Equals(slot.Shadow.CanStoreTags)
            && Same(world, slot.Live.Itemstack, slot.Shadow.Itemstack));
    }

    internal bool Accessible(Slot slot) => slot.Live.Inventory.HasOpened(player)
        && slot.Live.Inventory.CanPlayerModify(player, player.Entity.Pos);

    internal bool SafeStack(ItemStack? stack)
    {
        if (stack == null) return true;
        if (stack.StackSize <= 0
            || stack.TempAttributes.Count > 0
            || stack.Collectible.GetCollectibleInterface<IHeldBag>() != null
            || stack.Collectible.GetTransitionableProperties(world, stack, player.Entity) is { Length: > 0 }) return false;
        return stack.Collectible.Attributes?.IsTrue("allowHotCrafting") == true
            || stack.Collectible.GetTemperature(world, CraftingStackSnapshot.Copy(stack)) <= GlobalConstants.TooHotToTouchTemperature;
    }

    internal int Capacity(Slot source, Slot destination)
    {
        var from = source.Shadow;
        var to = destination.Shadow;
        if (source == destination || !Accessible(source) || !Accessible(destination)
            || !from.CanTake() || !to.CanHold(from) || !to.CanTakeFrom(from)
            || !SafeStack(from.Itemstack) || !SafeStack(to.Itemstack)) return 0;
        if (to.Itemstack != null && !SameKind(world, from.Itemstack!, to.Itemstack)) return 0;
        int capacity = Math.Min(to.GetRemainingSlotSpace(from.Itemstack!),
            from.Itemstack!.Collectible.MaxStackSize - to.StackSize);
        if (to.Itemstack != null)
            capacity = Math.Min(capacity, to.Itemstack.Collectible.GetMergableQuantity(
                to.Itemstack, from.Itemstack, EnumMergePriority.AutoMerge));
        return Math.Max(0, Math.Min(capacity, from.StackSize));
    }

    internal void Project(CraftingTransfer move)
    {
        var from = Slots[move.Source].Shadow;
        var to = Slots[move.Destination].Shadow;
        var stack = Copy(from.Itemstack)!;
        stack.StackSize = move.Quantity;
        if (to.Itemstack == null) to.Itemstack = stack;
        else to.Itemstack.StackSize += move.Quantity;
        from.Itemstack!.StackSize -= move.Quantity;
        if (from.StackSize == 0) from.Itemstack = null;
    }

    internal static ItemStack? Copy(ItemStack? stack) => stack == null ? null : CraftingStackSnapshot.Copy(stack);
    internal static bool SameKind(IWorldAccessor world, ItemStack left, ItemStack right) =>
        left.Class == right.Class && left.Id == right.Id && ReferenceEquals(left.Collectible, right.Collectible)
        && left.Attributes.Equals(world, right.Attributes) && left.TempAttributes.Equals(world, right.TempAttributes);
    internal static bool Same(IWorldAccessor world, ItemStack? left, ItemStack? right) =>
        left == null ? right == null : right != null && left.StackSize == right.StackSize && SameKind(world, left, right);
}

internal sealed class CraftingTransferPlanner
{
    internal const string Invalid = "vanillaexpanded:craftingmenu-placement-invalid";
    internal const string Space = "vanillaexpanded:craftingmenu-placement-space";
    internal const string Empty = "vanillaexpanded:craftingmenu-placement-empty";
    internal const string Unsafe = "vanillaexpanded:craftingmenu-placement-unsafe";
    private readonly IWorldAccessor world;

    internal CraftingTransferPlanner(IWorldAccessor world) => this.world = world;

    internal CraftingTransferPlan Plan(CraftingTransferState state, CraftingAllocation allocation, bool shift)
    {
        if (!allocation.Evaluation.IsSupported)
            return CraftingTransferPlan.Refused(allocation.Evaluation.UnsupportedReason!);
        var placements = shift ? allocation.Shift : allocation.Ordinary;
        if (placements.IsDefaultOrEmpty) return CraftingTransferPlan.Refused(Empty);
        var targets = new Dictionary<int, ItemStack>();
        var used = new Dictionary<CraftingSlotId, long>();
        foreach (var placement in placements)
        {
            var stack = placement.GetStack();
            if (placement.GridPosition < 0 || placement.GridPosition >= state.Grid.Count
                || placement.Quantity <= 0 || !targets.TryAdd(placement.GridPosition, stack)
                || placement.Sources.IsDefaultOrEmpty
                || placement.Sources.Sum(source => (long)source.Quantity) != placement.Quantity)
                return CraftingTransferPlan.Refused(Invalid);
            if (!state.SafeStack(stack)) return CraftingTransferPlan.Refused(Unsafe);
            var requirement = allocation.Requirements.FirstOrDefault(need => need.GridPosition == placement.GridPosition);
            if (requirement == null || !requirement.GetIngredient().SatisfiesAsIngredient(stack, false)
                || (!shift || !requirement.IsConsumed) && placement.Quantity > requirement.Quantity)
                return CraftingTransferPlan.Refused(Invalid);
            foreach (var source in placement.Sources)
            {
                if (source.Quantity <= 0 || !state.Slots.TryGetValue(source.Slot, out var slot)
                    || slot.Shadow.Itemstack == null || !slot.Shadow.CanTake() || !state.Accessible(slot)
                    || !CraftingTransferState.SameKind(world, stack, slot.Shadow.Itemstack))
                    return CraftingTransferPlan.Refused(Invalid);
                used[source.Slot] = used.GetValueOrDefault(source.Slot) + source.Quantity;
                if (used[source.Slot] > slot.Shadow.StackSize) return CraftingTransferPlan.Refused(Invalid);
            }
            if (stack.StackSize > Math.Min(state.Grid[placement.GridPosition].Shadow.MaxSlotStackSize,
                stack.Collectible.MaxStackSize)) return CraftingTransferPlan.Refused(Invalid);
        }
        foreach (var slot in state.Grid)
            if (!state.Accessible(slot) || !state.SafeStack(slot.Shadow.Itemstack))
                return CraftingTransferPlan.Refused(Unsafe);

        var moves = ImmutableArray.CreateBuilder<CraftingTransfer>();
        int Retained(CraftingTransferState.Slot slot) => slot.Grid
            && targets.TryGetValue(slot.Id.Index, out var target) && slot.Shadow.Itemstack != null
            && CraftingTransferState.SameKind(world, target, slot.Shadow.Itemstack)
                ? Math.Min(target.StackSize, slot.Shadow.StackSize) : 0;
        int Excess(CraftingTransferState.Slot slot) => slot.Shadow.StackSize - Retained(slot);
        bool Complete() => state.Grid.All(slot => CraftingTransferState.Same(world, slot.Shadow.Itemstack,
            targets.GetValueOrDefault(slot.Id.Index)));
        void Move(CraftingTransferState.Slot from, CraftingTransferState.Slot to, int quantity)
        {
            var move = new CraftingTransfer(from.Id, to.Id, quantity);
            moves.Add(move);
            state.Project(move);
        }

        var kinds = state.Slots.Values.Where(slot => !slot.Shadow.Empty)
            .Select(slot => CraftingStackSnapshot.Copy(slot.Shadow.Itemstack!)).ToArray();
        var seen = new HashSet<string>();
        while (!Complete())
        {
            string signature = string.Join(";", state.Slots.Values.Select(slot => slot.Shadow.Empty ? "-"
                : Array.FindIndex(kinds, stack => CraftingTransferState.SameKind(world, stack, slot.Shadow.Itemstack!))
                    + ":" + slot.Shadow.StackSize));
            if (!seen.Add(signature)) return CraftingTransferPlan.Refused(Space);
            bool progress = false;
            foreach (var destination in state.Grid)
            {
                if (!targets.TryGetValue(destination.Id.Index, out var target)
                    || destination.Shadow.Itemstack != null
                        && !CraftingTransferState.SameKind(world, target, destination.Shadow.Itemstack)) continue;
                int missing = target.StackSize - destination.Shadow.StackSize;
                if (missing <= 0) continue;
                foreach (var source in state.Grid.Concat(state.Storage))
                {
                    if (source.Shadow.Itemstack == null
                        || !CraftingTransferState.SameKind(world, target, source.Shadow.Itemstack)) continue;
                    int amount = Math.Min(missing, Math.Min(Excess(source), state.Capacity(source, destination)));
                    if (amount <= 0) continue;
                    Move(source, destination, amount);
                    missing -= amount;
                    progress = true;
                    if (missing == 0) break;
                }
            }
            if (Complete()) break;
            if (progress) continue;

            // Return the most constrained displaced stack first; fill existing compatible stacks before empty slots.
            foreach (var source in state.Grid.Where(slot => Excess(slot) > 0)
                .OrderBy(slot => targets.ContainsKey(slot.Id.Index) ? 0 : 1)
                .ThenBy(slot => state.Storage.Count(target => state.Capacity(slot, target) > 0)))
            {
                foreach (var destination in state.Storage.OrderBy(slot => slot.Shadow.Empty))
                {
                    int amount = Math.Min(Excess(source), state.Capacity(source, destination));
                    if (amount <= 0) continue;
                    Move(source, destination, amount);
                    progress = true;
                    break;
                }
                if (progress) break;
            }
            if (progress) continue;

            // A vacant input can be a native-transfer buffer, including when all carried slots are full.
            foreach (var source in state.Storage.Concat(state.Grid))
            {
                if (source.Shadow.Empty || Excess(source) != source.Shadow.StackSize
                    || !targets.Any(target => CraftingTransferState.SameKind(world, target.Value, source.Shadow.Itemstack!)
                        && !CraftingTransferState.Same(world, target.Value, state.Grid[target.Key].Shadow.Itemstack))) continue;
                var buffer = state.Grid.FirstOrDefault(slot => slot.Shadow.Empty
                    && state.Capacity(source, slot) >= source.Shadow.StackSize);
                if (buffer == null) continue;
                Move(source, buffer, source.Shadow.StackSize);
                progress = true;
                break;
            }
            if (!progress) return CraftingTransferPlan.Refused(Space);
        }
        return new(moves.ToImmutable(), null);
    }
}
