using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>Builds the local baseline from patched definitions and registered entity identities, without network catalogue input.</summary>
internal static class EcosystemSpeciesCatalogue
{
    #region Public API
    /// <summary>Acquires and patches isolated local definitions, then builds stable species for the finalized client world.</summary>
    internal static IReadOnlyList<EcosystemSpecies> Load(ICoreAPI api)
    {
        IAssetManager assets = LocalAnimalAssets.CaptureAndPatch(api);
        return Build(assets.GetMany("entities/"), api.World.EntityTypes, LocalPatchApi.Create(api, assets));
    }

    /// <summary>Groups discovered wildlife variants while retaining each complete rule and its worldgen/runtime sampling provenance.</summary>
    internal static IReadOnlyList<EcosystemSpecies> Build(IEnumerable<IAsset> definitions, IEnumerable<EntityProperties> entities, ICoreAPI? sourceContext = null)
    {
        var registered = entities.ToArray();
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var rules = new Dictionary<string, List<HabitatRule>>(StringComparer.Ordinal);
        var names = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (IAsset asset in definitions)
        {
            // Parse each source once; the resolver clones before mutating it for an individual identity.
            JObject definition;
            try { definition = VanillaEntitySourceResolver.Read(asset, sourceContext); }
            catch (Newtonsoft.Json.JsonException error)
            {
                sourceContext?.Logger.Error(error);
                continue;
            }
            foreach (EntityProperties entity in registered)
            {
                if (!AnimalCatalogueDiscovery.MatchesSource(asset, entity, definition)) continue;
                var resolved = VanillaHabitatDefinitionResolver.ResolveDefinition(definition, entity);
                if (!AnimalCatalogueDiscovery.IsWildlife(asset, resolved)) continue;
                string species = AnimalCatalogueDiscovery.Identify(asset, entity, resolved);
                string? name = resolved["attributes"]?["ecosystemMap"]?["nameKey"]?.Value<string>();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    if (!names.TryGetValue(species, out var keys)) names[species] = keys = new(StringComparer.Ordinal);
                    keys.Add(AssetLocation.Create(name, asset.Location.Domain).ToString());
                }
                if (!sources.TryGetValue(species, out var codes)) sources[species] = codes = new(StringComparer.Ordinal);
                codes.Add(entity.Code.ToString());
                var conditions = VanillaHabitatDefinitionResolver.ReadConditions(resolved, entity.Code.Domain);
                if (conditions == null) continue;
                if (!rules.TryGetValue(species, out var alternatives)) rules[species] = alternatives = new();
                if (conditions.Worldgen != null)
                    alternatives.Add(HabitatRule.From(entity.Code.ToString(), HabitatSpawnPath.Worldgen, conditions.Worldgen));
                if (conditions.Runtime != null)
                    alternatives.Add(HabitatRule.From(entity.Code.ToString(), HabitatSpawnPath.Runtime, conditions.Runtime));
            }
        }
        return Array.AsReadOnly(rules.Where(pair => pair.Value.Count > 0).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new EcosystemSpecies(pair.Key, sources[pair.Key], pair.Value, names.TryGetValue(pair.Key, out var keys) ? keys.First() : null)).ToArray());
    }
    #endregion
}
