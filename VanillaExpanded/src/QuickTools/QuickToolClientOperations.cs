using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.QuickTools;

/// <summary>Owns client context, native packet sending, and restoration lifetime.</summary>
internal sealed class QuickToolClientOperations : IDisposable
{
    private readonly IClientEventAPI events;
    private readonly Func<bool> ready;
    private readonly Func<(IPlayerInventoryManager? Manager, ItemSlot? Offhand)> currentPlayer;
    private readonly Action<object> send;
    private readonly bool allowGenericFixture;
    private readonly ITagRegistry<TagSet>? tagRegistry;
    private readonly long tickId;
    private IPlayerInventoryManager? manager;
    private ItemSlot? offhand;
    private QuickToolEquipment? equipment;
    private bool operationActive;
    private bool disposed;

    /// <summary>Binds the current client context and local player lifecycle.</summary>
    internal QuickToolClientOperations(ICoreClientAPI api)
        : this(api.Event,
            () => VanillaExpandedModSystem.Config.EnableQuickTools && api.PlayerReadyFired
                && api.World.Player?.Entity?.Alive == true,
            () => (api.World.Player?.InventoryManager, api.World.Player?.Entity?.LeftHandItemSlot),
            api.Network.SendPacketClient, tagRegistry: api.CollectibleTagRegistry)
    {
    }

    /// <summary>Accepts narrow client context and event seams for installed-slot verification.</summary>
    internal QuickToolClientOperations(IClientEventAPI events, Func<bool> ready,
        Func<(IPlayerInventoryManager? Manager, ItemSlot? Offhand)> currentPlayer,
        Action<object> send, bool allowGenericFixture = false, ITagRegistry<TagSet>? tagRegistry = null)
    {
        this.events = events;
        this.ready = ready;
        this.currentPlayer = currentPlayer;
        this.send = send;
        this.allowGenericFixture = allowGenericFixture;
        this.tagRegistry = tagRegistry;
        events.AfterActiveSlotChanged += OnActiveSlotChanged;
        events.LeaveWorld += OnLeaveWorld;
        tickId = events.RegisterGameTickListener(OnTick, 500);
    }

    #region Selection API
    /// <summary>Gets session presence without scanning inventory; menu availability requires explicit validation.</summary>
    internal bool HasSession => equipment?.HasSession == true;
    /// <summary>Gets whether a local native sequence is executing inside the current call.</summary>
    internal bool IsOperating => operationActive;
    /// <summary>Reports the local native result without claiming server acceptance.</summary>
    internal event Action<QuickToolEquipmentResult>? LocalResult;

    /// <summary>Resolves tracked stacks and the return route once when preparing the menu.</summary>
    internal bool ValidateRestoration()
    {
        if (IsOperating) return false;
        if (!Ready() || !BindCurrent()) { Clear(); return false; }
        return equipment!.ValidateRestoration();
    }

    /// <summary>Reruns the selected provider and sends its native inventory flip packets.</summary>
    internal QuickToolEquipmentResult Select(string entryId, QuickToolCandidate displayed)
    {
        if (IsOperating) return QuickToolEquipmentResult.Rejected;
        if (!Ready() || !BindCurrent()) { Clear(); return QuickToolEquipmentResult.Rejected; }
        QuickToolEquipmentResult result;
        operationActive = true;
        try { result = equipment!.Select(entryId, displayed); }
        finally { operationActive = false; }
        LocalResult?.Invoke(result);
        return result;
    }

    /// <summary>Attempts native restoration through the current client session.</summary>
    internal QuickToolEquipmentResult Restore()
    {
        if (IsOperating) return QuickToolEquipmentResult.Rejected;
        if (!Ready() || !BindCurrent()) { Clear(); return QuickToolEquipmentResult.Rejected; }
        QuickToolEquipmentResult result;
        operationActive = true;
        try { result = equipment!.Restore(); }
        finally { operationActive = false; }
        LocalResult?.Invoke(result);
        return result;
    }

    /// <summary>Requires a live local player and the enabled feature before movement.</summary>
    private bool Ready() => !disposed && ready();

    #endregion

    #region Player context
    /// <summary>Rebinds player context without subscribing to inventory changes or retaining slot topology.</summary>
    private bool BindCurrent()
    {
        (IPlayerInventoryManager? nextManager, ItemSlot? nextOffhand) = currentPlayer();
        if (nextManager is null || nextOffhand is null) return false;
        if (!ReferenceEquals(nextManager, manager) || !ReferenceEquals(nextOffhand, offhand))
        {
            equipment?.Clear();
            manager = nextManager;
            offhand = nextOffhand;
            equipment = new QuickToolEquipment(manager, offhand, send, allowGenericFixture, tagRegistry);
        }
        return equipment is not null;
    }

    /// <summary>Leaves restoration history intact; it becomes available again when the player returns to its recorded slot.</summary>
    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs args)
    {
        // Native inventory flips and ordinary player slot changes both raise this event. Action-time validation
        // ensures restoration is only offered when the recorded hand slot is active again.
    }

    /// <summary>Clears unavailable or replaced player context without scanning inventory or reconciling items.</summary>
    private void OnTick(float elapsed)
    {
        if (!Ready()) { Clear(); return; }
        if (!BindCurrent()) { Clear(); return; }
    }
    #endregion

    #region Lifecycle
    /// <summary>Discards session and player references on world exit or feature disablement.</summary>
    internal void Clear()
    {
        equipment?.Clear();
        manager = null;
        offhand = null;
        equipment = null;
    }

    /// <summary>Clears world-local restoration state.</summary>
    private void OnLeaveWorld() => Clear();

    /// <summary>Releases client lifecycle subscriptions.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Clear();
        events.AfterActiveSlotChanged -= OnActiveSlotChanged;
        events.LeaveWorld -= OnLeaveWorld;
        events.UnregisterGameTickListener(tickId);
        LocalResult = null;
    }
    #endregion
}
