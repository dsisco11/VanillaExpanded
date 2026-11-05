using VanillaExpanded.Gui;
using VanillaExpanded.Tests;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.Gui;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class GuiLayoutInputsTests
{
    [Fact]
    public void InnerSizeSnapshotTracksOnlyInnerDimensionsAndBoundsIdentity()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1);
        var bounds = CreateBounds();
        var snapshot = GuiBoundsSnapshot.CaptureInnerSize(bounds);
        var replacement = CreateBounds();

        // Act
        bounds.fixedX += 5;
        bounds.fixedY += 7;
        bounds.CalcWorldBounds();
        bool positionChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureInnerSize(bounds));
        bounds.fixedWidth += 1;
        bounds.CalcWorldBounds();
        bool widthChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureInnerSize(bounds));
        bounds.fixedWidth -= 1;
        bounds.fixedHeight += 1;
        bounds.CalcWorldBounds();
        bool heightChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureInnerSize(bounds));
        bool identityChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureInnerSize(replacement));

        // Assert
        Assert.True(positionChanged);
        Assert.False(widthChanged);
        Assert.False(heightChanged);
        Assert.False(identityChanged);
    }

    [Fact]
    public void PositionAndOuterWidthSnapshotIgnoresHeightButTracksPositionAndWidth()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1);
        var bounds = CreateBounds();
        var snapshot = GuiBoundsSnapshot.CapturePositionAndOuterWidth(bounds);
        var replacement = CreateBounds();

        // Act
        bounds.fixedHeight += 1;
        bounds.CalcWorldBounds();
        bool heightChanged = snapshot.Matches(GuiBoundsSnapshot.CapturePositionAndOuterWidth(bounds));
        bounds.fixedX += 1;
        bounds.CalcWorldBounds();
        bool positionChanged = snapshot.Matches(GuiBoundsSnapshot.CapturePositionAndOuterWidth(bounds));
        bounds.fixedX -= 1;
        bounds.fixedWidth += 1;
        bounds.CalcWorldBounds();
        bool widthChanged = snapshot.Matches(GuiBoundsSnapshot.CapturePositionAndOuterWidth(bounds));
        bool identityChanged = snapshot.Matches(GuiBoundsSnapshot.CapturePositionAndOuterWidth(replacement));

        // Assert
        Assert.True(heightChanged);
        Assert.False(positionChanged);
        Assert.False(widthChanged);
        Assert.False(identityChanged);
    }

    [Fact]
    public void OuterRectangleSnapshotTracksPositionAndBothDimensions()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1);
        var bounds = CreateBounds();
        var snapshot = GuiBoundsSnapshot.CaptureOuterRect(bounds);
        var replacement = CreateBounds();

        // Act
        bounds.fixedX += 1;
        bounds.CalcWorldBounds();
        bool xChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureOuterRect(bounds));
        bounds.fixedX -= 1;
        bounds.fixedY += 1;
        bounds.CalcWorldBounds();
        bool yChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureOuterRect(bounds));
        bounds.fixedY -= 1;
        bounds.fixedWidth += 1;
        bounds.CalcWorldBounds();
        bool widthChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureOuterRect(bounds));
        bounds.fixedWidth -= 1;
        bounds.fixedHeight += 1;
        bounds.CalcWorldBounds();
        bool heightChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureOuterRect(bounds));
        bool identityChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureOuterRect(replacement));

        // Assert
        Assert.False(xChanged);
        Assert.False(yChanged);
        Assert.False(widthChanged);
        Assert.False(heightChanged);
        Assert.False(identityChanged);
    }

    [Fact]
    public void FixedSizeSnapshotIgnoresPositionButTracksSizeAndBoundsIdentity()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1);
        var bounds = CreateBounds();
        var snapshot = GuiBoundsSnapshot.CaptureFixedSize(bounds);
        var replacement = CreateBounds();

        // Act
        bounds.fixedX += 5;
        bounds.fixedY += 7;
        bounds.CalcWorldBounds();
        bool positionChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureFixedSize(bounds));
        bounds.fixedWidth += 1;
        bounds.CalcWorldBounds();
        bool widthChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureFixedSize(bounds));
        bounds.fixedWidth -= 1;
        bounds.fixedHeight += 1;
        bounds.CalcWorldBounds();
        bool heightChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureFixedSize(bounds));
        bool identityChanged = snapshot.Matches(GuiBoundsSnapshot.CaptureFixedSize(replacement));

        // Assert
        Assert.True(positionChanged);
        Assert.False(widthChanged);
        Assert.False(heightChanged);
        Assert.False(identityChanged);
    }

    [Fact]
    public void BoundsSnapshotsKeepCapturedScalarsWhenTheSourceBoundsChanges()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1);
        var bounds = CreateBounds();
        var inner = GuiBoundsSnapshot.CaptureInnerSize(bounds);
        var positionAndWidth = GuiBoundsSnapshot.CapturePositionAndOuterWidth(bounds);
        var outerRect = GuiBoundsSnapshot.CaptureOuterRect(bounds);
        var fixedSize = GuiBoundsSnapshot.CaptureFixedSize(bounds);

        // Act
        bounds.fixedX = 40;
        bounds.fixedY = 50;
        bounds.fixedWidth = 160;
        bounds.fixedHeight = 120;
        bounds.CalcWorldBounds();

        // Assert
        Assert.Equal(100, inner.Width);
        Assert.Equal(80, inner.Height);
        Assert.Equal(10, positionAndWidth.X);
        Assert.Equal(20, positionAndWidth.Y);
        Assert.Equal(100, positionAndWidth.Width);
        Assert.Equal(10, outerRect.X);
        Assert.Equal(20, outerRect.Y);
        Assert.Equal(100, outerRect.Width);
        Assert.Equal(80, outerRect.Height);
        Assert.Equal(100, fixedSize.Width);
        Assert.Equal(80, fixedSize.Height);
    }

    [Fact]
    public void BoundsSnapshotFactoriesCaptureScaledExtentsAndAbsolutePosition()
    {
        // Arrange
        const double scale = 1.25;
        using var guiScale = new GuiScaleScope((float)scale);
        var parent = CreateBounds(30, 40, 400, 300);
        var bounds = ElementBounds.Fixed(17, 19, 73, 47)
            .WithFixedPadding(3, 5)
            .WithParent(parent);
        bounds.CalcWorldBounds();

        // Act
        var innerSize = GuiBoundsSnapshot.CaptureInnerSize(bounds);
        var positionAndOuterWidth = GuiBoundsSnapshot.CapturePositionAndOuterWidth(bounds);
        var outerRect = GuiBoundsSnapshot.CaptureOuterRect(bounds);
        var fixedSize = GuiBoundsSnapshot.CaptureFixedSize(bounds);

        // Assert
        Assert.Equal(0, innerSize.X);
        Assert.Equal(0, innerSize.Y);
        Assert.Equal(73 * scale, innerSize.Width);
        Assert.Equal(47 * scale, innerSize.Height);

        Assert.Equal((30 + 17) * scale, positionAndOuterWidth.X);
        Assert.Equal((40 + 19) * scale, positionAndOuterWidth.Y);
        Assert.Equal((73 + 2 * 3) * scale, positionAndOuterWidth.Width);
        Assert.Equal(0, positionAndOuterWidth.Height);

        Assert.Equal((30 + 17) * scale, outerRect.X);
        Assert.Equal((40 + 19) * scale, outerRect.Y);
        Assert.Equal((73 + 2 * 3) * scale, outerRect.Width);
        Assert.Equal((47 + 2 * 5) * scale, outerRect.Height);

        Assert.Equal(0, fixedSize.X);
        Assert.Equal(0, fixedSize.Y);
        Assert.Equal(73, fixedSize.Width);
        Assert.Equal(47, fixedSize.Height);
    }

    [Fact]
    public void DialogPlacementCreatesFreshRootsWithStoredGeometryAndWindowParent()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1);
        var window = CreateBounds(80, 40, 800, 600);
        var placement = new GuiDialogPlacement(window, 12, 24, 300, 200);

        // Act
        var firstRoot = placement.ToBounds();
        firstRoot.fixedX = 500;
        firstRoot.fixedY = 400;
        firstRoot.fixedWidth = 100;
        firstRoot.fixedHeight = 90;
        firstRoot.CalcWorldBounds();
        var secondRoot = placement.ToBounds();
        secondRoot.CalcWorldBounds();

        // Assert
        Assert.NotSame(firstRoot, secondRoot);
        Assert.Same(window, secondRoot.ParentBounds);
        Assert.Equal(12, secondRoot.fixedX);
        Assert.Equal(24, secondRoot.fixedY);
        Assert.Equal(300, secondRoot.fixedWidth);
        Assert.Equal(200, secondRoot.fixedHeight);
        Assert.Equal(92, secondRoot.absX);
        Assert.Equal(64, secondRoot.absY);
    }

    [Fact]
    public void MeasurementInputsCaptureTheLiveScaleAndCurrentAndDefaultLanguage()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1.25f);
        string currentLocale = Lang.CurrentLocale;
        string defaultLocale = Lang.DefaultLocale;
        var currentService = currentLocale == null ? null : Lang.AvailableLanguages.GetValueOrDefault(currentLocale);
        var defaultService = defaultLocale == null ? null : Lang.AvailableLanguages.GetValueOrDefault(defaultLocale);

        // Act
        var inputs = GuiMeasurementInputs.Capture();
        var repeated = GuiMeasurementInputs.Capture();

        // Assert
        Assert.Equal(1.25, inputs.Scale);
        Assert.Equal(currentLocale, inputs.CurrentLanguage.Locale);
        Assert.Same(currentService, inputs.CurrentLanguage.Service);
        Assert.Equal(defaultLocale, inputs.DefaultLanguage.Locale);
        Assert.Same(defaultService, inputs.DefaultLanguage.Service);
        Assert.True(inputs.Matches(repeated));
    }

    private static ElementBounds CreateBounds(double x = 10, double y = 20, double width = 100, double height = 80)
    {
        var bounds = ElementBounds.Fixed(x, y, width, height).WithEmptyParent();
        bounds.CalcWorldBounds();
        return bounds;
    }

    private sealed class GuiScaleScope : IDisposable
    {
        private readonly float previous = RuntimeEnv.GUIScale;
        internal GuiScaleScope(float scale) => RuntimeEnv.GUIScale = scale;
        public void Dispose() => RuntimeEnv.GUIScale = previous;
    }
}
