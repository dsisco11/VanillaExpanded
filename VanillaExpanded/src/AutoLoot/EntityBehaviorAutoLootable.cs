using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.AutoLoot;

internal sealed class EntityBehaviorAutoLootable : EntityBehavior
{
    public static string RegistryId => "VEAutoLootable";

    public EntityBehaviorAutoLootable(Entity entity) : base(entity) { }

    public override string PropertyName() => RegistryId;

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition,
        EnumInteractMode mode, ref EnumHandling handled)
    {
        if (entity.World.Side == EnumAppSide.Client
            && entity.GetBehavior<EntityBehaviorHarvestable>() is { } harvestable)
        {
            AutoLootSystem_Client.Active?.Client?.OnInteract(harvestable, byEntity, hitPosition, mode);
        }
    }

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es,
        IClientPlayer player, ref EnumHandling handled)
    {
        if (AutoLootSystem_Client.Active?.Client is null
            || !VanillaExpandedModSystem.Config.EnableAutoLoot || world.Side != EnumAppSide.Client
            || entity.GetBehavior<EntityBehaviorHarvestable>() is not { } harvestable
            || !AutoLootTarget.IsSupportedCorpse(harvestable)
            || harvestable.Inventory.FirstNonEmptySlot is null
            || !ReferenceEquals(es.Entity, entity)
            || !ReferenceEquals(world.GetEntityById(entity.EntityId), entity)
            || player.Entity.Pos.Dimension != entity.Pos.Dimension
            || es.HitPosition is not Vec3d hitPosition
            || !AutoLootTarget.IsInReach(player.Entity, entity, hitPosition))
        {
            return null;
        }

        return [new WorldInteraction
        {
            ActionLangCode = "vanillaexpanded:entityhelp-autoloot",
            MouseButton = EnumMouseButton.Right
        }];
    }
}
