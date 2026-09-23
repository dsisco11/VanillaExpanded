using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.Tests.RadialMenu;

/// <summary>Checks generic menu geometry and exactly-once interaction independently of inventory.</summary>
public sealed class RadialMenuTests
{
    #region Geometry
    /// <summary>Checks center, ring gap, wedge boundaries, and the fixed clockwise orientation.</summary>
    [Fact]
    public void HitTestUsesRenderedRadiiAndFixedClockwiseWedges()
    {
        var layout = new RadialMenuLayout(["north", "east", "south", "west"], "center", 0.2, 0.3, 1, separatorDegrees: 1);
        Assert.Equal("center", layout.HitTest(100, 100, 100, 100, 100));
        Assert.Null(layout.HitTest(100, 75, 100, 100, 100));
        Assert.Equal("north", layout.HitTest(100, 40, 100, 100, 100));
        Assert.Equal("east", layout.HitTest(160, 100, 100, 100, 100));
        Assert.Equal("south", layout.HitTest(100, 160, 100, 100, 100));
        Assert.Equal("west", layout.HitTest(40, 100, 100, 100, 100));
        Assert.Null(layout.HitTest(160, 40, 100, 100, 100));
        Assert.Null(layout.HitTest(201, 100, 100, 100, 100));
    }

    /// <summary>Checks counterclockwise layout and transformed icon centers use the same direction as hit testing.</summary>
    [Fact]
    public void CounterclockwiseCentersAgreeWithHitTest()
    {
        var layout = new RadialMenuLayout(["north", "west", "south", "east"], "center", 0.2, 0.3, 1, clockwise: false);
        for (int index = 0; index < layout.WedgeIds.Count; index++)
        {
            (double x, double y) = layout.GetWedgeCenter(index, 230, 110, 80, 0.6);
            Assert.Equal(layout.WedgeIds[index], layout.HitTest(x, y, 230, 110, 80));
        }
    }

    /// <summary>Checks equivalent reopenings preserve mesh geometry while a wedge-count change does not.</summary>
    [Fact]
    public void EquivalentLayoutReusesGeometryAcrossOpenings()
    {
        var first = new RadialMenuLayout(["tool:a", "virtual:light"], "restore", 0.2, 0.3, 1);
        var reopened = new RadialMenuLayout(["generic:a", "generic:b"], "center", 0.2, 0.3, 1);
        var changed = new RadialMenuLayout(["generic:a", "generic:b", "generic:c"], "center", 0.2, 0.3, 1);
        Assert.True(first.HasSameGeometry(reopened));
        Assert.False(first.HasSameGeometry(changed));
    }
    /// <summary>Checks every triangle retains a single entry identifier in the combined mesh.</summary>
    [Fact]
    public void CombinedMeshKeepsEntryIdConstantPerTriangle()
    {
        var layout = new RadialMenuLayout(["one", "two", "three"], "center", 0.2, 0.3, 1);
        var mesh = RadialMenuMesh.Build(layout, 300);
        Assert.True(mesh.VerticesCount > 0);
        Assert.True(mesh.IndicesCount > 0);
        for (int index = 0; index < mesh.IndicesCount; index += 3)
        {
            float id = mesh.Uv[mesh.Indices[index] * 2];
            Assert.Equal(id, mesh.Uv[mesh.Indices[index + 1] * 2]);
            Assert.Equal(id, mesh.Uv[mesh.Indices[index + 2] * 2]);
        }
    }
    /// <summary>Gets the signed area of the first mesh triangle for face-winding assertions.</summary>
    private static float SignedArea(Vintagestory.API.Client.MeshData mesh)
    {
        int a = mesh.Indices[0] * 3;
        int b = mesh.Indices[1] * 3;
        int c = mesh.Indices[2] * 3;
        return (mesh.xyz[b] - mesh.xyz[a]) * (mesh.xyz[c + 1] - mesh.xyz[a + 1])
            - (mesh.xyz[b + 1] - mesh.xyz[a + 1]) * (mesh.xyz[c] - mesh.xyz[a]);
    }
    /// <summary>Checks the maximum supported outer-entry density retains selectable wedge centers.</summary>
    [Fact]
    public void ThirtyFourAvailableWedgesKeepTheirOwnCenters()
    {
        string[] ids = [.. Enumerable.Range(0, 34).Select(index => $"entry:{index}")];
        var layout = new RadialMenuLayout(ids, "center", 0.18, 0.26, 1);
        for (int index = 0; index < ids.Length; index++)
        {
            (double x, double y) = layout.GetWedgeCenter(index, 400, 300, 260, 0.7);
            Assert.Equal(ids[index], layout.HitTest(x, y, 400, 300, 260));
        }
        Assert.True(RadialMenuMesh.Build(layout, 600).VerticesCount >= RadialMenuMesh.Build(layout, 300).VerticesCount);
    }
    /// <summary>Checks both layout directions produce visible triangles under the same face-culling rule.</summary>
    [Fact]
    public void WedgeTriangleWindingIsConsistentAcrossDirections()
    {
        var clockwise = RadialMenuMesh.Build(new RadialMenuLayout(["a", "b"], "center", 0.2, 0.3, 1), 300);
        var counterclockwise = RadialMenuMesh.Build(new RadialMenuLayout(["a", "b"], "center", 0.2, 0.3, 1, clockwise: false), 300);

        Assert.True(SignedArea(clockwise) * SignedArea(counterclockwise) > 0);
    }
    /// <summary>Checks generic entries can carry non-item artwork without inventory types.</summary>
    [Fact]
    public void EntryAcceptsCallerOwnedGenericIcon()
    {
        var icon = new MarkerIcon();
        var entry = new RadialMenuEntry("generic:marker", "Marker", true, icon);
        Assert.Same(icon, entry.Icon);
    }

    /// <summary>Provides non-item artwork for the generic icon-contract test.</summary>
    private sealed class MarkerIcon : IRadialMenuIcon
    {
        /// <inheritdoc />
        public void Render(Vintagestory.API.Client.ICoreClientAPI api, double centerX, double centerY, float sizePixels, bool enabled) { }
    }
    #endregion
    #region Interaction
    /// <summary>Checks disabled content remains present and cannot commit, while enabled selection reports once.</summary>
    [Fact]
    public void DisabledEntryRemainsVisibleAndSelectionReportsOnce()
    {
        var layout = new RadialMenuLayout(["virtual:light", "generic:other"], "center", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(layout, [new("virtual:light", "Light", false), new("generic:other", "Other", true), new("center", "Restore", true)]);
        int selections = 0;
        menu.Selected += _ => selections++;
        menu.Open();
        menu.MovePointer(0, -60, 0, 0, 100);
        Assert.Equal("virtual:light", menu.HoveredId);
        Assert.False(menu.SelectHovered());
        menu.UpdateEntries([new("virtual:light", "Lantern", true), new("generic:other", "Other", true), new("center", "Restore", true)]);
        Assert.True(menu.SelectHovered());
        Assert.False(menu.SelectHovered());
        Assert.Equal(1, selections);
        Assert.Equal("virtual:light", menu.SelectedId);
        menu.Open();
        Assert.Null(menu.SelectedId);
    }

    /// <summary>Checks the enabled center selects its own stable identifier exactly once.</summary>
    [Fact]
    public void CenterSelectionReportsCenterIdentifier()
    {
        var layout = new RadialMenuLayout(["outer"], "unequip", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(layout, [new("outer", "Outer", true), new("unequip", "Unequip", true)]);
        string? selected = null;
        menu.Selected += id => selected = id;
        menu.Open();
        menu.MovePointer(50, 50, 50, 50, 100);
        Assert.True(menu.SelectHovered());
        Assert.Equal("unequip", selected);
        Assert.Equal("unequip", menu.SelectedId);
    }

    /// <summary>An empty outer ring keeps the center selectable and has valid center-only mesh geometry.</summary>
    [Fact]
    public void CenterOnlyLayoutHasNoOuterHitTarget()
    {
        var layout = new RadialMenuLayout([], "unequip", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(layout, [new("unequip", "Unequip", true)]);
        Assert.Null(layout.HitTest(0, -60, 0, 0, 100));
        Assert.Equal("unequip", layout.HitTest(0, 0, 0, 0, 100));
        Assert.True(RadialMenuMesh.Build(layout, 300).VerticesCount > 0);
        menu.Open();
        menu.MovePointer(0, 0, 0, 0, 100);
        Assert.True(menu.SelectHovered());
    }

    /// <summary>Changing the available set replaces hit targets while preserving the open interaction.</summary>
    [Fact]
    public void OpenInteractionCanReplaceWedgeLayout()
    {
        var first = new RadialMenuLayout(["a", "b"], "center", 0.2, 0.3, 1);
        var next = new RadialMenuLayout(["b"], "center", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(first, [new("a", "A", true), new("b", "B", true), new("center", "Center", true)]);
        menu.Open();
        menu.UpdateLayout(next, [new("b", "B", true), new("center", "Center", true)]);
        Assert.True(menu.IsOpen);
        menu.MovePointer(0, -60, 0, 0, 100);
        Assert.Equal("b", menu.HoveredId);
        Assert.True(menu.SelectHovered());
    }
    /// <summary>Checks cancellation reports once and never selects an entry.</summary>
    [Fact]
    public void CancellationReportsWithoutSelection()
    {
        var layout = new RadialMenuLayout(["one"], "center", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(layout, [new("one", "One", true), new("center", "Center", true)]);
        int cancellations = 0;
        int selections = 0;
        menu.Cancelled += () => cancellations++;
        menu.Selected += _ => selections++;
        menu.Open();
        menu.MovePointer(0, 0, 0, 0, 100);
        menu.Cancel();
        menu.Cancel();
        Assert.False(menu.SelectHovered());
        Assert.Equal(1, cancellations);
        Assert.Equal(0, selections);
    }
    #endregion
}










