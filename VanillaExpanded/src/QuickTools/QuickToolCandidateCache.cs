using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.QuickTools;

/// <summary>Observes eligible inventories and caches one current winner for every fixed entry.</summary>
public sealed class QuickToolCandidateCache : IDisposable
{
    private readonly IReadOnlyList<IQuickToolCandidateProvider> providers;
    private readonly Dictionary<string, QuickToolCandidate?> winners = new(StringComparer.Ordinal);
    private readonly Action? scheduleRefresh;
    private readonly Action<string>? diagnostic;
    private IPlayerInventoryManager? manager;
    private IClientEventAPI? activeSlotEvents;
    private ItemSlot? offhand;
    private IInventory? hotbar;
    private IInventory? backpack;
    private ItemSlot[] hotbarSlots = [];
    private ItemSlot[] backpackSlots = [];
    private bool dirty;
    private bool scheduled;
    private bool disposed;

    /// <summary>Creates the fixed providers; a scheduler can enqueue one deferred refresh per dirty burst.</summary>
    public QuickToolCandidateCache(Action? scheduleRefresh = null, Action<string>? diagnostic = null)
    {
        providers = QuickToolLayout.WedgeIds.Select(id => id == QuickToolLayout.LightId
            ? (IQuickToolCandidateProvider)new LightCandidateProvider()
            : new ToolCandidateProvider(Enum.Parse<EnumTool>(id.AsSpan(5)), diagnostic)).ToArray();
        this.scheduleRefresh = scheduleRefresh;
        this.diagnostic = diagnostic;
    }

    /// <summary>Signals that a complete candidate snapshot is ready for visible entry updates.</summary>
    public event Action? Refreshed;

    #region Lifecycle and observation
    /// <summary>Binds a current player and drops all references from any preceding player.</summary>
    public void Bind(IPlayerInventoryManager newManager, ItemSlot newOffhand)
    {
        ArgumentNullException.ThrowIfNull(newManager);
        ArgumentNullException.ThrowIfNull(newOffhand);
        if (disposed) throw new ObjectDisposedException(nameof(QuickToolCandidateCache));
        if (!ReferenceEquals(manager, newManager) || !ReferenceEquals(offhand, newOffhand))
        {
            Detach();
            manager = newManager;
            offhand = newOffhand;
        }
        ReconcileTopology();
        RefreshPending();
    }

    /// <summary>Checks inventory identity and slot topology without scanning item metadata.</summary>
    public void ReconcileTopology()
    {
        if (manager is null) return;
        IInventory? nextHotbar = manager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        IInventory? nextBackpack = manager.GetOwnInventory(GlobalConstants.backpackInvClassName);
        if (!ReferenceEquals(nextHotbar, hotbar))
        {
            if (hotbar is not null) hotbar.SlotModified -= OnHotbarModified;
            hotbar = nextHotbar;
            if (hotbar is not null) hotbar.SlotModified += OnHotbarModified;
            Invalidate();
        }
        if (!ReferenceEquals(nextBackpack, backpack))
        {
            if (backpack is not null) backpack.SlotModified -= OnBackpackModified;
            backpack = nextBackpack;
            if (backpack is not null) backpack.SlotModified += OnBackpackModified;
            Invalidate();
        }

        // A bag swap can replace content slots while keeping the outer backpack inventory object.
        if (!SameSlots(hotbar, hotbarSlots)) { hotbarSlots = SnapshotSlots(hotbar); Invalidate(); }
        if (!SameSlots(backpack, backpackSlots)) { backpackSlots = SnapshotSlots(backpack); Invalidate(); }
    }

    /// <summary>Subscribes to active-hand selection changes for the current player context.</summary>
    public void ObserveActiveSlot(IClientEventAPI events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (disposed) throw new ObjectDisposedException(nameof(QuickToolCandidateCache));
        if (ReferenceEquals(activeSlotEvents, events)) return;
        if (activeSlotEvents is not null)
        {
            activeSlotEvents.AfterActiveSlotChanged -= OnActiveSlotChanged;
            activeSlotEvents.LeaveWorld -= OnLeaveWorld;
        }
        activeSlotEvents = events;
        activeSlotEvents.AfterActiveSlotChanged += OnActiveSlotChanged;
        activeSlotEvents.LeaveWorld += OnLeaveWorld;
    }

    /// <summary>Handles the client active-slot event without scanning inventory items.</summary>
    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs args) => Invalidate();

    /// <summary>Discards all player-specific references when leaving the world.</summary>
    private void OnLeaveWorld() => Clear();

    /// <summary>Marks all ranking and hand-priority inputs dirty after an active-slot change.</summary>
    public void OnActiveHotbarChanged() => Invalidate();

    /// <summary>Marks the cache dirty and requests at most one deferred refresh for a notification burst.</summary>
    public void Invalidate()
    {
        if (manager is null || disposed) return;
        dirty = true;
        if (scheduled) return;
        scheduled = true;
        scheduleRefresh?.Invoke();
    }

    /// <summary>Detaches inventories and discards cached references on disable or world exit.</summary>
    public void Clear()
    {
        Detach();
        if (activeSlotEvents is not null)
        {
            activeSlotEvents.AfterActiveSlotChanged -= OnActiveSlotChanged;
            activeSlotEvents.LeaveWorld -= OnLeaveWorld;
        }
        activeSlotEvents = null;
        manager = null;
        offhand = null;
        winners.Clear();
        dirty = false;
        scheduled = false;
    }

    /// <summary>Releases subscriptions and prevents reuse.</summary>
    public void Dispose()
    {
        if (disposed) return;
        Clear();
        disposed = true;
    }

    /// <summary>Handles any hotbar change, including the physical offhand.</summary>
    private void OnHotbarModified(int slotIndex) => Invalidate();

    /// <summary>Handles content or bag changes and defers topology reconciliation until callback completion.</summary>
    private void OnBackpackModified(int slotIndex) => Invalidate();

    /// <summary>Removes event handlers before dropping stale inventory references.</summary>
    private void Detach()
    {
        if (hotbar is not null) hotbar.SlotModified -= OnHotbarModified;
        if (backpack is not null) backpack.SlotModified -= OnBackpackModified;
        hotbar = null;
        backpack = null;
        hotbarSlots = [];
        backpackSlots = [];
    }
    #endregion

    #region Candidate access
    /// <summary>Refreshes the pending batch, including topology changes, before a menu opens.</summary>
    public void RefreshPending()
    {
        if (manager is null || offhand is null) return;
        ReconcileTopology();
        if (!dirty) { scheduled = false; return; }
        // Publish one complete snapshot after all provider resolutions, never halfway through a callback.
        ReportUnsupportedCategories();
        var next = new Dictionary<string, QuickToolCandidate?>(StringComparer.Ordinal);
        ItemSlot? activeHand = manager.ActiveHotbarSlot;
        foreach (IQuickToolCandidateProvider provider in providers) next[provider.EntryId] = provider.Resolve(manager, offhand, activeHand);
        winners.Clear();
        foreach (var pair in next) winners.Add(pair.Key, pair.Value);
        dirty = false;
        scheduled = false;
        RefreshCount++;
        Refreshed?.Invoke();
    }

    /// <summary>Gets the last refreshed winner for a fixed entry.</summary>
    public QuickToolCandidate? GetCached(string entryId) => winners.TryGetValue(entryId, out QuickToolCandidate? candidate) ? candidate : null;

    /// <summary>Rechecks the displayed candidate and current owned hand before equipment movement.</summary>
    public bool Revalidate(string entryId, QuickToolCandidate displayed, ItemSlot expectedHand, out QuickToolCandidate? current)
    {
        current = null;
        if (manager is null || offhand is null || expectedHand is null) return false;
        ReconcileTopology();
        if (hotbar is null || expectedHand.GetType() != typeof(ItemSlotSurvival)
            || !ReferenceEquals(manager.ActiveHotbarSlot, expectedHand)) return false;
        bool ownsHand = false;
        for (int i = 0; i < hotbar.Count; i++) if (ReferenceEquals(hotbar[i], expectedHand)) { ownsHand = true; break; }
        if (!ownsHand) return false;
        IQuickToolCandidateProvider? provider = providers.FirstOrDefault(p => p.EntryId == entryId);
        if (provider is null) return false;
        current = provider.Resolve(manager, offhand, expectedHand);
        if (!displayed.Matches(current)) { Invalidate(); return false; }
        return true;
    }

    /// <summary>Creates content only for available candidates, plus the center restoration action.</summary>
    public IReadOnlyList<RadialMenuEntry> CreateEntries(bool canRestore)
    {
        RefreshPending();
        var entries = new List<RadialMenuEntry>(QuickToolLayout.WedgeIds.Count + 1);
        foreach (string id in QuickToolLayout.WedgeIds)
        {
            QuickToolCandidate? candidate = GetCached(id);
            if (candidate is not null)
                entries.Add(new RadialMenuEntry(id, candidate.Stack.GetName(), true, new QuickToolItemIcon(candidate.Stack)));
        }
        entries.Add(new RadialMenuEntry(QuickToolLayout.RestoreId, "Unequip", canRestore));
        return entries;
    }

    /// <summary>Counts full metadata refreshes for deterministic batching verification.</summary>
    public int RefreshCount { get; private set; }

    /// <summary>Checks whether a visible snapshot has pending invalidation.</summary>
    public bool IsDirty => dirty;
    #endregion

    /// <summary>Reports unsupported enum values once per refresh without inventing a wedge for them.</summary>
    private void ReportUnsupportedCategories()
    {
        if (diagnostic is null) return;
        var reported = new HashSet<EnumTool>();
        foreach (IInventory? inventory in new[] { hotbar, backpack })
        {
            if (inventory is null) continue;
            bool isBackpack = ReferenceEquals(inventory, backpack);
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot? slot = inventory[i];
                if (slot is null || slot.Empty || (isBackpack ? slot is not ItemSlotBagContent : slot.GetType() != typeof(ItemSlotSurvival))) continue;
                try
                {
                    EnumTool? category = slot.Itemstack?.Collectible?.GetTool(slot);
                    if (category is EnumTool value && QuickToolLayout.GetToolId(value) is null && reported.Add(value))
                        diagnostic($"Unsupported tool category {value} ({(int)value}).");
                }
                catch (Exception exception)
                {
                    diagnostic($"Unable to classify slot {i}: {exception.Message}");
                }
            }
        }
    }
    #region Topology helpers
    /// <summary>Captures slot object identity separately from inventory object identity.</summary>
    private static ItemSlot[] SnapshotSlots(IInventory? inventory)
    {
        if (inventory is null) return [];
        var result = new ItemSlot[inventory.Count];
        for (int i = 0; i < result.Length; i++) result[i] = inventory[i] ?? throw new InvalidOperationException("An owned inventory returned a null slot.");
        return result;
    }

    /// <summary>Checks for replaced slot objects without reading collectible metadata.</summary>
    private static bool SameSlots(IInventory? inventory, ItemSlot[] previous)
    {
        if (inventory is null) return previous.Length == 0;
        if (inventory.Count != previous.Length) return false;
        for (int i = 0; i < previous.Length; i++) if (!ReferenceEquals(inventory[i], previous[i])) return false;
        return true;
    }
    #endregion
}
