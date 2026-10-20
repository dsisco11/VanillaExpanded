using Moq;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Provides a collection-scoped language service for native HUD registration and restores shared state afterward.</summary>
public sealed class HudOverlayGeometryLanguage : IDisposable
{
    private readonly ITranslationService? english = Lang.AvailableLanguages.GetValueOrDefault("en");

    #region Public API
    /// <summary>Installs a deterministic English fallback before HUD systems register localized native hotkeys.</summary>
    public HudOverlayGeometryLanguage()
    {
        var language = new Mock<ITranslationService>();
        language.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] _) => key);
        Lang.AvailableLanguages["en"] = language.Object;
    }

    /// <summary>Restores the language service that existed before the nonparallel HUD collection.</summary>
    public void Dispose()
    {
        if (english == null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = english;
    }
    #endregion
}
