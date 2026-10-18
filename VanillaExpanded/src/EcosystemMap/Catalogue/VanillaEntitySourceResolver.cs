using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.ServerMods.NoObf;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>Reuses vanilla's inherited definition merge on isolated local assets before species and variant resolution.</summary>
internal static class VanillaEntitySourceResolver
{
    /// <summary>Matches the installed instance method's by-reference token contract.</summary>
    private delegate void InheritDefinition(RegistryObjectType instance, ICoreAPI api, ref JObject token, string domain, string loggingPath);
    private static readonly InheritDefinition LoadInherits = BindInheritance();

    #region Public API
    /// <summary>Parses a private definition and expands local inheritance using vanilla's merge semantics.</summary>
    internal static JObject Read(IAsset asset, ICoreAPI? context)
    {
        var token = JObject.Parse(asset.ToText());
        if (token.GetValue("inheritFrom", StringComparison.OrdinalIgnoreCase) == null) return token;
        if (context == null)
            throw new InvalidOperationException("Inherited ecosystem definitions require an isolated local asset context.");
        // EntityType is only the data-loader receiver; its server-dependent registration/population methods are never called.
        LoadInherits(new EntityType(), context, ref token, asset.Location.Domain, asset.Location.ToString());
        return token;
    }
    #endregion

    #region Private
    /// <summary>Checks the exact vanilla inheritance signature and rejects unsupported target versions.</summary>
    private static InheritDefinition BindInheritance()
    {
        MethodInfo? method = typeof(RegistryObjectType).GetMethod("loadInherits", BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(ICoreAPI), typeof(JObject).MakeByRefType(), typeof(string), typeof(string) }, null);
        if (method == null || method.ReturnType != typeof(void))
            throw new NotSupportedException("The installed game's entity inheritance resolver is incompatible with the ecosystem catalogue.");
        return method.CreateDelegate<InheritDefinition>();
    }
    #endregion
}
