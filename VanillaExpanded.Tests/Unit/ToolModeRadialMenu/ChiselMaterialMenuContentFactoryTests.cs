using Moq;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Config;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

/// <summary>Checks every stack remains reachable through bounded pages and live menu sizing.</summary>
[Collection("ToolModeRadialMenuConfig")]
[Trait("Category", "Unit")]
public sealed class ChiselMaterialMenuContentFactoryTests : IDisposable
{
    #region Public API
    private readonly string originalLocale = Lang.CurrentLocale;
    private readonly ITranslationService? originalEnglish = Lang.AvailableLanguages.GetValueOrDefault("en");
    /// <summary>Provides the locale normally initialized by the game client.</summary>
    public ChiselMaterialMenuContentFactoryTests()
    {
        var translations = new Mock<ITranslationService>();
        translations.Setup(service => service.Get(It.IsAny<string>(), It.IsAny<object[]>())).Returns((string key, object[] args) => key);
        Lang.AvailableLanguages["en"] = translations.Object;
        Lang.ChangeLanguage("en");
    }
    /// <summary>Restores localization after headless content checks.</summary>
    public void Dispose()
    {
        if (originalEnglish is null) Lang.AvailableLanguages.Remove("en");
        else Lang.AvailableLanguages["en"] = originalEnglish;
        Lang.ChangeLanguage(originalLocale);
    }
    #region Content
    /// <summary>The material picker shares independent center and ring controls without changing its gap.</summary>
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(2f, 0.5f)]
    public void PickerUsesCenterAndRingSizeSettings(float centerScale, float ringScale)
    {
        float originalCenter = VanillaExpandedModSystem.Config.ToolModeCenterSize;
        float originalRing = VanillaExpandedModSystem.Config.ToolModeRingSize;
        try
        {
            VanillaExpandedModSystem.Config.ToolModeCenterSize = centerScale;
            VanillaExpandedModSystem.Config.ToolModeRingSize = ringScale;
            ToolModeMenuContent picker = ChiselMaterialMenuContentFactory.Create([]);
            Assert.Equal(0.36 * centerScale, picker.Layout.InnerMenu!.OuterRadius, 6);
            Assert.Equal(0.02, picker.Layout.InnerRadius - picker.Layout.InnerMenu.OuterRadius, 6);
            Assert.Equal(0.62 * ringScale, picker.Layout.OuterRadius - picker.Layout.InnerRadius, 6);
        }
        finally
        {
            VanillaExpandedModSystem.Config.ToolModeCenterSize = originalCenter;
            VanillaExpandedModSystem.Config.ToolModeRingSize = originalRing;
        }
    }

    /// <summary>Keeps an explanatory empty wedge and a separate enabled Back disc.</summary>
    [Fact]
    public void Create_Empty_KeepsBackAvailable()
    {
        var content = ChiselMaterialMenuContentFactory.Create([]);
        Assert.False(content.Layout.IsSingleOption);
        Assert.Equal(ChiselMaterialMenuContentFactory.BackId, content.Layout.CenterId);
        Assert.True(content.Entries.Single(entry => entry.Id == ChiselMaterialMenuContentFactory.BackId).Enabled);
        Assert.Single(content.Entries, entry => !entry.Enabled);
    }

    /// <summary>A single item remains a wedge outside the Back disc and carries a native icon/count.</summary>
    [Fact]
    public void Create_One_KeepsMaterialSeparateFromBack()
    {
        var content = ChiselMaterialMenuContentFactory.Create(Candidates(1));
        Assert.False(content.Layout.IsSingleOption);
        Assert.Equal(ChiselMaterialMenuContentFactory.BackId, content.Layout.HitTest(0, 0, 0, 0, 100));
        var material = content.Entries.Single(entry => ChiselMaterialMenuContentFactory.TryGetCandidateIndex(entry.Id, out _));
        Assert.Equal("Fixture material ×3", material.Label);
        Assert.NotNull(material.Icon);
        Assert.True(content.Layout.TryGetEntryCenter(material.Id, 0, 0, 100, out var center));
        Assert.Equal(material.Id, content.Layout.HitTest(center.X, center.Y, 0, 0, 100));
    }

    /// <summary>Pages exhaustively cover more candidates than the radial shader's entry limit.</summary>
    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(101)]
    public void Create_Pages_CoverEveryStackExactlyOnce(int count)
    {
        var candidates = Candidates(count);
        var seen = new List<int>();
        int pages = (count + ChiselMaterialMenuContentFactory.PageSize - 1) / ChiselMaterialMenuContentFactory.PageSize;
        for (int page = 0; page < pages; page++)
        {
            var content = ChiselMaterialMenuContentFactory.Create(candidates, page);
            Assert.InRange(content.Layout.EntryCount, 2, 15);
            Assert.Equal(page > 0, content.Entries.Any(entry => entry.Id == ChiselMaterialMenuContentFactory.PreviousId));
            Assert.Equal(page < pages - 1, content.Entries.Any(entry => entry.Id == ChiselMaterialMenuContentFactory.NextId));
            foreach (var entry in content.Entries)
                if (ChiselMaterialMenuContentFactory.TryGetCandidateIndex(entry.Id, out int index)) seen.Add(index);
        }
        Assert.Equal(Enumerable.Range(0, count), seen);
        Assert.Equal(ChiselMaterialMenuContentFactory.Create(candidates).Layout.AllEntryIds,
            ChiselMaterialMenuContentFactory.Create(candidates, -10).Layout.AllEntryIds);
        Assert.Equal(ChiselMaterialMenuContentFactory.Create(candidates, pages - 1).Layout.AllEntryIds,
            ChiselMaterialMenuContentFactory.Create(candidates, int.MaxValue).Layout.AllEntryIds);
    }

    #endregion
    #region Sizing and identifiers
    /// <summary>Reads current tool-mode sizing on an already-created page and applies shared bounds.</summary>
    [Fact]
    public void Create_LayoutReadsLiveToolModeSize()
    {
        float original = VanillaExpandedModSystem.Config.ToolModeMenuSize;
        try
        {
            var content = ChiselMaterialMenuContentFactory.Create(Candidates(1));
            VanillaExpandedModSystem.Config.ToolModeMenuSize = 1;
            Assert.Equal(.60, content.Layout.RadiusScale, 6);
            VanillaExpandedModSystem.Config.ToolModeMenuSize = 2;
            Assert.Equal(1.20, content.Layout.RadiusScale, 6);
            VanillaExpandedModSystem.Config.ToolModeMenuSize = 99;
            Assert.Equal(1.50, content.Layout.RadiusScale, 6);
        }
        finally { VanillaExpandedModSystem.Config.ToolModeMenuSize = original; }
    }

    /// <summary>Separates picker identifiers from native numeric modes and navigation.</summary>
    [Theory]
    [InlineData("0", false)]
    [InlineData("chisel-material:back", false)]
    [InlineData("chisel-material:stack:-1", false)]
    [InlineData("chisel-material:stack:0", true)]
    public void CandidateIds_RequireNonnegativePrefixedIndex(string id, bool expected)
    {
        Assert.Equal(expected, ChiselMaterialMenuContentFactory.TryGetCandidateIndex(id, out _));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates stable source addresses and detached presentation stacks.</summary>
    private static IReadOnlyList<ChiselMaterialCandidate> Candidates(int count)
    {
        var inventory = new InventoryGeneric(count, "hotbar", "content-test", null!);
        return Enumerable.Range(0, count).Select(index =>
        {
            var stack = new ItemStack(new NamedBlock { BlockId = index + 1, Code = new AssetLocation("fixture") }, 3);
            inventory[index].Itemstack = stack;
            return new ChiselMaterialCandidate(inventory, index, stack, stack.Clone());
        }).ToArray();
    }

    /// <summary>Supplies an item name without loading language assets.</summary>
    private sealed class NamedBlock : Block
    {
        /// <summary>Returns a deterministic display label for native stack naming.</summary>
        public override string GetHeldItemName(ItemStack stack) => "Fixture material";
    }
    #endregion
}
