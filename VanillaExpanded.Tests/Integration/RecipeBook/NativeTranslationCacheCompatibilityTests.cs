using System.Text.Json;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class NativeTranslationCacheCompatibilityTests
{
    /// <summary>Verifies the dialog observes native same-locale invalidation and reload table replacements.</summary>
    [Fact]
    public void RecipeBookRecomposesAfterNativeSameLocaleTranslationReloads()
    {
        // Arrange
        string originalDefaultLocale = Lang.DefaultLocale;
        try
        {
            using var fixture = new MenuFixture();
            Lang.DefaultLocale = "en";
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(MenuFixture.GetAssetPath("lang", "en.json")))!;
            string payload = JsonSerializer.Serialize(entries);
            var assets = CreateAssetManager(CreateLanguageAsset("en", () => payload, "vanillaexpanded"));
            Lang.AvailableLanguages.Remove("en");
            var logger = Mock.Of<ILogger>();
            Lang.LoadLanguage(logger, assets, "en");
            var service = Assert.IsType<TranslationService>(Lang.AvailableLanguages["en"]);
            fixture.Parent.TryOpen();
            using var calls = new RecipeBookLayoutCallCounter();
            fixture.Toggle();
            var book = Assert.Single(fixture.Menus);
            var initialComposer = book.SingleComposer;
            var initialTable = service.GetAllEntries();
            Assert.Equal(1, calls.MeasurementCalls);
            Assert.Equal(1, calls.FixedCalls);
            Assert.Equal(1, calls.CompositionCalls);

            // Act: native Invalidate reloads lazily through the same locale and service object.
            entries["recipebook-title"] = "Recipe Book title after native invalidation reload";
            payload = JsonSerializer.Serialize(entries);
            service.Invalidate();
            book.OnRenderGUI(0);

            // Assert the dialog observes the replaced table and conditionally rebuilds its measured shell once.
            var invalidatedTable = service.GetAllEntries();
            Assert.Equal("en", Lang.CurrentLocale);
            Assert.Same(service, Lang.AvailableLanguages["en"]);
            Assert.NotSame(initialTable, invalidatedTable);
            Assert.NotSame(initialComposer, book.SingleComposer);
            Assert.Equal(2, calls.MeasurementCalls);
            Assert.Equal(2, calls.FixedCalls);
            Assert.Equal(2, calls.CompositionCalls);

            // A direct same-locale Lang.LoadLanguage reload also replaces the table without replacing its service.
            var reloadedComposer = book.SingleComposer;
            var beforeLoadTable = service.GetAllEntries();
            entries["recipebook-title"] = "Recipe Book title after same-locale load reload";
            payload = JsonSerializer.Serialize(entries);
            Lang.LoadLanguage(logger, assets, "en");
            book.OnRenderGUI(0);

            // Assert
            var loadedTable = service.GetAllEntries();
            Assert.Equal("en", Lang.CurrentLocale);
            Assert.Same(service, Lang.AvailableLanguages["en"]);
            Assert.NotSame(beforeLoadTable, loadedTable);
            Assert.NotSame(reloadedComposer, book.SingleComposer);
            Assert.Equal(3, calls.MeasurementCalls);
            Assert.Equal(3, calls.FixedCalls);
            Assert.Equal(3, calls.CompositionCalls);
            int settledMeasurements = calls.MeasurementCalls;
            int settledPlacements = calls.FixedCalls;
            int settledCompositions = calls.CompositionCalls;
            book.OnRenderGUI(0);
            Assert.Equal(settledMeasurements, calls.MeasurementCalls);
            Assert.Equal(settledPlacements, calls.FixedCalls);
            Assert.Equal(settledCompositions, calls.CompositionCalls);
        }
        finally
        {
            Lang.DefaultLocale = originalDefaultLocale;
        }
    }

    [Fact]
    public void LocaleFallbackAndNativeReloadsReplaceEntryTablesWithoutReplacingServices()
    {
        // Arrange
        string originalLocale = Lang.CurrentLocale;
        string originalDefaultLocale = Lang.DefaultLocale;
        var originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
        var originalCurrent = Lang.AvailableLanguages.GetValueOrDefault("zz");
        var originalAlternateDefault = Lang.AvailableLanguages.GetValueOrDefault("de");
        string? temporaryAssets = null;

        try
        {
            Lang.AvailableLanguages.Remove("en");
            Lang.AvailableLanguages.Remove("zz");
            Lang.AvailableLanguages.Remove("de");

            var englishEntries = new Dictionary<string, string>
            {
                ["probe"] = "english-probe",
                ["fallback"] = "english-fallback"
            };
            var currentEntries = new Dictionary<string, string>
            {
                ["probe"] = "current-v1"
            };
            var alternateDefaultEntries = new Dictionary<string, string>
            {
                ["fallback"] = "alternate-default-fallback"
            };
            var assets = CreateAssetManager(
                CreateLanguageAsset("en", () => JsonSerializer.Serialize(englishEntries)),
                CreateLanguageAsset("zz", () => JsonSerializer.Serialize(currentEntries)),
                CreateLanguageAsset("de", () => JsonSerializer.Serialize(alternateDefaultEntries)));
            var logger = Mock.Of<ILogger>();

            Lang.LoadLanguage(logger, assets, "en");
            Lang.LoadLanguage(logger, assets, "zz");
            Lang.LoadLanguage(logger, assets, "de");
            var english = Assert.IsType<TranslationService>(Lang.AvailableLanguages["en"]);
            var current = Assert.IsType<TranslationService>(Lang.AvailableLanguages["zz"]);
            var alternateDefault = Assert.IsType<TranslationService>(Lang.AvailableLanguages["de"]);
            current.LineBreakBehavior = EnumLinebreakBehavior.AfterCharacter;
            Lang.DefaultLocale = "en";
            Lang.ChangeLanguage("zz");

            var initialTable = current.GetAllEntries();
            Assert.Same(initialTable, current.GetAllEntries());
            Assert.Same(current, Lang.AvailableLanguages["zz"]);
            Assert.Equal(EnumLinebreakBehavior.AfterCharacter, current.LineBreakBehavior);
            Assert.Equal("current-v1", Lang.Get("probe"));
            Assert.Equal("english-fallback", Lang.Get("fallback"));
            Assert.Equal("english-fallback", Lang.GetAllEntries()["game:fallback"]);
            Assert.NotSame(Lang.GetAllEntries(), Lang.GetAllEntries());

            // Act: an invalidation is lazy; the next entry-table request reloads through the same service.
            currentEntries["probe"] = "current-v2";
            current.Invalidate();
            var invalidatedTable = current.GetAllEntries();

            // Assert
            Assert.Same(current, Lang.AvailableLanguages["zz"]);
            Assert.NotSame(initialTable, invalidatedTable);
            Assert.Equal("current-v1", initialTable["game:probe"]);
            Assert.Equal("current-v2", invalidatedTable["game:probe"]);
            Assert.Equal("current-v2", Lang.Get("probe"));
            Assert.Equal(EnumLinebreakBehavior.AfterCharacter, current.LineBreakBehavior);

            // A same-locale Lang.LoadLanguage refresh reuses the service but replaces its entry dictionary.
            currentEntries["probe"] = "current-v3";
            Lang.LoadLanguage(logger, assets, "zz");
            var loadedTable = current.GetAllEntries();
            Assert.Same(current, Lang.AvailableLanguages["zz"]);
            Assert.NotSame(invalidatedTable, loadedTable);
            Assert.Equal("current-v3", Lang.Get("probe"));
            Assert.Equal(EnumLinebreakBehavior.AfterCharacter, current.LineBreakBehavior);

            // An explicit native PreLoad also replaces the table on the same locale service.
            temporaryAssets = Path.Combine(Path.GetTempPath(), "VanillaExpanded-Lang-" + Guid.NewGuid().ToString("N"));
            string languageDirectory = Path.Combine(temporaryAssets, "game", "lang");
            Directory.CreateDirectory(languageDirectory);
            var preloadedEntries = new Dictionary<string, string> { ["probe"] = "preloaded-v1" };
            string currentFile = Path.Combine(languageDirectory, "zz.json");
            File.WriteAllText(currentFile, JsonSerializer.Serialize(preloadedEntries));
            current.PreLoad(temporaryAssets);
            var preloadedTable = current.GetAllEntries();
            Assert.Same(current, Lang.AvailableLanguages["zz"]);
            Assert.NotSame(loadedTable, preloadedTable);
            Assert.Equal("preloaded-v1", Lang.Get("probe"));

            preloadedEntries["probe"] = "preloaded-v2";
            File.WriteAllText(currentFile, JsonSerializer.Serialize(preloadedEntries));
            current.PreLoad(temporaryAssets);
            var refreshedPreloadTable = current.GetAllEntries();
            Assert.Same(current, Lang.AvailableLanguages["zz"]);
            Assert.NotSame(preloadedTable, refreshedPreloadTable);
            Assert.Equal("preloaded-v2", Lang.Get("probe"));
            Assert.Equal(EnumLinebreakBehavior.AfterCharacter, current.LineBreakBehavior);

            // The default locale is a separate measurement input because missing current entries fall back to it.
            Lang.DefaultLocale = "de";
            Assert.Same(alternateDefault, Lang.AvailableLanguages["de"]);
            Assert.Equal("alternate-default-fallback", Lang.Get("fallback"));
            Assert.Equal("english-probe", english.Get("probe"));
        }
        finally
        {
            if (temporaryAssets != null && Directory.Exists(temporaryAssets))
                Directory.Delete(temporaryAssets, recursive: true);
            RestoreLanguage("en", originalEnglish);
            RestoreLanguage("zz", originalCurrent);
            RestoreLanguage("de", originalAlternateDefault);
            Lang.DefaultLocale = originalDefaultLocale;
            Lang.ChangeLanguage(originalLocale);
        }
    }

    private static IAssetManager CreateAssetManager(params IAsset[] assets)
    {
        var origin = new Mock<IAssetOrigin>();
        origin.Setup(value => value.GetAssets(AssetCategory.lang, It.IsAny<bool>()))
            .Returns(() => assets.ToList());
        var manager = new Mock<IAssetManager>();
        manager.SetupGet(value => value.Origins).Returns(new List<IAssetOrigin> { origin.Object });
        return manager.Object;
    }

    /// <summary>Creates a native localization asset with the requested language and domain identity.</summary>
    private static IAsset CreateLanguageAsset(string language, Func<string> getText, string domain = "game")
    {
        var asset = new Mock<IAsset>();
        asset.SetupGet(value => value.Name).Returns(language + ".json");
        asset.SetupGet(value => value.Location).Returns(new AssetLocation(domain + ":lang/" + language + ".json"));
        asset.Setup(value => value.ToText()).Returns(getText);
        return asset.Object;
    }

    private static void RestoreLanguage(string locale, ITranslationService? service)
    {
        if (service == null) Lang.AvailableLanguages.Remove(locale);
        else Lang.AvailableLanguages[locale] = service;
    }
}
