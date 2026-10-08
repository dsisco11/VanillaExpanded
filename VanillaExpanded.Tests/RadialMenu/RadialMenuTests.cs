using VanillaExpanded.RadialMenu;

namespace VanillaExpanded.Tests.RadialMenu;

/// <summary>Checks generic menu geometry and exactly-once interaction independently of inventory.</summary>
public sealed class RadialMenuTests
{
    [Fact]
    public void RingModeUsesEngineSupportedIntegerUniform()
    {
        string shaderRoot = Path.Combine(AppContext.BaseDirectory, "Mods", "mod", "assets", "vanillaexpanded", "shaders");
        if (!Directory.Exists(shaderRoot)) return;
        foreach (string path in new[] { Path.Combine(shaderRoot, "radial_menu.vsh"), Path.Combine(shaderRoot, "radial_menu.fsh") })
        {
            string source = File.ReadAllText(path);
            Assert.Contains("uniform int ringMode;", source);
            Assert.DoesNotContain("uniform bool", source);
        }
    }

    #region Geometry
    /// <summary>Checks center, ring gap, wedge boundaries, and the fixed clockwise orientation.</summary>
    [Fact]
    public void HitTestUsesRenderedRadiiAndFixedClockwiseWedges()
    {
        var layout = new RadialMenuLayout(["north", "east", "south", "west"], "center", 0.2, 0.3, 1, separatorDegrees: 1);
        Assert.Equal(1, layout.RadiusScale);
        Assert.Equal("center", layout.HitTest(100, 100, 100, 100, 100));
        Assert.Null(layout.HitTest(100, 75, 100, 100, 100));
        Assert.Equal("north", layout.HitTest(100, 40, 100, 100, 100));
        Assert.Equal("east", layout.HitTest(160, 100, 100, 100, 100));
        Assert.Equal("south", layout.HitTest(100, 160, 100, 100, 100));
        Assert.Equal("west", layout.HitTest(40, 100, 100, 100, 100));
        Assert.Equal("east", layout.HitTest(160, 40, 100, 100, 100));
        Assert.Equal("east", layout.HitTest(201, 100, 100, 100, 100));
    }

    [Fact]
    public void NestedMenusHitTestFromOuterRingToSingleOptionDisc()
    {
        var inner = new RadialMenuLayout(["center"], 0, 0.2);
        var middle = new RadialMenuLayout(["inner-north", "inner-south"], 0.3, 0.55, inner);
        var outer = new RadialMenuLayout(["outer-north", "outer-south"], 0.65, 1, middle);

        Assert.Equal(new[] { "outer-north", "outer-south", "inner-north", "inner-south", "center" }, outer.AllEntryIds);
        Assert.Equal("center", outer.HitTest(0, 0, 0, 0, 100));
        Assert.Equal("inner-north", outer.HitTest(0, -45, 0, 0, 100));
        Assert.Equal("outer-north", outer.HitTest(0, -80, 0, 0, 100));
        Assert.Null(outer.HitTest(0, -60, 0, 0, 100));
    }

    [Fact]
    public void MultiOptionCenterMenuOccupiesOnlyCenterDisc()
    {
        var centerMenu = new RadialMenuLayout(["north", "south"], 0, 0.25, renderAsCenter: true);
        var outer = new RadialMenuLayout(["outer-north", "outer-south"], 0.30, 1, centerMenu);

        Assert.True(centerMenu.RenderAsCenter);
        Assert.Equal("north", outer.HitTest(0, -20, 0, 0, 100));
        Assert.Equal("south", outer.HitTest(0, 20, 0, 0, 100));
        Assert.Null(outer.HitTest(0, -27, 0, 0, 100));
        Assert.Equal("outer-north", outer.HitTest(0, -50, 0, 0, 100));
    }

    [Theory]
    [InlineData("inner-north")]
    [InlineData("center")]
    public void NestedInteractionSelectsEntriesAtEveryDepth(string targetId)
    {
        var inner = new RadialMenuLayout(["center"], 0, 0.2);
        var middle = new RadialMenuLayout(["inner-north", "inner-south"], 0.3, 0.55, inner);
        var outer = new RadialMenuLayout(["outer-north", "outer-south"], 0.65, 1, middle);
        var interaction = new RadialMenuInteraction(outer,
        [
            new("outer-north", "Outer North", true), new("outer-south", "Outer South", true),
            new("inner-north", "Inner North", true), new("inner-south", "Inner South", true),
            new("center", "Center", true)
        ]);
        Assert.True(outer.TryGetEntryCenter(targetId, 0, 0, 100, out (double X, double Y) position));

        interaction.Open();
        interaction.MovePointer(position.X, position.Y, 0, 0, 100);

        Assert.Equal(targetId, interaction.HoveredId);
        Assert.True(interaction.SelectHovered());
        Assert.Equal(targetId, interaction.SelectedId);
    }

    [Fact]
    public void NestedLayoutRejectsDuplicateIdsAndMoreThanShaderCapacity()
    {
        var duplicate = new RadialMenuLayout(["duplicate"], 0, 0.2);
        Assert.Throws<ArgumentException>(() => new RadialMenuLayout(["duplicate", "other"], 0.3, 1, duplicate));

        string[] outerIds = [.. Enumerable.Range(0, 63).Select(index => $"outer:{index}")];
        var inner = new RadialMenuLayout(["inner:a", "inner:b"], 0.1, 0.2);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadialMenuLayout(outerIds, 0.3, 1, inner));
    }

    /// <summary>Rounded wedge corners remain outside the pointer target while separator gaps snap to a wedge.</summary>
    [Fact]
    public void RoundedCornersDoNotSelectTheirCutouts()
    {
        var layout = new RadialMenuLayout(["north", "east", "south", "west"], "center", 0.2, 0.3, 1,
            separatorDegrees: 1);
        double nearCornerAngle = 43.8 * Math.PI / 180d;
        double nearCornerRadius = 0.31 * 100d;
        Assert.Null(layout.HitTest(Math.Sin(nearCornerAngle) * nearCornerRadius,
            -Math.Cos(nearCornerAngle) * nearCornerRadius, 0, 0, 100));
        Assert.Equal("north", layout.HitTest(0, -45, 0, 0, 100));
        Assert.Null(layout.HitTest(70, -70, 0, 0, 100));
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

    [Fact]
    public void SingleOptionMeshIsADiscWithItsGlobalEntryId()
    {
        var layout = new RadialMenuLayout(["only"], 0, 0.25);
        var mesh = RadialMenuMesh.Build(layout, 300, entryOffset: 4);

        Assert.True(mesh.VerticesCount > 0);
        for (int index = 0; index < mesh.VerticesCount; index++) Assert.Equal(4, mesh.Uv[index * 2]);
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

    [Fact]
    public void IconSizeFitsRingThicknessAndShrinksForDenseWedges()
    {
        var sparse = new RadialMenuLayout(["a", "b", "c", "d"], 0.3, 0.6);
        string[] denseIds = [.. Enumerable.Range(0, 16).Select(index => index.ToString())];
        var dense = new RadialMenuLayout(denseIds, 0.3, 0.6);

        float sparseSize = sparse.GetIconSizePixels(200, 4);
        float denseSize = dense.GetIconSizePixels(200, 4);

        Assert.True(sparseSize <= (sparse.OuterRadius - sparse.InnerRadius) * 200);
        Assert.True(denseSize < sparseSize);
        Assert.True(denseSize > 0);
    }

    /// <summary>Every icon corner stays inside its wedge across sparse and dense menus and screen directions.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void IconCornersRemainWithinTheirWedges(int count)
    {
        string[] ids = [.. Enumerable.Range(0, count).Select(index => index.ToString())];
        var layout = new RadialMenuLayout(ids, 0.40, 0.90, separatorDegrees: 1.5);
        const double radius = 600;
        double halfSize = layout.GetIconSizePixels(radius, 9) / 2d;
        for (int index = 0; index < count; index++)
        {
            (double x, double y) = layout.GetWedgeCenter(index, 0, 0, radius, 0.65);
            foreach (int horizontal in new[] { -1, 1 })
                foreach (int vertical in new[] { -1, 1 })
                    Assert.Equal(ids[index], layout.HitTest(x + horizontal * halfSize,
                        y + vertical * halfSize, 0, 0, radius));
        }
    }

    /// <summary>Cached sizes refresh for screen-radius and padding changes without retaining stale entry values.</summary>
    [Fact]
    public void UprightIconCacheRefreshesForScreenSpaceChanges()
    {
        string[] ids = ["a", "b", "c", "d"];
        var layout = new RadialMenuLayout(ids, 0.50, 0.74, startAngleDegrees: 13);
        foreach ((double radius, double inset) in new[] { (600d, 7.5d), (300d, 7.5d), (300d, 15d), (600d, 7.5d) })
        {
            var fresh = new RadialMenuLayout(ids, 0.50, 0.74, startAngleDegrees: 13);
            for (int index = 0; index < ids.Length; index++)
            {
                float expected = fresh.GetScreenAlignedIconSizePixels(index, radius, inset);
                Assert.Equal(expected, layout.GetScreenAlignedIconSizePixels(index, radius, inset));
                Assert.Equal(expected, layout.GetScreenAlignedIconSizePixels(index, radius, inset));
            }
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetScreenAlignedIconSizePixels(4, 600, 7.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetScreenAlignedIconSizePixels(0, 0, 7.5));
    }

    /// <summary>Upright widget artwork uses more of cardinal wedges than the rotation-safe fit.</summary>
    [Fact]
    public void UprightIconUsesAvailableRingThickness()
    {
        var layout = new RadialMenuLayout(["a", "b", "c", "d"], 0.50, 0.74, separatorDegrees: 1.5);
        Assert.True(layout.GetScreenAlignedIconSizePixels(0, 600, 7.5)
            > layout.GetIconSizePixels(600, 7.5) * 1.25f);
    }

    /// <summary>Upright squares retain padding along their entire edges, including the concave inner arc.</summary>
    [Theory]
    [InlineData(2, 0, true, 0.50, 1.0)]
    [InlineData(4, 0, true, 0.50, 0.74)]
    [InlineData(4, 45, false, 0.50, 0.74)]
    [InlineData(8, 13, false, 0.76, 1.0)]
    [InlineData(16, 0, true, 0.50, 1.0)]
    [InlineData(32, 7, true, 0.50, 1.0)]
    public void UprightIconEdgesRetainWedgePadding(int count, double startAngle, bool clockwise,
        double innerRadius, double outerRadius)
    {
        string[] ids = [.. Enumerable.Range(0, count).Select(index => index.ToString())];
        var layout = new RadialMenuLayout(ids, innerRadius, outerRadius,
            startAngleDegrees: startAngle, clockwise: clockwise, separatorDegrees: 1.5);
        const double radius = 600;
        const double inset = 7.5;
        for (int index = 0; index < count; index++)
        {
            double halfSize = layout.GetScreenAlignedIconSizePixels(index, radius, inset) / 2d;
            (double x, double y) = layout.GetWedgeCenter(index, 0, 0, radius, (innerRadius + outerRadius) / 2d);
            double angle = (startAngle + (clockwise ? 1 : -1) * index * layout.StepDegrees) * Math.PI / 180d;
            double halfAngle = (layout.StepDegrees / 2d - layout.SeparatorDegrees) * Math.PI / 180d;
            // Check the exact nearest point as well as sampled perimeter points: corner-only
            // checks can miss an upright edge cutting through the inner circular boundary.
            double nearestX = Math.Max(0d, Math.Abs(x) - halfSize);
            double nearestY = Math.Max(0d, Math.Abs(y) - halfSize);
            Assert.True(Math.Sqrt(nearestX * nearestX + nearestY * nearestY) >= innerRadius * radius + inset - 0.001);
            for (int sample = 0; sample <= 8; sample++)
                foreach (int side in new[] { -1, 1 })
                    foreach (bool horizontal in new[] { false, true })
                    {
                        double along = halfSize * (sample / 4d - 1d);
                        double px = x + (horizontal ? along : side * halfSize);
                        double py = y + (horizontal ? side * halfSize : along);
                        double distance = Math.Sqrt(px * px + py * py);
                        double delta = Math.Atan2(px * Math.Cos(angle) + py * Math.Sin(angle),
                            px * Math.Sin(angle) - py * Math.Cos(angle));
                        Assert.True(distance <= outerRadius * radius - inset + 0.001);
                        Assert.True(distance * Math.Sin(halfAngle - Math.Abs(delta)) >= inset - 0.001);
                        Assert.Equal(ids[index], layout.HitTest(px, py, 0, 0, radius));
                    }
        }
    }

    [Theory]
    [InlineData("2x2x2", "2x2x2")]
    [InlineData("Andesite Cobblestone", "Andesite\nCobblestone")]
    public void CenterLabelLayoutPreservesWholeWords(string text, string expected)
    {
        string wrapped = RadialMenuLabelLayout.FitToCircle(text, 15, 3, value => value.Length, 0);

        Assert.Equal(expected, wrapped);
    }

    [Fact]
    public void CenterLabelLayoutCollapsesWhitespaceAndAvoidsEmptyLines()
    {
        string wrapped = RadialMenuLabelLayout.FitToCircle(
            "Drop  blocks\n\nhere to add a new material", 18, 3, value => value.Length, 0);

        Assert.DoesNotContain("\n\n", wrapped);
        Assert.Equal("Drop blocks here to add a new material", wrapped.Replace('\n', ' '));
    }

    [Fact]
    public void CenterLabelScaleUsesEachLinesActualWidth()
    {
        string[] lines = ["x", "longlong", "x"];

        double scale = RadialMenuLabelLayout.GetScaleForCircle(lines, 8, 2, value => value.Length, 0);
        double boundingBoxScale = 8 / Math.Sqrt(8 * 8 + 6 * 6);

        Assert.True(scale > boundingBoxScale);
    }

    [Fact]
    public void CenterLabelLayoutMatchesExhaustiveOptimalScale()
    {
        var random = new Random(7331);
        for (int sample = 0; sample < 250; sample++)
        {
            int wordCount = random.Next(1, 10);
            string[] words = Enumerable.Range(0, wordCount)
                .Select(index => $"w{index}" + new string('x', random.Next(1, 12)))
                .ToArray();
            string text = string.Join(' ', words);
            double diameter = random.Next(12, 42);
            double lineHeight = random.Next(2, 7);
            double padding = random.Next(0, 3);

            string fitted = RadialMenuLabelLayout.FitToCircle(
                text, diameter, lineHeight, value => value.Length, padding);
            double actualScale = RadialMenuLabelLayout.GetScaleForCircle(
                fitted.Split('\n'), diameter, lineHeight, value => value.Length, padding);
            double optimalScale = GetExhaustiveOptimalScale(words, diameter, lineHeight, padding);

            Assert.Equal(optimalScale, actualScale, precision: 10);
            Assert.Equal(words, fitted.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain("\n\n", fitted);
        }
    }

    private static double GetExhaustiveOptimalScale(string[] words, double diameter,
        double lineHeight, double padding)
    {
        double best = 0;
        int partitionCount = 1 << Math.Max(0, words.Length - 1);
        for (int mask = 0; mask < partitionCount; mask++)
        {
            var lines = new List<string>();
            int start = 0;
            for (int boundary = 0; boundary < words.Length - 1; boundary++)
            {
                if ((mask & 1 << boundary) == 0) continue;
                lines.Add(string.Join(' ', words[start..(boundary + 1)]));
                start = boundary + 1;
            }
            lines.Add(string.Join(' ', words[start..]));
            best = Math.Max(best, RadialMenuLabelLayout.GetScaleForCircle(
                lines, diameter, lineHeight, value => value.Length, padding));
        }
        return best;
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
    /// <summary>Rapid pointer changes continue from current hover progress and discard removed entries.</summary>
    [Fact]
    public void HoverAnimationContinuesAndResetsByStableId()
    {
        var first = new RadialMenuLayout(["a", "b"], "center", 0.2, 0.3, 1);
        var next = new RadialMenuLayout(["b", "c"], "center", 0.2, 0.3, 1);
        var animation = new RadialMenuHoverAnimation();
        animation.Advance(first, "a", 0.025f);
        float risingA = animation.VisualProgress("a");
        Assert.InRange(risingA, 0.15f, 0.16f);
        animation.Advance(first, "b", 0.01f);
        Assert.True(animation.VisualProgress("a") < risingA);
        Assert.True(animation.VisualProgress("b") > 0f);
        animation.Retain(next);
        Assert.Equal(0f, animation.VisualProgress("a"));
        animation.Advance(next, "b", 1f);
        Assert.Equal(1f, animation.VisualProgress("b"));
        animation.Reset();
        Assert.Equal(0f, animation.VisualProgress("b"));
    }
    /// <summary>Checks disabled content remains present and cannot commit, while enabled selection reports once.</summary>
    [Fact]
    public void DisabledEntryRemainsVisibleAndSelectionReportsOnce()
    {
        var layout = new RadialMenuLayout(["virtual:light", "generic:other"], "center", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(layout, [new("virtual:light", "Light", false), new("generic:other", "Other", true), new("center", "Restore", true)]);
        int selections = 0;
        menu.Selected += _ =>
        {
            selections++;
            return RadialMenuSelectionResult.Close;
        };
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
        menu.Selected += id =>
        {
            selected = id;
            return RadialMenuSelectionResult.Close;
        };
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

    [Fact]
    public void KeepOpenSelectionAllowsAnotherSelectionBeforeClosing()
    {
        var layout = new RadialMenuLayout(["repeat"], "center", 0.2, 0.3, 1);
        var menu = new RadialMenuInteraction(layout,
            [new("repeat", "Repeat", true), new("center", "Center", true)]);
        int selections = 0;
        menu.Selected += _ => ++selections == 1
            ? RadialMenuSelectionResult.KeepOpen
            : RadialMenuSelectionResult.Close;
        menu.Open();

        menu.MovePointer(0, -60, 0, 0, 100);
        Assert.True(menu.SelectHovered());
        Assert.True(menu.IsOpen);
        Assert.Null(menu.SelectedId);

        menu.MovePointer(0, -60, 0, 0, 100);
        Assert.True(menu.SelectHovered());
        Assert.False(menu.IsOpen);
        Assert.Equal("repeat", menu.SelectedId);
        Assert.Equal(2, selections);
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
        menu.Selected += _ =>
        {
            selections++;
            return RadialMenuSelectionResult.Close;
        };
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










