using Newtonsoft.Json.Linq;
using VanillaExpanded.EcosystemMap.Catalogue;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks discovery against the installed target's animal definitions without requiring enumerated support mappings.</summary>
public sealed class InstalledSpeciesInventoryTests
{
    #region Public API
    /// <summary>Discovers the current naturally spawning animal types and reuses their vanilla creature-name keys.</summary>
    [Fact]
    public void InstalledWildlifeIsDiscoveredWithNativeNameKeys()
    {
        string install = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? throw new InvalidOperationException("Set VINTAGE_STORY.");
        string assetRoot = Path.Combine(install, "assets", "survival");
        var definitions = Directory.EnumerateFiles(Path.Combine(assetRoot, "entities", "animal"), "*.json", SearchOption.AllDirectories)
            .Where(path => path.Contains(Path.DirectorySeparatorChar + "mammal" + Path.DirectorySeparatorChar)
                || path.Contains(Path.DirectorySeparatorChar + "bird" + Path.DirectorySeparatorChar))
            .Select(path => CatalogueFixture.Asset(Path.GetRelativePath(assetRoot, path).Replace('\\','/'), File.ReadAllText(path))).ToArray();
        var identities = definitions.SelectMany(CatalogueFixture.InstalledIdentities).ToArray();
        var result = EcosystemSpeciesCatalogue.Build(definitions.Where(asset => !asset.Location.Path.Contains("semitamed")), identities);
        string[] expected = { "game:bear-black","game:bear-brown","game:bear-sun","game:bear-panda","game:bear-polar","game:fox-red","game:fox-arctic","game:hare-arctic","game:hare-cape","game:hare-european","game:deer-whitetail","game:deer-redbrocket","game:deer-marsh","game:deer-caribou","game:deer-water","game:deer-pudu","game:deer-elk","game:deer-taruca","game:deer-chital","game:deer-guemal","game:deer-pampas","game:deer-fallow","game:deer-moose","game:goat-angora","game:goat-nubian","game:goat-sirohi","game:goat-valais","game:goat-ibexalp","game:goat-ibexnub","game:goat-markhor","game:goat-mountain","game:goat-muskox","game:goat-takingold","game:goat-turdag","game:pig-eurasian","game:pig-redriver","game:pig-warthog","game:sheep-bighorn","game:sheep-mouflon","game:wolf-eurasian","game:hyena-spotted","game:raccoon-common","game:gazelle-thomson","game:chicken" };
        Assert.Equal(expected.Order(StringComparer.Ordinal), result.Select(species => species.Code).Order(StringComparer.Ordinal));
        Assert.NotEmpty(result);
        Assert.Contains(result, species => species.Code == "game:deer-elk");
        Assert.Contains(result, species => species.Code == "game:deer-fallow");
        Assert.DoesNotContain(result, species => species.Code.Contains("tamed", StringComparison.Ordinal));
        var language = JObject.Parse(File.ReadAllText(Path.Combine(install, "assets", "game", "lang", "en.json")));
        Assert.All(result, species =>
        {
            Assert.NotEmpty(species.Alternatives);
            Assert.All(species.Alternatives, rule => Assert.Contains(rule.EntityCode, species.EntityCodes));
            string representative = species.NameKey["game:item-creature-".Length..];
            Assert.Contains("game:" + representative, species.EntityCodes);
            Assert.NotNull(language[species.NameKey["game:".Length..]]);
        });
    }

    /// <summary>Exercises shipped definition grouping metadata through the real patcher while preserving automatic discovery.</summary>
    [Fact]
    public void ShippedMetadataCombinesGoatBreedsAndUsesVanillaGroupName()
    {
        string install = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? throw new InvalidOperationException("Set VINTAGE_STORY.");
        string assetRoot = Path.Combine(install, "assets", "survival");
        var definitions = Directory.EnumerateFiles(Path.Combine(assetRoot, "entities", "animal", "mammal", "hooved"), "goat-*.json")
            .Select(path => CatalogueFixture.Asset(Path.GetRelativePath(assetRoot, path).Replace('\\','/'), File.ReadAllText(path))).ToArray();
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "VanillaExpanded.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var patch = CatalogueFixture.Asset("patches/ecosystem-species-groups.json",
            File.ReadAllText(Path.Combine(root.FullName, "VanillaExpanded", "assets", "vanillaexpanded", "patches", "ecosystem-species-groups.json")), "vanillaexpanded");
        var api = CatalogueFixture.Api(new[] { CatalogueFixture.Origin(definitions.Append(patch)) },
            definitions.SelectMany(CatalogueFixture.InstalledIdentities));
        var result = EcosystemSpeciesCatalogue.Load(api);
        Assert.Equal(8, result.Count);
        var goat = Assert.Single(result, species => species.Code == "game:goat");
        Assert.Equal("game:creaturegroup-goat", goat.NameKey);
        foreach (string breed in new[] { "angora","nubian","sirohi","valais" })
            Assert.Contains(goat.EntityCodes, code => code.StartsWith("game:goat-" + breed + "-", StringComparison.Ordinal));
        Assert.Contains(result, species => species.Code == "game:goat-muskox");
        Assert.Contains(result, species => species.Code == "game:goat-ibexalp");
        var language = JObject.Parse(File.ReadAllText(Path.Combine(install, "assets", "game", "lang", "en.json")));
        Assert.NotNull(language["creaturegroup-goat"]);
    }

    #endregion
}
