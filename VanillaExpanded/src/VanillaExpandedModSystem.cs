using System;

using HarmonyLib;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.AutoStashing;
using VanillaExpanded.HandbookSearch;
using VanillaExpanded.IgnitionTools;
using VanillaExpanded.SpawnDecal;
using VanillaExpanded.src.AutoStashing;
using VanillaExpanded.src.IgnitionTools;

using Vintagestory.API.Common;

namespace VanillaExpanded;

public class VanillaExpandedModSystem : ModSystem
{
    #region Fields
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
        new Harmony(Constants.ModId).UnpatchAll(Constants.ModId);
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

    internal static void ApplyLiveConfig(ICoreAPI api)
    {
        EnsureConfigLoaded(api);

        ReapplyHarmonyPatches();

        if (Config.EnableAutoStash)
        {
            AutoStashPatch.AmendContainerBehaviors(api);
        }

        UpdateRecipesBasedOnConfig(api, logChanges: true);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockBehaviorClass(BlockBehaviorAutoStashable.RegistryId, typeof(BlockBehaviorAutoStashable));
        api.RegisterBlockBehaviorClass(BehaviorCrateEntityEventBridge.RegistryId, typeof(BehaviorCrateEntityEventBridge));

        var channel = api.Network.RegisterChannel(Mod.Info.ModID)
            .RegisterMessageType<Network.Packet_RequestAutoStash>()
            .RegisterMessageType<Network.Packet_TemporalSpawn>();

        EnsureHarmonyPatched();
    }

    /// <summary>
    /// Applies Harmony patches selectively based on enabled features in config.
    /// </summary>
    private static void ApplySelectivePatches(Harmony harmony)
    {
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
        UpdateRecipesBasedOnConfig(api, logChanges: false);
    }

    private static void EnsureHarmonyPatched()
    {
        if (Harmony.HasAnyPatches(Constants.ModId))
        {
            return;
        }

        var harmony = new Harmony(Constants.ModId);
        ApplySelectivePatches(harmony);
    }

    private static void ReapplyHarmonyPatches()
    {
        new Harmony(Constants.ModId).UnpatchAll(Constants.ModId);
        var harmony = new Harmony(Constants.ModId);
        ApplySelectivePatches(harmony);
    }

    private static void UpdateRecipesBasedOnConfig(ICoreAPI api, bool logChanges)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }

        var recipes = api.World.GridRecipes;
        int disabledCount = 0;
        int enabledCount = 0;

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

            bool enable = !shouldDisable;
            if (recipe.Enabled != enable)
            {
                recipe.Enabled = enable;
                if (enable) enabledCount++;
                else disabledCount++;
            }
        }

        if (!logChanges) return;

        if (disabledCount > 0)
        {
            api.Logger.Notification("[VanillaExpanded] Disabled {0} recipes based on configuration.", disabledCount);
        }

        if (enabledCount > 0)
        {
            api.Logger.Notification("[VanillaExpanded] Enabled {0} recipes based on configuration.", enabledCount);
        }
    }
}
