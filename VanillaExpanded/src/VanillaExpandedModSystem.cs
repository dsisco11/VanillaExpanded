using System;
using System.Linq;

using HarmonyLib;

using VanillaExpanded.AutoStashing;
using VanillaExpanded.IgnitionTools;
using VanillaExpanded.src.AutoStashing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded;

public class VanillaExpandedModSystem : ModSystem
{
    #region Constants
    public const string ConfigFileName = "VanillaExpanded.json";
    #endregion

    #region Fields
    internal Harmony? harmony;

    /// <summary>
    /// The mod configuration. Loaded on startup.
    /// </summary>
    public static VanillaExpandedConfig Config { get; private set; } = new();
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
        LoadConfig(api);

        api.RegisterCollectibleBehaviorClass(BehaviorIgnitionTool.RegistryId, typeof(BehaviorIgnitionTool));
        api.RegisterBlockBehaviorClass(BlockBehaviorAutoStashable.RegistryId, typeof(BlockBehaviorAutoStashable));
        api.RegisterBlockBehaviorClass(BehaviorCrateEntityEventBridge.RegistryId, typeof(BehaviorCrateEntityEventBridge));

        var channel = api.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Network.Packet_RequestAutoStash>()
            .RegisterMessageType<Network.Packet_TemporalSpawn>();

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
        }
    }

    private void LoadConfig(ICoreAPI api)
    {
        try
        {
            var loadedConfig = api.LoadModConfig<VanillaExpandedConfig>(ConfigFileName);
            if (loadedConfig is null)
            {
                // Create default config and save it immediately
                Config = new VanillaExpandedConfig();
                api.StoreModConfig(Config, ConfigFileName);
                api.Logger.Notification("[VanillaExpanded] Created default configuration file: {0}", ConfigFileName);
            }
            else
            {
                Config = loadedConfig;
            }
        }
        catch (Exception ex)
        {
            api.Logger.Error("[VanillaExpanded] Failed to load configuration: {0}", ex.Message);
            Config = new VanillaExpandedConfig();
        }

        // Suggest ConfigLib if not installed
        if (!api.ModLoader.IsModEnabled("configlib"))
        {
            api.Logger.Notification("[VanillaExpanded] ConfigLib is not installed. Install it for an in-game configuration GUI. Settings can be edited manually in ModConfig/{0}", ConfigFileName);
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        AutoStashPatch.AmendContainerBehaviors(api);
        DisableRecipesBasedOnConfig(api);
    }

    private void DisableRecipesBasedOnConfig(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        var recipes = api.World.GridRecipes;
        int disabledCount = 0;

        foreach (var recipe in recipes)
        {
            if (recipe.Name is null)
            {
                continue;
            }

            string recipePath = recipe.Name.Path;

            // Check if recipe belongs to VanillaExpanded and should be disabled
            if (recipe.Name.Domain != "vanillaexpanded")
            {
                continue;
            }

            bool shouldDisable = recipePath switch
            {
                var p when p.StartsWith("backpack_decraft") => !Config.EnableBackpackDecraft,
                var p when p.StartsWith("linensack_decraft") => !Config.EnableLinenSackDecraft,
                var p when p.StartsWith("metalbits") => !Config.EnableMetalBitsRecycling,
                var p when p.StartsWith("sticks") => !Config.EnableStickRecipes,
                var p when p.StartsWith("wattle_decraft") => !Config.EnableWattleDecraft,
                _ => false
            };

            if (shouldDisable)
            {
                recipe.Enabled = false;
                disabledCount++;
            }
        }

        if (disabledCount > 0)
        {
            api.Logger.Notification("[VanillaExpanded] Disabled {0} recipes based on configuration.", disabledCount);
        }
    }
}
