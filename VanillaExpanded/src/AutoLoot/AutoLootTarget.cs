using System;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoLoot;

internal sealed class AutoLootTarget
{
    private static readonly AccessTools.FieldRef<EntityBehaviorHarvestable, GuiDialogCreatureContents> LootDialog =
        AccessTools.FieldRefAccess<EntityBehaviorHarvestable, GuiDialogCreatureContents>("dlg");
    // EntityBehaviorHarvestable.OnInteract uses a five-block client eye-to-hit reach.
    private const double MaximumReachSquared = 25;
    private readonly EntityBehaviorHarvestable behavior;
    private readonly EntityPlayer looter;
    private readonly Vec3d hitPosition;
    private readonly int dimension;

    public Entity Corpse => behavior.entity;
    public long EntityId { get; }
    public InventoryBase Inventory { get; }
    public bool HasLoot => Inventory.FirstNonEmptySlot is not null;

    public void CloseDialog() => LootDialog(behavior)?.TryClose();

    private AutoLootTarget(EntityBehaviorHarvestable behavior, EntityPlayer looter, Vec3d hitPosition)
    {
        this.behavior = behavior;
        this.looter = looter;
        this.hitPosition = hitPosition.Clone();
        dimension = looter.Pos.Dimension;
        EntityId = behavior.entity.EntityId;
        Inventory = behavior.Inventory;
    }

    public static bool TryCreate(ICoreClientAPI api, IClientPlayer player,
        EntityBehaviorHarvestable behavior, Vec3d hitPosition,
        [NotNullWhen(true)] out AutoLootTarget? target)
    {
        target = null;
        if (!IsSupportedCorpse(behavior))
        {
            return false;
        }

        var candidate = new AutoLootTarget(behavior, player.Entity, hitPosition);
        if (!candidate.IsValid(api, player) || !candidate.HasLoot)
        {
            return false;
        }

        target = candidate;
        return true;
    }

    public static bool IsSupportedCorpse(EntityBehaviorHarvestable behavior)
        => behavior.GetType() == typeof(EntityBehaviorHarvestable)
            && behavior.entity is not EntityPlayer && !behavior.entity.Alive
            && behavior.IsHarvested && behavior.Inventory is not null;

    public static bool IsInReach(EntityPlayer player, Entity corpse, Vec3d hitPosition)
        => corpse.Pos.XYZ.Add(hitPosition).SquareDistanceTo(player.Pos.XYZ.Add(player.LocalEyePos))
            <= MaximumReachSquared;

    public bool IsValid(ICoreClientAPI api, IClientPlayer player)
    {
        if (api.Side != EnumAppSide.Client
            || !ReferenceEquals(player.Entity, looter)
            || Corpse.World.Side != EnumAppSide.Client
            || !IsSupportedCorpse(behavior)
            || Corpse.EntityId != EntityId
            || !ReferenceEquals(api.World.GetEntityById(EntityId), Corpse)
            || !ReferenceEquals(Corpse.GetBehavior<EntityBehaviorHarvestable>(), behavior)
            || !ReferenceEquals(behavior.Inventory, Inventory)
            || looter.Pos.Dimension != dimension || Corpse.Pos.Dimension != dimension
            || !Inventory.HasOpened(player)
            || player.InventoryManager.OpenedInventories?.Contains(Inventory) != true)
        {
            return false;
        }

        return IsInReach(looter, Corpse, hitPosition);
    }
}
