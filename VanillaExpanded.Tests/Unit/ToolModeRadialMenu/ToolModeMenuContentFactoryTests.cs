using VanillaExpanded.RadialMenu;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

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

        bool created = ToolModeMenuContentFactory.TryCreate(modes, 1, "Fallback", out ToolModeMenuContent? content);

        Assert.True(created);
        Assert.NotNull(content);
        Assert.Equal(new[] { "0", "1" }, content.Layout.WedgeIds);
        Assert.Equal("current", content.Layout.CenterId);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
        Assert.True(content.Layout.InnerMenu.IsSingleOption);
        Assert.Equal(0.6, content.Layout.RadiusScale);
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
    public void TryCreateSmithingHammer_GroupsDirectionalAndActionModesWithoutChangingIndices()
    {
        string[] codes = ["hit", "upsetup", "upsetright", "upsetdown", "upsetleft", "split"];
        SkillItem[] modes = [.. codes.Select(code => new SkillItem { Code = new AssetLocation(code), Name = code })];

        Assert.True(ToolModeMenuContentFactory.TryCreateSmithingHammer(modes, 0, "Current", out ToolModeMenuContent? content));

        Assert.NotNull(content);
        Assert.Equal(new[] { "1", "2", "3", "4" }, content.Layout.EntryIds);
        RadialMenuLayout actionMenu = Assert.IsType<RadialMenuLayout>(content.Layout.InnerMenu);
        Assert.Equal(new[] { "0", "5" }, actionMenu.EntryIds);
        Assert.True(actionMenu.RenderAsCenter);
        Assert.Null(actionMenu.InnerMenu);
        Assert.Equal(new[] { "1", "2", "3", "4", "0", "5" }, content.Layout.AllEntryIds);
        Assert.True(content.Layout.TryGetEntryCenter("2", 0, 0, 100, out (double X, double Y) right));
        Assert.True(right.X > 0);
        Assert.Equal(0, right.Y, precision: 6);
    }

    [Fact]
    public void TryCreateSmithingHammer_NonVanillaModesUseGenericLayout()
    {
        SkillItem[] modes = [new() { Code = new AssetLocation("custom"), Name = "Custom" }];

        Assert.True(ToolModeMenuContentFactory.TryCreateSmithingHammer(modes, 0, "Current", out ToolModeMenuContent? content));

        Assert.Equal(new[] { "0" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
    }

    [Fact]
    public void TryCreateSmithingHammer_ReorderedExtendedModesGroupByCodeAndPreserveIndices()
    {
        string[] codes = ["split", "upsetdown", "custom", "upsetup", "hit", "upsetleft", "upsetright"];
        SkillItem[] modes = [.. codes.Select(code => new SkillItem { Code = new AssetLocation(code), Name = code })];

        Assert.True(ToolModeMenuContentFactory.TryCreateSmithingHammer(modes, 4, "Current", out ToolModeMenuContent? content));

        Assert.Equal(new[] { "3", "6", "1", "5" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "0", "2", "4" }, content.Layout.InnerMenu!.EntryIds);
        Assert.Equal(codes, content.Entries.Select(entry => entry.Label));
        Assert.Equal("4", content.Layout.InnerMenu.EntryIds[^1]);
    }

    [Fact]
    public void TryCreateSmithingHammer_MissingGroupUsesGenericLayout()
    {
        SkillItem[] directionalOnly =
        [
            new() { Code = new AssetLocation("upsetup"), Name = "Up" },
            new() { Code = new AssetLocation("upsetdown"), Name = "Down" }
        ];

        Assert.True(ToolModeMenuContentFactory.TryCreateSmithingHammer(directionalOnly, 0, "Current", out ToolModeMenuContent? content));

        Assert.Equal(new[] { "0", "1" }, content!.Layout.EntryIds);
        Assert.Equal(new[] { "current" }, content.Layout.InnerMenu!.EntryIds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void TryCreate_InvalidCurrentMode_UsesFallbackLabel(int currentMode)
    {
        SkillItem[] modes = [new() { Code = new AssetLocation("only"), Name = "Only" }];

        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, currentMode, "Current mode", out ToolModeMenuContent? content));

        Assert.Equal("Current mode", content!.Entries[^1].Label);
    }

    [Fact]
    public void TryCreate_UnsupportedModeSets_DeclinesForVanillaFallback()
    {
        Assert.False(ToolModeMenuContentFactory.TryCreate(null, 0, "Current", out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate([], 0, "Current", out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate(new SkillItem[64], 0, "Current", out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate([null!], 0, "Current", out _));
        Assert.False(ToolModeMenuContentFactory.TryCreate([new SkillItem { Name = null! }], 0, "Current", out _));
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