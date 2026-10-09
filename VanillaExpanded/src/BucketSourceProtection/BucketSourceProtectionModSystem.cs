using HarmonyLib;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Common;

namespace VanillaExpanded.BucketSourceProtection;

/// <summary>Owns the optional bucket spill patch on both client and server.</summary>
public sealed class BucketSourceProtectionModSystem : ModSystem, ILiveConfigurable
{
    private const string PatchId = "vanillaexpanded.bucketsourceprotection";
    private Harmony? harmony;

    #region Public API
    /// <summary>Loads configuration and installs the enabled spill guard.</summary>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        VanillaExpandedModSystem.EnsureConfigLoaded(api);
        OnConfigReloaded(api);
    }

    /// <summary>Installs or removes only this system's patch when its option changes.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch)
        {
            if (harmony != null) return;
            harmony = new Harmony(PatchId);
            new PatchClassProcessor(harmony, typeof(BucketSpillPatch)).Patch();
        }
        else
        {
            harmony?.UnpatchAll(PatchId);
            harmony = null;
        }
    }

    /// <summary>Removes the spill guard when the world closes.</summary>
    public override void Dispose()
    {
        harmony?.UnpatchAll(PatchId);
        harmony = null;
        base.Dispose();
    }
    #endregion
}
