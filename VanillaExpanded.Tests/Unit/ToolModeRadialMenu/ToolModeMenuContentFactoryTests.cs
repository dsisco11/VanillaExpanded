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