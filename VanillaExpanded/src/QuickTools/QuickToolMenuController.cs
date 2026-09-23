using System;
using System.Collections.Generic;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Coordinates held-menu interaction, candidate snapshots, and the existing equipment owner.</summary>
internal sealed class QuickToolMenuController : IDisposable
{
    private readonly QuickToolClientOperations equipment;
    private readonly QuickToolCandidateCache cache;
    private readonly IRadialMenu menu;
    private readonly IClientEventAPI events;
    private readonly System.Func<bool> ready;
    private readonly System.Func<(IPlayerInventoryManager? Manager, ItemSlot? Offhand)> currentPlayer;
    private readonly System.Func<QuickToolBinding> currentBinding;
    private readonly System.Func<int, bool> down;
    private readonly System.Func<bool> focused;
    private readonly System.Func<string, string> text;
    private readonly Action<string> feedback;
    private readonly System.Func<object?> playerIdentity;
    private RadialMenuLayout? layout;
    private readonly Dictionary<string, QuickToolCandidate?> displayed = new(StringComparer.Ordinal);
    private IPlayerInventoryManager? manager;
    private ItemSlot? offhand;
    private object? boundPlayerIdentity;
    private QuickToolBinding openingBinding;
    private bool restoreAvailable;
    private bool disposed;

    /// <summary>Accepts domain owners and narrow host seams without requiring a game player proxy.</summary>
    internal QuickToolMenuController(QuickToolClientOperations equipment, QuickToolCandidateCache cache,
        IRadialMenu menu, IClientEventAPI events, System.Func<bool> ready,
        System.Func<(IPlayerInventoryManager? Manager, ItemSlot? Offhand)> currentPlayer,
        System.Func<QuickToolBinding> currentBinding, System.Func<int, bool> down, System.Func<bool> focused,
        System.Func<string, string> text, Action<string> feedback, System.Func<object?>? playerIdentity = null)
    {
        this.equipment = equipment;
        this.cache = cache;
        this.menu = menu;
        this.events = events;
        this.ready = ready;
        this.currentPlayer = currentPlayer;
        this.currentBinding = currentBinding;
        this.down = down;
        this.focused = focused;
        this.text = text;
        this.feedback = feedback;
        this.playerIdentity = playerIdentity ?? (() => currentPlayer().Manager);
        cache.Refreshed += OnCandidatesRefreshed;
    }

    #region Input lifetime
    /// <summary>Gets the hold/reopen state without reading inventory.</summary>
    internal QuickToolMenuState State { get; private set; }

    /// <summary>Handles a real hotkey press, consuming repeats until its activation keys are released.</summary>
    internal bool Press()
    {
        PollInput();
        if (disposed || !ready() || !focused()) return false;
        if (equipment.IsOperating) return true;
        QuickToolBinding binding = currentBinding();
        if (!binding.Supported)
        {
            if (binding.Primary > 0) feedback(text("quicktool-binding-unavailable"));
            return false;
        }
        if (State != QuickToolMenuState.ClosedReady) return true;
        if (!RefreshContext()) return false;
        if (State != QuickToolMenuState.ClosedReady) return true;

        openingBinding = binding;
        restoreAvailable = equipment.ValidateRestoration();
        IReadOnlyList<RadialMenuEntry> entries = SnapshotEntries();
        State = QuickToolMenuState.OpenHeld;
        if (!menu.Open(layout!, entries, OnSelected, OnCancelled))
        {
            State = QuickToolMenuState.ClosedAwaitRelease;
            displayed.Clear();
            feedback(text("quicktool-menu-unavailable"));
        }
        return true;
    }

    /// <summary>Polls only physical inputs and readiness; a key-up override handles event-before-raw-state ordering.</summary>
    internal void PollInput(int? releasedKey = null)
    {
        if (disposed) return;
        if (!ready()) { ClearContext(); return; }
        if (manager is not null && !ReferenceEquals(boundPlayerIdentity, playerIdentity())) ClearContext();
        QuickToolBinding binding = currentBinding();
        bool IsDown(int key) => key != releasedKey && down(key);
        if (State == QuickToolMenuState.Disabled) State = QuickToolMenuState.ClosedAwaitRelease;
        if (!focused())
        {
            CancelMenu();
            State = QuickToolMenuState.ClosedAwaitRelease;
            return;
        }
        if (State == QuickToolMenuState.OpenHeld && (binding != openingBinding || !openingBinding.IsHeld(IsDown)))
            CancelMenu();
        // Rebinding and modifier release cannot turn a still-held activation key into a new press.
        if (State == QuickToolMenuState.ClosedAwaitRelease
            && !openingBinding.AnyActivationDown(IsDown) && !binding.AnyActivationDown(IsDown))
            State = QuickToolMenuState.ClosedReady;
    }

    /// <summary>Closes only this interaction while preserving the equipment session across cancellation/focus loss.</summary>
    internal void CancelMenu()
    {
        if (State != QuickToolMenuState.OpenHeld) return;
        State = QuickToolMenuState.ClosedAwaitRelease;
        menu.Cancel();
        displayed.Clear();
    }

    /// <summary>Retains the release latch when the generic dialog cancels independently.</summary>
    private void OnCancelled()
    {
        if (State == QuickToolMenuState.OpenHeld) State = QuickToolMenuState.ClosedAwaitRelease;
        displayed.Clear();
    }
    #endregion

    #region Context and content
    /// <summary>Reconciles player context and refreshes coalesced cache work on the low-frequency host tick or open.</summary>
    internal bool RefreshContext()
    {
        if (disposed) return false;
        if (!ready()) { ClearContext(); return false; }
        (IPlayerInventoryManager? nextManager, ItemSlot? nextOffhand) = currentPlayer();
        object? nextIdentity = playerIdentity();
        if (nextManager is null || nextOffhand is null) { ClearContext(); return false; }
        if (!ReferenceEquals(manager, nextManager) || !ReferenceEquals(offhand, nextOffhand)
            || !ReferenceEquals(boundPlayerIdentity, nextIdentity))
        {
            CancelMenu();
            equipment.Clear();
            cache.Clear();
            manager = nextManager;
            offhand = nextOffhand;
            boundPlayerIdentity = nextIdentity;
            cache.ObserveActiveSlot(events);
        }
        cache.Bind(manager, offhand);
        return true;
    }

    /// <summary>Builds one wedge per available item and remembers the candidates visible under those wedges.</summary>
    private IReadOnlyList<RadialMenuEntry> SnapshotEntries()
    {
        var entries = new List<RadialMenuEntry>(QuickToolLayout.WedgeIds.Count + 1);
        var availableIds = new List<string>(QuickToolLayout.WedgeIds.Count);
        displayed.Clear();
        foreach (string id in QuickToolLayout.WedgeIds)
        {
            QuickToolCandidate? candidate = cache.GetCached(id);
            if (candidate is null) continue;
            availableIds.Add(id);
            displayed.Add(id, candidate);
            entries.Add(new RadialMenuEntry(id, candidate.Stack.GetName(), true,
                new QuickToolItemIcon(candidate.Stack)));
        }
        layout = QuickToolLayout.CreateLayout(availableIds);
        entries.Add(new RadialMenuEntry(QuickToolLayout.RestoreId, text("quicktool-unequip"), restoreAvailable,
            description: text("quicktool-restore-description")));
        return entries;
    }

    /// <summary>Resizes the open ring when candidate availability changes, retaining its center snapshot.</summary>
    private void OnCandidatesRefreshed()
    {
        if (State != QuickToolMenuState.OpenHeld) return;
        RadialMenuLayout previous = layout!;
        IReadOnlyList<RadialMenuEntry> entries = SnapshotEntries();
        bool sameIds = previous.WedgeIds.Count == layout!.WedgeIds.Count;
        for (int i = 0; sameIds && i < previous.WedgeIds.Count; i++)
            sameIds = previous.WedgeIds[i] == layout.WedgeIds[i];
        if (sameIds) menu.UpdateEntries(entries);
        else menu.UpdateLayout(layout, entries);
    }

    /// <summary>Executes the selected local action once and reports its return value, including early failures.</summary>
    private void OnSelected(string id)
    {
        if (State != QuickToolMenuState.OpenHeld) return;
        displayed.TryGetValue(id, out QuickToolCandidate? candidate);
        IPlayerInventoryManager? openedManager = manager;
        ItemSlot? openedOffhand = offhand;
        object? openedPlayerIdentity = boundPlayerIdentity;
        State = QuickToolMenuState.ClosedAwaitRelease;
        menu.Cancel();
        displayed.Clear();
        if (!focused() || currentBinding() != openingBinding || !openingBinding.IsHeld(down)) return;
        if (!RefreshContext() || !ReferenceEquals(manager, openedManager) || !ReferenceEquals(offhand, openedOffhand)
            || !ReferenceEquals(boundPlayerIdentity, openedPlayerIdentity))
        {
            feedback(text("quicktool-session-unavailable"));
            return;
        }

        QuickToolEquipmentResult result = id == QuickToolLayout.RestoreId && restoreAvailable
            ? equipment.Restore()
            : candidate is not null ? equipment.Select(id, candidate) : QuickToolEquipmentResult.Rejected;
        string? message = result switch
        {
            QuickToolEquipmentResult.Rejected => "quicktool-rejected",
            QuickToolEquipmentResult.Interrupted => "quicktool-interrupted",
            QuickToolEquipmentResult.SessionInvalidated => "quicktool-session-unavailable",
            _ => null
        };
        if (message is not null) feedback(text(message));
        // The native sequence has finished; update candidates once from its final local arrangement.
        cache.Invalidate();
        cache.RefreshPending();
    }
    #endregion

    #region Lifecycle
    /// <summary>Discards feature/player state without treating ordinary menu closure as equipment cancellation.</summary>
    internal void ClearContext()
    {
        CancelMenu();
        State = QuickToolMenuState.Disabled;
        cache.Clear();
        equipment.Clear();
        manager = null;
        offhand = null;
        boundPlayerIdentity = null;
        displayed.Clear();
    }

    /// <summary>Detaches coordinator callbacks and clears its context; the host owns domain-owner disposal.</summary>
    public void Dispose()
    {
        if (disposed) return;
        ClearContext();
        cache.Refreshed -= OnCandidatesRefreshed;
        disposed = true;
    }
    #endregion
}

/// <summary>Defines the hold-to-open and release-before-reopen interaction states.</summary>
internal enum QuickToolMenuState
{
    ClosedReady,
    OpenHeld,
    ClosedAwaitRelease,
    Disabled
}
