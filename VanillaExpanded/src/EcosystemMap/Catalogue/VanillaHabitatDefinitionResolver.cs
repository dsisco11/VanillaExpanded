using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;
using Vintagestory.ServerMods.NoObf;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>Owns the version-sensitive boundary to vanilla's pure byType/placeholder resolver.</summary>
internal static class VanillaHabitatDefinitionResolver
{
    private static readonly Action<JToken, string, OrderedDictionary<string, string>> ResolveByType = BindResolver();

    #region Public API
    /// <summary>Resolves a private token with vanilla's rules and deserializes its climate override using vanilla defaults.</summary>
    internal static SpawnConditions? Resolve(JObject source, EntityProperties entity)
    {
        var token = ResolveDefinition(source, entity);
        return ReadConditions(token, entity.Code.Domain);
    }

    /// <summary>Resolves a private complete definition so classification also observes tagsByType and other variant metadata.</summary>
    internal static JObject ResolveDefinition(JObject source, EntityProperties entity)
    {
        var token = (JObject)source.DeepClone();
        ResolveByType(token, entity.Code.Path, new OrderedDictionary<string, string>(entity.Variant));
        return token;
    }

    /// <summary>Reads already resolved spawning data using vanilla defaults and climate override handling.</summary>
    internal static SpawnConditions? ReadConditions(JObject token, string domain)
    {
        if (token.GetValue("enabled", StringComparison.OrdinalIgnoreCase)?.Value<bool?>() == false) return null;
        return (token.GetValue("server", StringComparison.OrdinalIgnoreCase) as JObject)?.GetValue("spawnconditions", StringComparison.OrdinalIgnoreCase)?.ToObject<SpawnConditions>(
            JsonUtil.CreateSerializerForDomain(domain));
    }
    #endregion

    #region Private
    /// <summary>Rejects incompatible game signatures instead of silently substituting a different resolver.</summary>
    private static Action<JToken, string, OrderedDictionary<string, string>> BindResolver()
    {
        MethodInfo? method = typeof(RegistryObjectType).GetMethod("solveByType", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(JToken), typeof(string), typeof(OrderedDictionary<string, string>) }, null);
        if (method == null || method.ReturnType != typeof(void))
            throw new NotSupportedException("The installed game's entity variant resolver is incompatible with the ecosystem catalogue.");
        return method.CreateDelegate<Action<JToken, string, OrderedDictionary<string, string>>>();
    }
    #endregion
}
