using System.Linq;
using VanillaExpanded.AutoLoot;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RadialProgress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace VanillaExpanded;

internal sealed class AutoLootSystem_Client : ModSystem, ILiveConfigurable
{
    private readonly IProgressSystemProvider? progressSystem;
    private ICoreClientAPI? api;
    internal static AutoLootSystem_Client? Active { get; private set; }
    internal AutoLootClient? Client { get; private set; }

    public AutoLootSystem_Client() { }

    internal AutoLootSystem_Client(IProgressSystemProvider progressSystem)
    {
        this.progressSystem = progressSystem;
    }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorAutoLootable.RegistryId, typeof(EntityBehaviorAutoLootable));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        try
        {
            this.api = api;
            Client = new AutoLootClient(api, progressSystem);
            Active = this;
            api.Event.BlockTexturesLoaded += OnBlockTexturesLoaded;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnBlockTexturesLoaded()
    {
        if (api is null) return;

        foreach (var properties in api.World.EntityTypes)
        {
            if (properties.Client?.BehaviorsAsJsonObj is not { } behaviors
                || behaviors.Any(behavior => behavior["code"].AsString() == EntityBehaviorAutoLootable.RegistryId)
                || !behaviors.Any(behavior => behavior["code"].AsString() is string code
                    && typeof(EntityBehaviorHarvestable).IsAssignableFrom(api.ClassRegistry.GetEntityBehaviorClass(code))))
            {
                continue;
            }

            // Amend synchronized client definitions before the engine clones them for entity instances.
            properties.Client.BehaviorsAsJsonObj = [.. behaviors,
                JsonObject.FromJson("{\"code\":\"" + EntityBehaviorAutoLootable.RegistryId + "\"}")];
        }
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (!VanillaExpandedModSystem.Config.EnableAutoLoot)
        {
            Client?.Cancel();
        }
    }

    public override void Dispose()
    {
        if (Active == this)
        {
            Active = null;
        }
        if (api is not null)
        {
            api.Event.BlockTexturesLoaded -= OnBlockTexturesLoaded;
            api = null;
        }
        Client?.Dispose();
        Client = null;
        base.Dispose();
    }
}
