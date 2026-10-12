using Moq;
using VanillaExpanded.RadialMenu;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit.Abstractions;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

/// <summary>Checks crowded native-style art choices retain their identities and usable concentric geometry.</summary>
[Collection("ToolModeRadialMenuConfig")]
public sealed class GenericToolModeMenuLayoutStrategyTests : IDisposable
{
    private readonly float originalCenter = VanillaExpandedModSystem.Config.ToolModeCenterSize;
    private readonly float originalRing = VanillaExpandedModSystem.Config.ToolModeRingSize;
    private readonly float originalMenu = VanillaExpandedModSystem.Config.ToolModeMenuSize;
    private readonly float originalIcon = VanillaExpandedModSystem.Config.ToolModeIconSize;
    private readonly ITestOutputHelper output;

    #region Public API
    /// <summary>Uses the default geometry and restores shared configuration after every check.</summary>
    public GenericToolModeMenuLayoutStrategyTests(ITestOutputHelper output)
    {
        this.output = output;
        VanillaExpandedModSystem.Config.ToolModeCenterSize = 1;
        VanillaExpandedModSystem.Config.ToolModeRingSize = 1;
        VanillaExpandedModSystem.Config.ToolModeMenuSize = 1;
        VanillaExpandedModSystem.Config.ToolModeIconSize = .75f;
    }

    /// <summary>The native six-by-six art mode shape keeps blank names, metadata, handlers, and selectable original IDs.</summary>
    [Fact]
    public void ThirtySixArtModesKeepIdentityAndHaveLargerIcons()
    {
        var renderedCodes = new List<AssetLocation>();
        var modes = Enumerable.Range(0, 36).Select(index => new SkillItem
        {
            Code = new AssetLocation("game", "art" + index), Name = "", Data = new object(),
            Linebreak = index % 6 == 0,
            RenderHandler = (code, _, _, _) => renderedCodes.Add(code)
        }).ToArray();
        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 17, "Current",
            GenericToolModeMenuLayoutStrategy.Instance, out var content));
        var rings = GetRings(content!.Layout);
        Assert.Equal(new[] { 12, 12, 12 }, rings.Select(ring => ring.EntryIds.Count));
        Assert.Equal(37, content.Entries.Count);
        var api = new Mock<ICoreClientAPI>(); var renderer = new Mock<IRenderAPI>();
        api.SetupGet(value => value.Render).Returns(renderer.Object);
        const double radius = 145.8;
        var oldRing = CreateOldRing(Enumerable.Range(0, 36).Select(index => index.ToString()).ToArray());
        float minimumNew = float.MaxValue, minimumOld = float.MaxValue;
        // Compare the exact upright-square sizing used by art handlers at the default 1080p radius.
        for (int index = 0; index < modes.Length; index++)
        {
            var entry = content.Entries[index];
            Assert.Equal(index.ToString(), entry.Id); Assert.Empty(entry.Label);
            Assert.Equal(modes[index].Description, entry.Description); Assert.Equal(modes[index].Enabled, entry.Enabled);
            var icon = Assert.IsType<ToolModeSkillIcon>(entry.Icon);
            icon.Render(api.Object, 0, 0, 20, entry.Enabled);
            Assert.Same(modes[index].Code, renderedCodes[index]);
            Assert.True(content.Layout.TryGetEntryCenter(entry.Id, 0, 0, radius, out var point));
            Assert.Equal(entry.Id, content.Layout.HitTest(point.X, point.Y, 0, 0, radius));
            Assert.True(ToolModeMenuContentFactory.TryGetMode(entry.Id, out int originalIndex));
            Assert.Equal(index, originalIndex);
            var ring = rings.Single(value => value.EntryIds.Contains(entry.Id));
            int localIndex = ring.EntryIds.ToList().IndexOf(entry.Id);
            float newSize = ring.GetScreenAlignedIconSizePixels(localIndex, radius, RadialMenuWedgeStyle.IconInsetPixels);
            float oldSize = oldRing.GetScreenAlignedIconSizePixels(index, radius, RadialMenuWedgeStyle.IconInsetPixels);
            minimumNew = Math.Min(minimumNew, newSize); minimumOld = Math.Min(minimumOld, oldSize);
            Assert.True(newSize > 2 * oldSize);
        }
        Assert.True(content.Layout.OuterRadius * radius < 540);
        Assert.Equal("current", content.Entries[^1].Id); Assert.Empty(content.Entries[^1].Label);
        output.WriteLine($"Default 1080p minimum artwork side: new {minimumNew * .75f:F3}px; old {minimumOld * .75f:F3}px; outer extent {content.Layout.OuterRadius * radius:F3}px.");
    }

    /// <summary>Boundary counts balance contiguous slices and keep radial bands separated.</summary>
    [Theory]
    [InlineData(12, 1)]
    [InlineData(13, 2)]
    [InlineData(63, 6)]
    public void RingBoundariesAreBalancedAndNonoverlapping(int count, int expectedRings)
    {
        var modes = Enumerable.Range(0, count).Select(index => new SkillItem { Name = "Mode " + index }).ToArray();
        Assert.True(ToolModeMenuContentFactory.TryCreate(modes, 0, "Current",
            GenericToolModeMenuLayoutStrategy.Instance, out var content));
        var rings = GetRings(content!.Layout);
        Assert.Equal(expectedRings, rings.Count);
        Assert.True(rings.Max(ring => ring.EntryIds.Count) - rings.Min(ring => ring.EntryIds.Count) <= 1);
        Assert.All(rings, ring => Assert.InRange(ring.EntryIds.Count, 1, 12));
        Assert.Equal(Enumerable.Range(0, count).Select(index => index.ToString()), rings.SelectMany(ring => ring.EntryIds));
        foreach (var ring in rings)
        {
            Assert.Equal(RadialMenuWedgeStyle.RingGapFraction, ring.InnerRadius - ring.InnerMenu!.OuterRadius, 6);
            Assert.Equal(ToolModeMenuGeometry.DefaultRingThickness, ring.OuterRadius - ring.InnerRadius, 6);
            foreach (string id in ring.EntryIds)
            {
                Assert.True(content.Layout.TryGetEntryCenter(id, 0, 0, 145.8, out var center));
                Assert.Equal(id, content.Layout.HitTest(center.X, center.Y, 0, 0, 145.8));
            }
        }
        if (count == 12) Assert.True(content.Layout.HasSameGeometry(CreateOldRing(rings[0].EntryIds.ToArray())));
    }

    /// <summary>Restores shared live settings after geometry and icon-adapter checks.</summary>
    public void Dispose()
    {
        VanillaExpandedModSystem.Config.ToolModeCenterSize = originalCenter;
        VanillaExpandedModSystem.Config.ToolModeRingSize = originalRing;
        VanillaExpandedModSystem.Config.ToolModeMenuSize = originalMenu;
        VanillaExpandedModSystem.Config.ToolModeIconSize = originalIcon;
    }
    #endregion

    #region Private
    /// <summary>Lists option rings in original mode order, excluding the current-mode center.</summary>
    private static List<RadialMenuLayout> GetRings(RadialMenuLayout layout)
    {
        var rings = new List<RadialMenuLayout>();
        for (RadialMenuLayout? current = layout; current?.InnerMenu != null; current = current.InnerMenu) rings.Add(current);
        rings.Reverse();
        return rings;
    }

    /// <summary>Reconstructs the previous single-ring geometry for a sizing and compatibility comparison.</summary>
    private static RadialMenuLayout CreateOldRing(string[] ids)
    {
        var center = new RadialMenuLayout(["current"], 0, ToolModeMenuGeometry.CenterRadius,
            labelFontScale: ToolModeMenuGeometry.CenterLabelFontScale);
        double inner = center.OuterRadius + RadialMenuWedgeStyle.RingGapFraction;
        return new RadialMenuLayout(ids, inner, ToolModeMenuGeometry.GetOuterRadius(inner), center,
            separatorDegrees: ToolModeMenuGeometry.GenericSeparatorDegrees,
            radiusScale: ToolModeMenuGeometry.DefaultRadiusScale, sizeMultiplier: static () => 1);
    }
    #endregion
}
