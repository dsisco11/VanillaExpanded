using System;

using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace VanillaExpanded.src.ModSystems;

/// <summary>
/// Defines the death penalty modes that control what items players lose on death.
/// </summary>
public enum EnumDeathPenalty
{
    /// <summary>Use the base game's death penalty setting.</summary>
    Vanilla,
    /// <summary>Lose only bag/backpack items (keep hotbar and equipped).</summary>
    Bag,
    /// <summary>Lose bag and hotbar items (keep equipped armor/weapons).</summary>
    Unequipped,
    /// <summary>Lose all items including equipped armor/weapons.</summary>
    All
}

/// <summary>
/// Handles custom death penalty options that control what items players lose on death
/// and applies optional tool durability degradation.
/// </summary>
internal class DeathPenaltySystem : ModSystem
{
    #region Constants
    public const string ConfigKey_DeathPenalty = "veDeathPenalty";
    public const string ConfigKey_ToolDuraLoss = "veToolDuraLoss";
    #endregion

    #region Fields
    protected ICoreServerAPI? sapi;
    #endregion

    #region Properties
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;
    #endregion

    #region Hooks
    public override void Dispose()
    {
        base.Dispose();
        if (sapi != null)
        {
            sapi.Event.PlayerDeath -= OnPlayerDeath;
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.PlayerDeath += OnPlayerDeath;
    }
    #endregion

    #region Event Handlers
    private void OnPlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
    {
        ITreeAttribute worldConfig = sapi!.WorldManager.SaveGame.WorldConfiguration;
        string penaltyStr = worldConfig.GetString(ConfigKey_DeathPenalty, "vanilla");
        EnumDeathPenalty penaltyType = Enum.TryParse(penaltyStr, ignoreCase: true, out EnumDeathPenalty parsed) 
            ? parsed 
            : EnumDeathPenalty.Vanilla;
        
        string toolDuraLossStr = worldConfig.GetString(ConfigKey_ToolDuraLoss, "0");
        float toolDuraLoss = float.TryParse(toolDuraLossStr, out float toolDuraParsed) ? toolDuraParsed / 100f : 0f;

        // Apply tool durability loss before dropping items
        if (toolDuraLoss > 0)
        {
            ApplyToolDurabilityLoss(byPlayer, toolDuraLoss);
        }

        // Handle custom death penalties (vanilla defers to base game behavior)
        if (penaltyType != EnumDeathPenalty.Vanilla)
        {
            ApplyDeathPenalty(byPlayer, penaltyType);
        }
    }
    #endregion

    #region Death Penalty Logic
    private void ApplyDeathPenalty(IServerPlayer player, EnumDeathPenalty penaltyType)
    {
        var invMgr = player.InventoryManager;

        switch (penaltyType)
        {
            case EnumDeathPenalty.Bag:
                // Drop only backpack/bag items (keep hotbar and equipped)
                DropInventoryByClassName(invMgr, GlobalConstants.backpackInvClassName);
                break;

            case EnumDeathPenalty.Unequipped:
                // Drop backpack + hotbar (keep equipped armor/weapons)
                DropInventoryByClassName(invMgr, GlobalConstants.backpackInvClassName);
                DropInventoryByClassName(invMgr, GlobalConstants.hotBarInvClassName);
                break;

            case EnumDeathPenalty.All:
                // Drop everything including equipped items
                invMgr.OnDeath();
                break;
        }
    }

    private void DropInventoryByClassName(IPlayerInventoryManager invMgr, string invClassName)
    {
        var inventory = invMgr.GetOwnInventory(invClassName);
        if (inventory != null)
        {
            invMgr.DropAllInventoryItems(inventory);
        }
    }
    #endregion

    #region Tool Durability Logic
    private void ApplyToolDurabilityLoss(IServerPlayer player, float percentage)
    {
        foreach (var inv in player.InventoryManager.InventoriesOrdered)
        {
            foreach (var slot in inv)
            {
                if (slot.Empty) continue;
                if (slot.Itemstack.Collectible.Tool == null) continue;

                int maxDura = slot.Itemstack.Collectible.GetMaxDurability(slot.Itemstack);
                int remaining = slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack);
                int loss = (int)(maxDura * percentage);
                int newDura = remaining - loss;

                slot.Itemstack.Collectible.SetDurability(slot.Itemstack, newDura);
                slot.MarkDirty();
            }
        }
    }
    #endregion
}
