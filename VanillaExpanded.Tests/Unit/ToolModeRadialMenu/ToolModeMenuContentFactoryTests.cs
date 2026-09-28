using VanillaExpanded.RadialMenu;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

[Trait("Category", "Unit")]
public sealed class ToolModeMenuContentFactoryTests
{
    [Fact]
    public void SystemLifecycle_LoadsOnlyClientSide()
    {
        var system = new ToolModeRadialMenuSystem();

        Assert.True(system.ShouldLoad(EnumAppSide.Client));
        Assert.False(system.ShouldLoad(EnumAppSide.Server));
    }

    [Fact]
    public void TryCreate_ValidModes_PreservesOrderAndMetadata()
    {
        var modes = new[]
        {
            new SkillItem { Code = new AssetLocation("first"), Name = "First", Description = "Primary", Enabled = true },
            new SkillItem
            {
                Code = new AssetLocation("second"), Name = "Second", Description = "Secondary", Enabled = false,
                RenderHandler = static (_, _, _, _) => { }
            }
        };

        bool created = ToolModeMenuContentFactory.TryCreate(modes, 1, "Fallback",
            GenericToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content);

        Assert.True(created);
        Assert.NotNull(content);
        Assert.Equal(new[] { "0", "1" }, content.Layout.WedgeIds);
        Assert.Equal("current", content.Layout.CenterId);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
        Assert.True(content.Layout.InnerMenu.IsSingleOption);
        Assert.Equal(0.77, content.Layout.RadiusScale);
        Assert.Collection(content.Entries,
            first =>
            {
                Assert.Equal("0", first.Id);
                Assert.Equal("First", first.Label);
                Assert.Equal("Primary", first.Description);
                Assert.True(first.Enabled);
                Assert.Null(first.Icon);
            },
            second =>
            {
                Assert.Equal("1", second.Id);
                Assert.Equal("Second", second.Label);
                Assert.Equal("Secondary", second.Description);
                Assert.False(second.Enabled);
                Assert.IsType<ToolModeSkillIcon>(second.Icon);
            },
            center =>
            {
                Assert.Equal("current", center.Id);
                Assert.Equal("Second", center.Label);
                Assert.False(center.Enabled);
            });
    }

    [Fact]
    public void TryCreateSmithingHammer_UsesPaddedRingWithDirectionalCardinalSlots()
    {
        string[] codes = ["hit", "upsetup", "upsetright", "upsetdown", "upsetleft", "split"];
        SkillItem[] modes = [.. codes.Select(code => new SkillItem { Code = new AssetLocation(code), Name = code })];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 0, "Current",
            SmithingHammerToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.NotNull(content);
        Assert.Equal(new[] { "1", "0", "2", "5", "3", "padding:5", "4", "padding:7" }, content.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
        Assert.Equal(9, content.Entries.Count);
        Assert.All(content.Entries.Where(entry => entry.Id.StartsWith("padding:")), entry =>
        {
            Assert.False(entry.Enabled);
            Assert.Empty(entry.Label);
        });
        Assert.True(content.Layout.TryGetEntryCenter("2", 0, 0, 100, out (double X, double Y) right));
        Assert.True(right.X > 0);
        Assert.Equal(0, right.Y, precision: 6);
    }

    [Fact]
    public void TryCreateSmithingHammer_NonVanillaModesUseGenericLayout()
    {
        SkillItem[] modes = [new() { Code = new AssetLocation("custom"), Name = "Custom" }];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 0, "Current",
            SmithingHammerToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal(new[] { "0" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
    }

    [Fact]
    public void TryCreateSmithingHammer_ReorderedExtendedModesGroupByCodeAndPreserveIndices()
    {
        string[] codes = ["split", "upsetdown", "custom", "upsetup", "hit", "upsetleft", "upsetright"];
        SkillItem[] modes = [.. codes.Select(code => new SkillItem { Code = new AssetLocation(code), Name = code })];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 4, "Current",
            SmithingHammerToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal(new[] { "3", "0", "6", "2", "1", "4", "5", "padding:7" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
        Assert.Equal(codes, content.Entries.Take(codes.Length).Select(entry => entry.Label));
    }

    [Fact]
    public void TryCreateSmithingHammer_MissingGroupUsesGenericLayout()
    {
        SkillItem[] directionalOnly =
        [
            new() { Code = new AssetLocation("upsetup"), Name = "Up" },
            new() { Code = new AssetLocation("upsetdown"), Name = "Down" }
        ];

        Assert.True(ToolModeMenuContentFactory.TryCreate(directionalOnly, 0, "Current",
            SmithingHammerToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal(new[] { "0", "1" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
    }

    [Fact]
    public void TryCreateChisel_GroupsSizesInsideAllOtherOptions()
    {
        SkillItem[] modes =
        [
            new SkillItem { Code = new AssetLocation("1size"), Name = "1x1x1" },
            new SkillItem { Code = new AssetLocation("2size"), Name = "2x2x2" },
            new SkillItem { Code = new AssetLocation("4size"), Name = "4x4x4" },
            new SkillItem { Code = new AssetLocation("8size"), Name = "8x8x8" },
            new SkillItem { Code = new AssetLocation("rotate"), Name = "Rotate" },
            new SkillItem { Code = new AssetLocation("flip"), Name = "Flip" },
            new SkillItem { Code = new AssetLocation("rename"), Name = "Rename" },
            new SkillItem { Code = new AssetLocation("granite"), Name = "Granite", Linebreak = true },
            new SkillItem { Code = new AssetLocation("basalt"), Name = "Basalt" },
            new SkillItem { Code = new AssetLocation("addmat"), Name = "Add material", Enabled = false }
        ];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 2, "Current",
            ChiselToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal(new[] { "4", "5", "6", "7", "8", "9" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "0", "1", "2", "3" }, content.Layout.InnerMenu!.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu.InnerMenu!.EntryIds);
        Assert.Equal("4x4x4", content.Entries[^1].Label);
        Assert.True(content.Entries.Single(entry => entry.Id == "9").Enabled);
    }

    [Fact]
    public void TryCreateChisel_WithoutMaterialLinebreakUsesGenericLayout()
    {
        SkillItem[] modes =
        [
            new() { Code = new AssetLocation("action"), Name = "Action" },
            new() { Code = new AssetLocation("material"), Name = "Material" }
        ];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 0, "Current",
            ChiselToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal(new[] { "0", "1" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
    }

    [Fact]
    public void TryCreateBoatRoller_PlacesFacingModesAtCardinalWedges()
    {
        SkillItem[] modes =
        [
            new() { Code = new AssetLocation("east"), Name = "East" },
            new() { Code = new AssetLocation("north"), Name = "North" },
            new() { Code = new AssetLocation("west"), Name = "West" },
            new() { Code = new AssetLocation("south"), Name = "South" }
        ];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 0, "Current",
            BoatRollerToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal(new[] { "1", "0", "3", "2" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void TryCreate_InvalidCurrentMode_UsesFallbackLabel(int currentMode)
    {
        SkillItem[] modes = [new() { Code = new AssetLocation("only"), Name = "Only" }];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, currentMode, "Current mode",
            GenericToolModeMenuLayoutStrategy.Instance, out ToolModeMenuContent? content));

        Assert.Equal("Current mode", content!.Entries[^1].Label);
    }

    [Fact]
    public void TryCreate_UnsupportedModeSets_DeclinesForVanillaFallback()
    {
        IToolModeMenuLayoutStrategy strategy = GenericToolModeMenuLayoutStrategy.Instance;
        Assert.False(ToolModeMenuContentFactory.TryCreate(null, 0, "Current", strategy, out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate([], 0, "Current", strategy, out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate(new SkillItem[64], 0, "Current", strategy, out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate([null!], 0, "Current", strategy, out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate([new SkillItem { Name = null! }], 0, "Current", strategy, out _));
    }

    [Fact]
    public void StrategyRegistry_ResolvesSpecializedAndGenericCollectibles()
    {
        Assert.Same(SmithingHammerToolModeMenuLayoutStrategy.Instance,
            ToolModeMenuLayoutStrategyRegistry.Resolve(new ItemHammer()));
        Assert.Same(ChiselToolModeMenuLayoutStrategy.Instance,
            ToolModeMenuLayoutStrategyRegistry.Resolve(new ItemChisel()));
        Assert.Same(BoatRollerToolModeMenuLayoutStrategy.Instance,
            ToolModeMenuLayoutStrategyRegistry.Resolve(new ItemRoller()));
        Assert.Same(GenericToolModeMenuLayoutStrategy.Instance,
            ToolModeMenuLayoutStrategyRegistry.Resolve(new Item()));
    }

    [Theory]
    [InlineData("0", true, 0)]
    [InlineData("62", true, 62)]
    [InlineData("current", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("1.0", false, 0)]
    public void TryGetMode_AcceptsOnlyGeneratedIdentifiers(string id, bool expected, int expectedMode)
    {
        bool parsed = ToolModeMenuContentFactory.TryGetMode(id, out int mode);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedMode, mode);
    }
}