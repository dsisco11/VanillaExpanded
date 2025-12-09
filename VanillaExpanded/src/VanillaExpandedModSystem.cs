using HarmonyLib;

using System;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.IgnitionTools;
using VanillaExpanded.InputHandlers;
using VanillaExpanded.Network;
using VanillaExpanded.src;
using VanillaExpanded.src.AutoStashing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace VanillaExpanded;

public class VanillaExpandedModSystem : ModSystem
{
    #region Fields
    internal Harmony? harmony;
    internal static AutoStashManager_Client ClientSide;
    internal static AutoStashManager_Server ServerSide;
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

    public override void StartClientSide(ICoreClientAPI api)
    {
        ClientSide = new AutoStashManager_Client(Mod.Info, api);
        api.Input.RegisterHotKey("ve.equipLightSourceToOffhand", Lang.Get($"{this.Mod.Info.ModID}:ve-hotkey-equiplightsource-offhand"), GlKeys.F, HotkeyType.InventoryHotkeys);
        api.Input.SetHotKeyHandler("ve.equipLightSourceToOffhand", (hotKey) =>
        {
            return EquipLightSource.OnHotKeyPressed(api, hotKey, true);
        });

        api.Input.RegisterHotKey("ve.equipLightSourceToHotbar", Lang.Get($"{this.Mod.Info.ModID}:ve-hotkey-equiplightsource-hotbar"), GlKeys.F, HotkeyType.InventoryHotkeys, shiftPressed: true);
        api.Input.SetHotKeyHandler("ve.equipLightSourceToHotbar", (hotKey) =>
        {
            return EquipLightSource.OnHotKeyPressed(api, hotKey, false);
        });
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        ServerSide = new AutoStashManager_Server(Mod.Info, api);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        { 
            AutoStashPatch.AmendContainerBehaviors(api); 
        }
    }
}
