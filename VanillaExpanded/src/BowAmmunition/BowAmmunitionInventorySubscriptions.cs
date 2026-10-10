using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Owns session-bound inventory notifications and reconciles membership during bounded samples.</summary>
internal sealed class BowAmmunitionInventorySubscriptions : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly IPlayer player;
    private readonly Action invalidate;
    private readonly HashSet<InventoryBase> inventories = new(ReferenceEqualityComparer.Instance);
    private bool disposed;
    #region Public API
    /// <summary>Attaches the local active-hand notification without scanning inventories.</summary>
    public BowAmmunitionInventorySubscriptions(ICoreClientAPI api, IPlayer player, Action invalidate)
    {
        this.api = api; this.player = player; this.invalidate = invalidate;
        api.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
    }
    /// <summary>Observes all available inventory instances, including closed inventories whose opening matters.</summary>
    public void Reconcile()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var current = new HashSet<InventoryBase>(player.InventoryManager.InventoriesOrdered, ReferenceEqualityComparer.Instance);
        foreach (InventoryBase inventory in inventories.Where(item => !current.Contains(item)).ToArray())
        {
            Detach(inventory); inventories.Remove(inventory);
        }
        foreach (InventoryBase inventory in current)
        {
            if (!inventories.Add(inventory)) continue;
            inventory.SlotModified += OnSlotModified;
            inventory.OnInventoryOpened += OnMembershipChanged;
            inventory.OnInventoryClosed += OnMembershipChanged;
        }
    }
    /// <summary>Detaches borrowed inventory and API events exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        foreach (InventoryBase inventory in inventories) Detach(inventory);
        inventories.Clear();
    }
    #endregion
    #region Private
    /// <summary>Removes only this consumer's handlers from a borrowed inventory.</summary>
    private void Detach(InventoryBase inventory)
    {
        inventory.SlotModified -= OnSlotModified;
        inventory.OnInventoryOpened -= OnMembershipChanged;
        inventory.OnInventoryClosed -= OnMembershipChanged;
    }
    /// <summary>Coalesces slot notifications through the shared scheduler without sampling in the event.</summary>
    private void OnSlotModified(int slotId) => invalidate();
    /// <summary>Coalesces local active-hand changes without accessing inventory contents.</summary>
    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs change) => invalidate();
    /// <summary>Ignores another player's open/close notification on shared external inventories.</summary>
    private void OnMembershipChanged(IPlayer changedPlayer)
    {
        if (changedPlayer.PlayerUID == player.PlayerUID) invalidate();
    }
    #endregion
}
