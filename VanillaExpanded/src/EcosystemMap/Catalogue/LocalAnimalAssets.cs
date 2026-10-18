using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.ServerMods.NoObf;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>Copies local entity and patch assets in vanilla origin order and applies vanilla patches exclusively to those copies.</summary>
internal static class LocalAnimalAssets
{
    #region Public API
    /// <summary>Builds an isolated local baseline, including gameplay-capable replacements and conditional entity patches.</summary>
    internal static IAssetManager CaptureAndPatch(ICoreAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var view = new LocalEntityAssetStore();
        foreach (IAssetOrigin origin in api.Assets.Origins)
        {
            // Vanilla overwrites locations in origin enumeration order and excludes resource packs from gameplay assets.
            if (origin.IsAllowedToAffectGameplay())
                foreach (IAsset asset in origin.GetAssets(AssetCategory.entities))
                    view.Copy(asset);
            foreach (IAsset asset in origin.GetAssets(AssetCategory.patches))
                view.Copy(asset);
        }

        var patcher = new ModJsonPatchLoader();
        SetContext(patcher, "api", typeof(ICoreAPI), LocalPatchApi.Create(api, view));
        SetContext(patcher, "worldConfig", typeof(ITreeAttribute), api.World.Config);
        patcher.ApplyPatches("entities/");
        return view;
    }
    #endregion

    #region Private
    /// <summary>Checks private context signatures before invoking the existing patch pipeline.</summary>
    private static void SetContext(ModJsonPatchLoader patcher, string name, Type type, object value)
    {
        FieldInfo? field = typeof(ModJsonPatchLoader).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || field.FieldType != type)
            throw new NotSupportedException("The installed game's JSON patch context is incompatible: " + name);
        field.SetValue(patcher, value);
    }
    #endregion
}
