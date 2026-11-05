using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Gui;

internal readonly record struct GuiLanguageInputs(string? Locale, ITranslationService? Service,
    IDictionary<string, string>? Entries, EnumLinebreakBehavior? LineBreakBehavior)
{
    /// <summary>Captures a locale's translation service, entry-table identity, and line-breaking behavior.</summary>
    internal static GuiLanguageInputs Capture(string? locale)
    {
        ITranslationService? service = null;
        if (locale != null) Lang.AvailableLanguages.TryGetValue(locale, out service);
        // GetAllEntries also exposes the replacement table after native lazy invalidation.
        return new(locale, service, service?.GetAllEntries(), service?.LineBreakBehavior);
    }

    /// <summary>Compares locale and line-breaking values plus service and table reference identities.</summary>
    internal bool Matches(GuiLanguageInputs other) => Locale == other.Locale
        && ReferenceEquals(Service, other.Service) && ReferenceEquals(Entries, other.Entries)
        && LineBreakBehavior == other.LineBreakBehavior;
}
