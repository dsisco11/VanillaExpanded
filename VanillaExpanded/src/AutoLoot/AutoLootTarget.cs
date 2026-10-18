using System;
using System.Diagnostics.CodeAnalysis;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoLoot;

internal sealed class AutoLootTarget
{
    // EntityBehaviorHarvestable.OnInteract uses a five-block client eye-to-hit reach.
    private const double MaximumReachSquared = 25;
    private readonly EntityBehaviorHarvestable behavior;
    private readonly EntityPlayer looter;
    private readonly Vec3d hitPosition;
    private readonly int dimension;

    public Entity Corpse => behavior.entity;
    public long EntityId { get; }
    public InventoryBase Inventory { get; }
    public GuiDialog Dialog { get; }
    public bool HasLoot => Inventory.FirstNonEmptySlot is not null;

    private AutoLootTarget(EntityBehaviorHarvestable behavior, GuiDialog dialog, EntityPlayer looter, Vec3d hitPosition)
    {
        this.behavior = behavior;
        this.looter = looter;
        this.hitPosition = hitPosition.Clone();
        dimension = looter.Pos.Dimension;
        EntityId = behavior.entity.EntityId;
        Inventory = behavior.Inventory;
        Dialog = dialog;
    }

    public static bool TryCreate(ICoreClientAPI api, IClientPlayer player,
        EntityBehaviorHarvestable behavior, GuiDialog? dialog, Vec3d hitPosition,
        [NotNullWhen(true)] out AutoLootTarget? target)
    {
        target = null;
        if (behavior.GetType() != typeof(EntityBehaviorHarvestable)
            || behavior.entity is EntityPlayer || dialog is null || behavior.Inventory is null)
        {
            return false;
        }

        var candidate = new AutoLootTarget(behavior, dialog, player.Entity, hitPosition);
        if (!candidate.IsValid(api, player) || !candidate.HasLoot)
        {
            return false;
        }

        target = candidate;
        return true;
    }

    public bool IsValid(ICoreClientAPI api, IClientPlayer player)
    {
        if (api.Side != EnumAppSide.Client
            || !ReferenceEquals(player.Entity, looter)
            || Corpse.World.Side != EnumAppSide.Client
            || Corpse.Alive || !behavior.IsHarvested
            || Corpse.EntityId != EntityId
            || !ReferenceEquals(api.World.GetEntityById(EntityId), Corpse)
            || !ReferenceEquals(Corpse.GetBehavior<EntityBehaviorHarvestable>(), behavior)
            || !ReferenceEquals(behavior.Inventory, Inventory)
            || looter.Pos.Dimension != dimension || Corpse.Pos.Dimension != dimension
            || !Dialog.IsOpened()
            || !Inventory.HasOpened(player)
            || player.InventoryManager.OpenedInventories?.Contains(Inventory) != true)
        {
            return false;
        }

        return Corpse.Pos.XYZ.Add(hitPosition).SquareDistanceTo(looter.Pos.XYZ.Add(looter.LocalEyePos))
            <= MaximumReachSquared;
    }
}
