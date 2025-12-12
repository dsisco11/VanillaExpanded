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
        if (sapi is not null)
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
                // Destroy only backpack/bag items (keep hotbar and equipped)
                ClearInventoryByClassName(invMgr, GlobalConstants.backpackInvClassName);
                break;

            case EnumDeathPenalty.Unequipped:
                // Destroy backpack + hotbar (keep equipped armor/weapons)
                ClearInventoryByClassName(invMgr, GlobalConstants.backpackInvClassName);
                ClearInventoryByClassName(invMgr, GlobalConstants.hotBarInvClassName);
                break;

            case EnumDeathPenalty.All:
                // Destroy everything including equipped items
                ClearInventoryByClassName(invMgr, GlobalConstants.backpackInvClassName);
                ClearInventoryByClassName(invMgr, GlobalConstants.hotBarInvClassName);
                ClearInventoryByClassName(invMgr, GlobalConstants.characterInvClassName);
                break;
        }
    }

    private void ClearInventoryByClassName(IPlayerInventoryManager invMgr, string invClassName)
    {
        var inventory = invMgr.GetOwnInventory(invClassName);
        if (inventory is not null)
        {
            foreach (var slot in inventory)
            {
                if (!slot.Empty)
                {
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            }
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
                int loss = (int)(maxDura * percentage);
                if (loss > 0)
                {
                    slot.Itemstack.Collectible.DamageItem(sapi!.World, player.Entity, slot, loss);
                }
            }
        }
    }
    #endregion
}
