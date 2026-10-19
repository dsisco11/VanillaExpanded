using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Retains stable species selection independently of composer reconstruction and localized sort order.</summary>
internal sealed class EcosystemMapSelection
{
    private readonly Func<EcosystemSpecies, string> localize;
    private IReadOnlyList<EcosystemSpecies> species = Array.Empty<EcosystemSpecies>();
    internal string? SelectedCode { get; private set; }
    internal string[] Codes => species.Select(item => item.Code).ToArray();
    internal string[] Names => species.Select(localize).ToArray();
    internal int SelectedIndex => Math.Max(0, Array.FindIndex(Codes, code => code == SelectedCode));

    #region Public API
    /// <summary>Uses native species naming, with a localization boundary for isolated content checks.</summary>
    internal EcosystemMapSelection(Func<EcosystemSpecies, string>? localize = null)
    { this.localize = localize ?? (item => item.DisplayName); }

    /// <summary>Sorts native localized names and preserves an existing identity when rebuilding the dropdown.</summary>
    internal void SetSpecies(IEnumerable<EcosystemSpecies> source)
    {
        species = source.OrderBy(localize, StringComparer.CurrentCulture)
            .ThenBy(item => item.Code, StringComparer.Ordinal).ToArray();
        if (!species.Any(item => item.Code == SelectedCode)) SelectedCode = species.FirstOrDefault()?.Code;
    }
    /// <summary>Accepts only a known species identity; deselection and stale composer callbacks cannot clear selection.</summary>
    internal void Select(string code, bool selected)
    {
        if (selected && species.Any(item => item.Code == code)) SelectedCode = code;
    }
    /// <summary>Returns one explicitly resolved criteria set without widening definition alternatives.</summary>
    internal HabitatRanges? ActiveRanges(Func<EcosystemSpecies, HabitatRule> resolve)
    {
        var selected = species.FirstOrDefault(item => item.Code == SelectedCode);
        return selected == null ? null : HabitatRanges.From(resolve(selected));
    }
    #endregion
}
