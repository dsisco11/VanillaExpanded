using HarmonyLib;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.IgnitionTools;
using VanillaExpanded.src.AutoStashing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded;

public class VanillaExpandedModSystem : ModSystem
{
    #region Fields
    internal Harmony? harmony;
    #endregion

    public override void Dispose()
    {
        base.Dispose();
        harmony?.UnpatchAll(Mod.Info.ModID);
    }

    public override double ExecuteOrder()
    {
        return 1;// execute after all the blocks JSON defs are loaded, but before they are finalized, so we can inject our own stuff into the JSON defs.
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass(BehaviorIgnitionTool.RegistryId, typeof(BehaviorIgnitionTool));
        api.RegisterBlockBehaviorClass(BlockBehaviorAutoStashable.RegistryId, typeof(BlockBehaviorAutoStashable));
        api.RegisterBlockBehaviorClass(BehaviorCrateEntityEventBridge.RegistryId, typeof(BehaviorCrateEntityEventBridge));

        var channel = api.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Network.Packet_RequestAutoStash>();

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        AutoStashPatch.AmendContainerBehaviors(api);
    }
}
