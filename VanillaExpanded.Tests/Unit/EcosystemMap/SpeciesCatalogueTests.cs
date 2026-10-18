using System.Text;
using Newtonsoft.Json.Linq;
using VanillaExpanded.EcosystemMap.Catalogue;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks species grouping, complete alternatives and the real vanilla variant/patch boundaries.</summary>
public sealed class SpeciesCatalogueTests
{
    private const string DeerPath = "entities/animal/mammal/hooved/deer-adult.json";
    private const string Definition = """
        {tags:["animal"],code:"deer",variantgroups:[{tags:["animal"],code:"type",states:["elk","fallow"]},{tags:["animal"],code:"age",states:["adult"]},{tags:["animal"],code:"gender",states:["male","female"]}],
         server:{spawnconditions:{worldgen:{minTemp:-8,maxTemp:2,minRain:0.2,maxRain:0.4,minY:0.9,maxY:1.2},
                                 runtime:{minTemp:12,maxTemp:20,minRain:0.7,maxRain:0.9}}}}
        """;

    #region Public API
    /// <summary>Keeps species distinct, combines sex/cosmetics and preserves disjoint rules from both spawn paths.</summary>
    [Fact]
    public void GroupsVariantsWithoutWideningRules()
    {
        var source = CatalogueFixture.Asset(DeerPath, Definition);
        var entities = new[]
        {
            CatalogueFixture.Entity("deer-elk-adult-male", ("type","elk"), ("age","adult"), ("gender","male")),
            CatalogueFixture.Entity("deer-elk-adult-female", ("type","elk"), ("age","adult"), ("gender","female"), ("coat","white")),
            CatalogueFixture.Entity("deer-fallow-adult-male", ("type","fallow"), ("age","adult"), ("gender","male"))
        };
        var catalogue = EcosystemSpeciesCatalogue.Build(new[] { source }, entities);
        Assert.Equal(new[] { "game:deer-elk", "game:deer-fallow" }, catalogue.Select(species => species.Code));
        var elk = catalogue[0];
        Assert.Equal(2, elk.EntityCodes.Count);
        Assert.Equal(4, elk.Alternatives.Count);
        Assert.All(elk.Alternatives.Where(rule => rule.SpawnPath == HabitatSpawnPath.Worldgen),
            rule => { Assert.Equal(-8, rule.MinTemperature); Assert.Equal(2, rule.MaxTemperature); Assert.Equal(0.9f, rule.MinElevation); Assert.Equal(1.2f, rule.MaxElevation); });
        Assert.All(elk.Alternatives.Where(rule => rule.SpawnPath == HabitatSpawnPath.Runtime),
            rule => { Assert.Equal(12, rule.MinTemperature); Assert.Equal(20, rule.MaxTemperature); });
        Assert.Equal(Definition, source.ToText());
        Assert.Equal("game:item-creature-deer-elk-adult-female", elk.NameKey);
    }

    /// <summary>Uses vanilla byType first-match and climate override behavior, including placeholder substitution and default limits.</summary>
    [Fact]
    public void ReusesVanillaByTypeAndSharedClimateOverride()
    {
        var source = CatalogueFixture.Asset(DeerPath, """
            {tags:["animal"],code:"deer",variantgroups:[{tags:["animal"],code:"type",states:["elk"]}],
             server:{spawnconditions:{worldgen:{minTemp:-10},runtime:{minTemp:30}},
                     spawnconditionsByType:{"deer-elk-*":{climate:{minTemp:5,maxTemp:15,minForest:0.4,minShrubs:0.2,minForestOrShrubs:0.6}},
                                           "*":{climate:{minTemp:99}}}}}
            """);
        var entity = CatalogueFixture.Entity("deer-elk-adult-male", ("type","elk"));
        var result = Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { source }, new[] { entity }));
        Assert.Equal(2, result.Alternatives.Count);
        Assert.All(result.Alternatives, rule =>
        {
            Assert.Equal(5, rule.MinTemperature);
            Assert.Equal(15, rule.MaxTemperature);
            Assert.Equal(0.4f, rule.MinForest);
            Assert.Equal(0.2f, rule.MinShrubs);
            Assert.Equal(0.6f, rule.MinForestOrShrubs);
            Assert.Equal(0, rule.MinRainfall);
            Assert.Equal(1, rule.MaxRainfall);
            Assert.Equal(2, rule.MaxElevation);
        });
        var token = JObject.Parse("""{server:{spawnconditions:{worldgen:{companions:["deer-{type}-baby-female"]}}}}""");
        var resolved = VanillaHabitatDefinitionResolver.Resolve(token, entity);
        Assert.Equal("game:deer-elk-baby-female", Assert.Single(resolved!.Worldgen.Companions).ToString());
        Assert.Contains("{type}", token.ToString());
    }

    /// <summary>Combines chicken sex-in-age variants and companion-only young without inventing unconstrained rules.</summary>
    [Fact]
    public void CombinesChickenAgesAndRetainsCompanionIdentities()
    {
        var adult = CatalogueFixture.Asset("entities/animal/bird/chicken-adult.json", """
            {tags:["animal"],code:"chicken",variantgroups:[{tags:["animal"],code:"age",states:["hen","henpoult","rooster","roosterpoult"]}],
             server:{spawnconditionsByType:{"*-rooster":{worldgen:{minRain:0.32},runtime:{minShrubs:0.5}},"*":null}}}
            """);
        var baby = CatalogueFixture.Asset("entities/animal/bird/chicken-baby.json", """{tags:["animal"],code:"chicken-baby",attributes:{handbook:{groupcode:"creaturegroup-chicken"}},server:{}}""");
        var identities = new[] { "hen", "henpoult", "rooster", "roosterpoult" }
            .Select(age => CatalogueFixture.Entity("chicken-" + age, ("age", age)))
            .Append(CatalogueFixture.Entity("chicken-baby"));
        var species = Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { adult, baby }, identities));
        Assert.Equal("game:chicken", species.Code);
        Assert.Equal(5, species.EntityCodes.Count);
        Assert.Equal(2, species.Alternatives.Count);
        Assert.All(species.Alternatives, rule => Assert.Equal("game:chicken-rooster", rule.EntityCode));
    }

    /// <summary>Excludes monsters, fish, insects and domestic-only definitions by category metadata rather than a species whitelist.</summary>
    [Theory]
    [InlineData("entities/animal/fish/new.json", "animal", "Land")]
    [InlineData("entities/animal/invertebrate/new.json", "animal", "Land")]
    [InlineData("entities/custom/new.json", "rust-creature", "Land")]
    [InlineData("entities/custom/new.json", "mechanical", "Land")]
    [InlineData("entities/custom/new.json", "insect", "Air")]
    [InlineData("entities/custom/new.json", "fish", "Underwater")]
    [InlineData("entities/custom/new.json", "tamed", "Land")]
    public void ExcludesNonWildlifeMetadata(string path, string tag, string habitat)
    {
        var source = CatalogueFixture.Asset(path, "{code:\"newanimal\",tags:[\"animal\",\"" + tag
            + "\"],habitat:\"" + habitat + "\",server:{spawnconditions:{worldgen:{}}}}", "wildlife");
        Assert.Empty(EcosystemSpeciesCatalogue.Build(new[] { source }, new[] { CatalogueFixture.Entity("wildlife:newanimal") }));
    }

    /// <summary>Discovers new families, domains and semantic types without adding any catalogue mapping or language entry.</summary>
    [Fact]
    public void DiscoversModAddedWildlifeWithoutRegistrationLists()
    {
        var source = CatalogueFixture.Asset("entities/custom/otter.json", """
            {code:"otter",tags:["animal"],variantgroups:[{code:"type",states:["river","sea"]},{code:"age",states:["adult","baby"]}],
             server:{spawnconditions:{runtime:{group:"hostile",minRain:0.4}}}}
            """, "wildlife");
        var entities = new[]
        {
            CatalogueFixture.Entity("wildlife:otter-river-adult", ("type","river"),("age","adult")),
            CatalogueFixture.Entity("wildlife:otter-river-baby", ("type","river"),("age","baby")),
            CatalogueFixture.Entity("wildlife:otter-sea-adult", ("type","sea"),("age","adult"))
        };
        var result = EcosystemSpeciesCatalogue.Build(new[] { source }, entities);
        Assert.Equal(new[] { "wildlife:otter-river", "wildlife:otter-sea" }, result.Select(species => species.Code));
        Assert.Equal(2, result[0].EntityCodes.Count);
        Assert.StartsWith("wildlife:item-creature-otter-", result[0].NameKey);
    }


    /// <summary>Joins separate age sources using handbook metadata after vanilla byType and placeholder expansion.</summary>
    [Fact]
    public void ResolvedHandbookGroupsUnifyYoungAndAdultSources()
    {
        var adult = CatalogueFixture.Asset("entities/custom/adult.json", """
            {code:"otteradult",tagsByType:{"*":["animal"]},variantgroups:[{code:"species",states:["river"]}],
             attributesByType:{"*":{handbook:{groupcode:"creaturegroup-{species}"}}},
             server:{spawnconditions:{runtime:{minRain:0.4}}}}
            """, "wildlife");
        var baby = CatalogueFixture.Asset("entities/custom/baby.json", adult.ToText().Replace("otteradult","otterbaby"), "wildlife");
        var result = Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { adult,baby }, new[]
        {
            CatalogueFixture.Entity("wildlife:otteradult-river", ("species","river")),
            CatalogueFixture.Entity("wildlife:otterbaby-river", ("species","river"))
        }));
        Assert.Equal("wildlife:river", result.Code);
        Assert.Equal(2, result.EntityCodes.Count);
    }

    /// <summary>Aquatic habitat does not make a tagged mammal into a fish.</summary>
    [Fact]
    public void AquaticWildlifeWithoutFishMetadataRemainsDiscoverable()
    {
        var source = CatalogueFixture.Asset("entities/custom/dolphin.json",
            """{code:"dolphin",tags:["animal"],habitat:"Underwater",server:{spawnconditions:{runtime:{}}}}""", "wildlife");
        Assert.Equal("wildlife:dolphin", Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { source },
            new[] { CatalogueFixture.Entity("wildlife:dolphin") })).Code);
    }


    /// <summary>Authored metadata merges cosmetic type variants of unknown species and points at an existing native name key.</summary>
    [Fact]
    public void DefinitionMetadataMergesTypesWithoutSupportedSpeciesLists()
    {
        var source = CatalogueFixture.Asset("entities/custom/otter.json", """
            {code:"otter",tags:["animal"],variantgroups:[{code:"type",states:["brown","silver"]}],
             attributes:{ecosystemMap:{speciesGroup:"riverotter",nameKey:"creaturegroup-riverotter"}},
             server:{spawnconditions:{runtime:{minRain:0.4}}}}
            """, "wildlife");
        var result = Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { source }, new[]
        {
            CatalogueFixture.Entity("wildlife:otter-brown", ("type","brown")),
            CatalogueFixture.Entity("wildlife:otter-silver", ("type","silver"))
        }));
        Assert.Equal("wildlife:riverotter", result.Code);
        Assert.Equal("wildlife:creaturegroup-riverotter", result.NameKey);
        Assert.Equal(2, result.EntityCodes.Count);
        Assert.Equal(2, result.Alternatives.Count);
    }

    /// <summary>Runs actual vanilla JSON patches in a client-shaped context, preserving originals and later-origin replacement precedence.</summary>
    [Fact]
    public void AppliesLocalPatchesInIsolationWithoutServerCatalogue()
    {
        var original = CatalogueFixture.Asset(DeerPath, Definition);
        var replacement = CatalogueFixture.Asset(DeerPath, Definition.Replace("minTemp:-8", "minTemp:-6"));
        var patch = CatalogueFixture.Asset("patches/habitat.json", """
            [
              {file:"game:entities/animal/mammal/hooved/deer-adult.json",op:"replace",path:"/server/spawnconditions/worldgen/minRain",value:0.55},
              {file:"game:entities/animal/mammal/hooved/deer-*",op:"replace",path:"/server/spawnconditions/runtime/maxTemp",value:27,condition:{when:"ecosystem-test",isValue:"true"}},
              {file:"game:entities/animal/mammal/hooved/deer-adult.json",op:"replace",path:"/server/spawnconditions/runtime/minTemp",value:99,side:"client"},
              {file:"game:entities/animal/mammal/hooved/deer-adult.json",op:"replace",path:"/server/spawnconditions/runtime/minTemp",value:88,dependsOn:[{modid:"missing-mod"}]}
            ]
            """, "patchmod");
        var resourceReplacement = CatalogueFixture.Asset(DeerPath, Definition.Replace("minTemp:-8", "minTemp:99"));
        var entity = CatalogueFixture.Entity("deer-elk-adult-male", ("type","elk"), ("age","adult"), ("gender","male"));
        var config = new TreeAttribute();
        config.SetBool("ecosystem-test", true);
        var api = CatalogueFixture.Api(new[]
        {
            CatalogueFixture.Origin(new[] { original }),
            CatalogueFixture.Origin(new[] { replacement, patch }),
            CatalogueFixture.Origin(new[] { resourceReplacement }, gameplay:false)
        }, new[] { entity }, config);
        byte[] before = (byte[])replacement.Data.Clone();
        var result = Assert.Single(EcosystemSpeciesCatalogue.Load(api));
        var worldgen = Assert.Single(result.Alternatives, rule => rule.SpawnPath == HabitatSpawnPath.Worldgen);
        var runtime = Assert.Single(result.Alternatives, rule => rule.SpawnPath == HabitatSpawnPath.Runtime);
        Assert.Equal(-6, worldgen.MinTemperature);
        Assert.Equal(0.55f, worldgen.MinRainfall);
        Assert.Equal(12, runtime.MinTemperature);
        Assert.Equal(27, runtime.MaxTemperature);
        Assert.Equal(before, replacement.Data);
        Assert.False(replacement.IsPatched);
        Assert.Equal(Definition, original.ToText());
        Assert.Contains("value:0.55", patch.ToText());
        Assert.Null(entity.Server);
        Assert.Equal(EnumAppSide.Client, api.Side);
        Assert.Throws<NotSupportedException>(() => LocalPatchApi.Create(api, new LocalEntityAssetStore()).RegisterEntityClass("unwanted", entity));
    }

    /// <summary>Returns private collections and byte buffers, preventing mutation of the original manager or assets.</summary>
    [Fact]
    public void PrivateAssetViewDoesNotAliasLiveData()
    {
        var original = CatalogueFixture.Asset(DeerPath, Definition);
        var store = new LocalEntityAssetStore();
        store.Copy(original);
        var copy = store.Get(original.Location);
        Assert.NotSame(original, copy);
        Assert.NotSame(original.Data, copy.Data);
        Assert.NotSame(original.Location, copy.Location);
        store.AllAssets.Clear();
        store.GetMany("entities/").Clear();
        Assert.True(store.Exists(original.Location));
        copy.Data = Encoding.UTF8.GetBytes("{}");
        Assert.Equal(Definition, original.ToText());
    }
    #endregion
}
