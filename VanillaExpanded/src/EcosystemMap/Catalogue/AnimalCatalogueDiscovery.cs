using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>Discovers wildlife identities from definitions and semantic variants without an enumerated species or source-file list.</summary>
internal static class AnimalCatalogueDiscovery
{
    #region Public API
    /// <summary>Rejects excluded animal categories and non-animal tags while retaining predators and foreign-domain wildlife.</summary>
    internal static bool IsWildlife(IAsset asset, JObject definition)
    {
        string path = "/" + asset.Location.Path;
        if (path.Contains("/fish/", StringComparison.Ordinal) || path.Contains("/invertebrate/", StringComparison.Ordinal)
            || path.Contains("/insect/", StringComparison.Ordinal)) return false;
        string[] tags = (definition.GetValue("tags", StringComparison.OrdinalIgnoreCase) as JArray)?.Values<string>().OfType<string>().ToArray()
            ?? Array.Empty<string>();
        // Hostile spawn groups include wolves and bears, so they cannot be used as a monster filter.
        if (tags.Any(tag => tag is "rust-creature" or "mechanical" or "humanoid" or "inanimate" or "bot"
            or "monster" or "fish" or "insect" or "invertebrate" or "tamed" or "semitamed" or "domestic")) return false;
        return tags.Contains("animal", StringComparer.Ordinal);
    }

    /// <summary>Associates registered variants with their original source and groups by semantic type, ignoring age, gender and cosmetics.</summary>
    internal static bool MatchesSource(IAsset asset, EntityProperties entity, JObject definition)
    {
        string code = definition.GetValue("code", StringComparison.OrdinalIgnoreCase)?.Value<string>() ?? "";
        if (code.Length == 0) return false;
        var baseCode = AssetLocation.Create(code, asset.Location.Domain);
        if (entity.Code.Domain != baseCode.Domain) return false;
        JArray? groups = definition.GetValue("variantgroups", StringComparison.OrdinalIgnoreCase) as JArray;
        if (groups is { Count: > 0 })
        {
            if (entity.Code.Path != baseCode.Path && !entity.Code.Path.StartsWith(baseCode.Path + "-", StringComparison.Ordinal)) return false;
            foreach (JObject group in groups)
            {
                string? key = group.GetValue("code", StringComparison.OrdinalIgnoreCase)?.Value<string>();
                if (key == null || !entity.Variant.TryGetValue(key, out string? value)) return false;
                if (group.GetValue("states", StringComparison.OrdinalIgnoreCase) is JArray states && !states.Values<string>().Contains(value)) return false;
            }
        }
        else if (entity.Code.Path != baseCode.Path) return false;

        return true;
    }

    /// <summary>Builds a stable identity from resolved semantic metadata, never from localized text or code suffix removal.</summary>
    internal static string Identify(IAsset asset, EntityProperties entity, JObject definition)
    {
        var baseCode = AssetLocation.Create(definition.GetValue("code", StringComparison.OrdinalIgnoreCase)!.Value<string>()!, asset.Location.Domain);
        string? authored = definition["attributes"]?["ecosystemMap"]?["speciesGroup"]?.Value<string>();
        if (!string.IsNullOrWhiteSpace(authored)) return AssetLocation.Create(authored, baseCode.Domain).ToString();
        if (entity.Variant.TryGetValue("type", out string? type))
            return baseCode.Domain + ":" + baseCode.Path + "-" + type;
        // Handbook groups unify separate young/sex source files, e.g. chicken-baby and chicken.
        string? groupCode = definition["attributes"]?["handbook"]?["groupcode"]?.Value<string>();
        if (groupCode != null && !groupCode.Contains('{'))
        {
            var group = AssetLocation.Create(groupCode, baseCode.Domain);
            string family = group.Path.StartsWith("creaturegroup-", StringComparison.Ordinal) ? group.Path["creaturegroup-".Length..] : group.Path;
            return group.Domain + ":" + family;
        }
        return baseCode.ToString();
    }
    #endregion
}
