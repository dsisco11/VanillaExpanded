using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Config;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>A stable species identity with localized naming and independent, complete habitat alternatives.</summary>
internal sealed class EcosystemSpecies
{
    internal string Code { get; }
    internal string NameKey { get; }
    internal string DisplayName => Lang.GetMatching(NameKey);
    internal IReadOnlyList<string> EntityCodes { get; }
    internal IReadOnlyList<HabitatRule> Alternatives { get; }

    #region Public API
    /// <summary>Snapshots catalogue data so subsequent asset or collection mutations cannot change an evaluated species.</summary>
    internal EcosystemSpecies(string code, IEnumerable<string> entityCodes, IEnumerable<HabitatRule> alternatives, string? nameKey = null)
    {
        Code = code;
        EntityCodes = Array.AsReadOnly(entityCodes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        Alternatives = Array.AsReadOnly(alternatives.Distinct().ToArray());
        string representative = Alternatives.Select(rule => rule.EntityCode).Order(StringComparer.Ordinal).First();
        var entity = new Vintagestory.API.Common.AssetLocation(representative);
        NameKey = nameKey ?? entity.Domain + ":item-creature-" + entity.Path;
    }
    #endregion
}
