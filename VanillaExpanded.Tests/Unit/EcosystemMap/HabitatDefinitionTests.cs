using Newtonsoft.Json.Linq;
using VanillaExpanded.EcosystemMap.Catalogue;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks immutable rule extraction and variant isolation independently of catalogue presentation.</summary>
public sealed class HabitatDefinitionTests
{
    #region Public API
    /// <summary>Preserves every environmental limit and defaults independently of ignored spawn probability/cap fields.</summary>
    [Fact]
    public void CopiesCompleteEnvironmentalLimits()
    {
        var asset = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-adult.json", """
            {tags:["animal"],code:"deer",variantgroups:[{tags:["animal"],code:"type",states:["elk"]}],
             server:{spawnconditions:{runtime:{minTemp:-7,maxTemp:23,minRain:0.1,maxRain:0.8,
                minForest:0.2,maxForest:0.7,minShrubs:0.3,maxShrubs:0.9,minForestOrShrubs:0.6,minY:0.95,maxY:1.3,
                chance:0,maxQuantity:0,minLightLevel:31,minDistanceToPlayer:999}}}}
            """);
        var entity = CatalogueFixture.Entity("deer-elk-adult-male", ("type","elk"));
        var species = Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { asset }, new[] { entity }));
        var rule = Assert.Single(species.Alternatives);
        Assert.Equal(new HabitatRule("game:deer-elk-adult-male", HabitatSpawnPath.Runtime,
            -7,23,0.1f,0.8f,0.2f,0.7f,0.3f,0.9f,0.6f,0.95f,1.3f), rule);
        asset.Data = System.Text.Encoding.UTF8.GetBytes("{}");
        Assert.Equal(-7, rule.MinTemperature);
        Assert.Throws<NotSupportedException>(() => ((IList<HabitatRule>)species.Alternatives).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)species.EntityCodes).Clear());
    }

    /// <summary>Never applies an age-specific source to a differently aged registered entity.</summary>
    [Fact]
    public void AdultAndBabySourceRulesStayAttachedToTheirOwnIdentity()
    {
        var adult = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-adult.json", """
            {tags:["animal"],code:"deer",variantgroups:[{tags:["animal"],code:"type",states:["elk"]},{tags:["animal"],code:"age",states:["adult"]}],
             server:{spawnconditions:{worldgen:{minTemp:-10,maxTemp:-5}}}}
            """);
        var baby = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-baby.json", """
            {tags:["animal"],code:"deer",variantgroups:[{tags:["animal"],code:"type",states:["elk"]},{tags:["animal"],code:"age",states:["baby"]}],
             server:{spawnconditions:{runtime:{minTemp:15,maxTemp:20}}}}
            """);
        var identities = new[]
        {
            CatalogueFixture.Entity("deer-elk-adult-male", ("type","elk"),("age","adult")),
            CatalogueFixture.Entity("deer-elk-baby-female", ("type","elk"),("age","baby"))
        };
        var species = Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { adult,baby }, identities));
        Assert.Equal(2, species.Alternatives.Count);
        Assert.Contains(species.Alternatives, rule => rule.EntityCode == "game:deer-elk-adult-male" && rule.MinTemperature == -10 && rule.MaxTemperature == -5);
        Assert.Contains(species.Alternatives, rule => rule.EntityCode == "game:deer-elk-baby-female" && rule.MinTemperature == 15 && rule.MaxTemperature == 20);
    }

    /// <summary>Ignores unrelated malformed content and omits disabled or ruleless supported definitions.</summary>
    [Fact]
    public void UnsupportedContentCannotCreateHabitatOrBreakCatalogueParsing()
    {
        var unsupported = CatalogueFixture.Asset("entities/mod-added.json", "invalid json", "othermod");
        var disabled = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-adult.json",
            """{tags:["animal"],code:"deer",enabled:false,server:{spawnconditions:{worldgen:{}}}}""");
        var noRules = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-baby.json", """{tags:["animal"],code:"deer",server:{}}""");
        Assert.Empty(EcosystemSpeciesCatalogue.Build(new[] { unsupported,disabled,noRules },
            new[] { CatalogueFixture.Entity("game:deer-elk", ("type","elk")) }));
    }

    /// <summary>Uses vanilla inherited-clause merging before source matching, with qualified identity codes and child overrides.</summary>
    [Fact]
    public void LocalInheritancePreservesParentClausesAndChildOverrides()
    {
        var parent = CatalogueFixture.Asset("entities/habitat-parent.json", """
            {tags:["animal"],code:"habitat-template",variantgroups:[{tags:["animal"],code:"type",states:["elk"]},{tags:["animal"],code:"age",states:["adult"]}],
             server:{spawnconditions:{worldgen:{minTemp:-5,maxTemp:25,minRain:0.2,maxForest:0.7},runtime:{maxShrubs:0.6}}}}
            """);
        var child = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-adult.json", """
            {inheritFrom:"game:entities/habitat-parent",code:"game:deer",server:{spawnconditions:{worldgen:{maxTemp:10}}}}
            """);
        var entity = CatalogueFixture.Entity("game:deer-elk-adult-male", ("type","elk"),("age","adult"));
        var api = CatalogueFixture.Api(new[] { CatalogueFixture.Origin(new[] { parent,child }) }, new[] { entity });
        var species = Assert.Single(EcosystemSpeciesCatalogue.Load(api));
        var worldgen = Assert.Single(species.Alternatives, rule => rule.SpawnPath == HabitatSpawnPath.Worldgen);
        Assert.Equal(-5, worldgen.MinTemperature);
        Assert.Equal(10, worldgen.MaxTemperature);
        Assert.Equal(0.2f, worldgen.MinRainfall);
        Assert.Equal(0.7f, worldgen.MaxForest);
        Assert.Equal(0.6f, Assert.Single(species.Alternatives, rule => rule.SpawnPath == HabitatSpawnPath.Runtime).MaxShrubs);
        Assert.Contains("maxTemp:25", parent.ToText());
        Assert.Contains("inheritFrom", child.ToText());
        Assert.Throws<InvalidOperationException>(() => EcosystemSpeciesCatalogue.Build(new[] { child }, new[] { entity }));
    }

    /// <summary>Normalizes source codes through vanilla AssetLocation semantics rather than comparing a qualified string to a path.</summary>
    [Theory]
    [InlineData("deer")]
    [InlineData("game:deer")]
    [InlineData("GAME:DEER")]
    public void QualifiedAndUnqualifiedCodesSelectTheSameSpecies(string code)
    {
        var source = CatalogueFixture.Asset("entities/animal/mammal/hooved/deer-adult.json",
            "{tags:[\"animal\"],code:\"" + code + "\",variantgroups:[{code:\"type\",states:[\"elk\"]}],server:{spawnconditions:{worldgen:{minTemp:3}}}}");
        var entity = CatalogueFixture.Entity("game:deer-elk-adult-male", ("type","elk"));
        Assert.Equal("game:deer-elk", Assert.Single(EcosystemSpeciesCatalogue.Build(new[] { source }, new[] { entity })).Code);
    }

    #endregion
}
