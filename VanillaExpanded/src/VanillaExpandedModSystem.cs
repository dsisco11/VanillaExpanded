using System;

using HarmonyLib;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.HandbookSearch;
using VanillaExpanded.IgnitionTools;
using VanillaExpanded.SpawnDecal;
using VanillaExpanded.src.AutoStashing;
using VanillaExpanded.src.IgnitionTools;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded;

public class VanillaExpandedModSystem : ModSystem
{
    #region Fields
    internal Harmony? harmony;

    /// <summary>
    /// The mod configuration. Loaded on startup.
    /// </summary>
    public static VanillaExpandedConfig Config { get; private set; } = new();
    private static bool configLoaded = false;
    #endregion

    /// <summary>
    /// Ensures the config is loaded. Can be called from other ModSystems' ShouldLoad().
    /// </summary>
    public static void EnsureConfigLoaded(ICoreAPI api)
    {
        if (configLoaded) return;
        
        try
        {
            var loadedConfig = api.LoadModConfig<VanillaExpandedConfig>(Constants.ConfigFileName);
            if (loadedConfig is null)
            {
                Config = new VanillaExpandedConfig();
                api.StoreModConfig(Config, Constants.ConfigFileName);
                api.Logger.Notification("[VanillaExpanded] Created default configuration file: {0}", Constants.ConfigFileName);
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
        
        configLoaded = true;
    }

    public override void Dispose()
    {
        base.Dispose();
        harmony?.UnpatchAll(Mod.Info.ModID);
        configLoaded = false; // Reset so config reloads on next game start
    }

    public override double ExecuteOrder()
    {
        return 1;// execute after all the blocks JSON defs are loaded, but before they are finalized, so we can inject our own stuff into the JSON defs.
    }

    public override void StartPre(ICoreAPI api)
    {
        EnsureConfigLoaded(api);
        
        // Suggest ConfigLib if not installed
        if (!api.ModLoader.IsModEnabled("configlib"))
        {
            api.Logger.Notification("[VanillaExpanded] ConfigLib is not installed. Install it for an in-game configuration GUI. Settings can be edited manually in ModConfig/{0}", Constants.ConfigFileName);
        }
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockBehaviorClass(BlockBehaviorAutoStashable.RegistryId, typeof(BlockBehaviorAutoStashable));
        api.RegisterBlockBehaviorClass(BehaviorCrateEntityEventBridge.RegistryId, typeof(BehaviorCrateEntityEventBridge));

        var channel = api.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Network.Packet_RequestAutoStash>()
            .RegisterMessageType<Network.Packet_TemporalSpawn>();

        if (!Harmony.HasAnyPatches(Mod.Info.ModID))
        {
            harmony = new Harmony(Mod.Info.ModID);
            ApplySelectivePatches();
        }
    }

    /// <summary>
    /// Applies Harmony patches selectively based on enabled features in config.
    /// </summary>
    private void ApplySelectivePatches()
    {
        if (harmony is null) return;

        if (Config.EnableAlloyCalculator)
        {
            new PatchClassProcessor(harmony, typeof(FirepitGuiPatch)).Patch();
        }

        if (Config.EnableSpawnDecal)
        {
            new PatchClassProcessor(harmony, typeof(ServerPlayerPatches)).Patch();
        }

        if (Config.EnableAutoStash)
        {
            new PatchClassProcessor(harmony, typeof(AutoStashPatch)).Patch();
        }

        if (Config.EnableIgnitionTools)
        {
            new PatchClassProcessor(harmony, typeof(ItemCandlePatch)).Patch();
            new PatchClassProcessor(harmony, typeof(IgnitionSourcesPatch)).Patch();
        }

        if (Config.EnableHandbookSearchPrioritization)
        {
            new PatchClassProcessor(harmony, typeof(HandbookSearchPatch)).Patch();
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (Config.EnableAutoStash)
        {
            AutoStashPatch.AmendContainerBehaviors(api);
        }
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
            if (recipe.Name.Domain != Constants.ModId)
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
