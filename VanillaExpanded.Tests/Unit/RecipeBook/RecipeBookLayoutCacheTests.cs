using System.Collections.Generic;
using Moq;
using VanillaExpanded.Gui;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.Common;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookInteractionTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookLayoutCacheTests
{
    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    public void EqualFixedInputsReuseTheDockingResultAndMaterializeIndependentBounds(float scale)
    {
        // Arrange
        using var guiScale = new GuiScaleScope(scale);
        var geometry = new LayoutGeometry(scale);
        var uncached = RecipeBookLayout.Place(geometry.Parent, geometry.Grid, geometry.Output,
            geometry.Window, 200, 164)!;
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();

        // Act
        var placement = EnsurePlacement(cache, geometry, movable: false)!.Value;
        var repeated = EnsurePlacement(cache, geometry, movable: false)!.Value;

        // Assert
        Assert.Equal(1, calls.FixedCalls);
        Assert.Equal(0, calls.MovableCalls);
        Assert.Equal(uncached.fixedX, placement.X);
        Assert.Equal(uncached.fixedY, placement.Y);
        Assert.Equal(uncached.fixedWidth, placement.Width);
        Assert.Equal(uncached.fixedHeight, placement.Height);
        Assert.Equal(placement, repeated);

        var firstRoot = placement.ToBounds();
        var secondRoot = repeated.ToBounds();
        Assert.NotSame(firstRoot, secondRoot);
        Assert.Same(geometry.Window, firstRoot.ParentBounds);
        firstRoot.CalcWorldBounds();
        secondRoot.CalcWorldBounds();
        Assert.Equal(2, firstRoot.absX - geometry.Parent.absX - geometry.Parent.OuterWidth, 5);
        Assert.Equal(placement.X, secondRoot.fixedX);
        Assert.Equal(placement.Y, secondRoot.fixedY);
    }

    [Fact]
    public void ChangedMeasurementsInvalidatePlacementEvenWhenMinimumDimensionsStayEqual()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        int measurementCalls = 0;
        var cache = new RecipeBookLayoutCache(() => measurementCalls++);
        var initial = RecipeBookMeasurementInputs.Capture(45);
        using var calls = new RecipeBookLayoutCallCounter();

        // Act
        Assert.True(cache.EnsureMeasurements(initial));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        Assert.False(cache.EnsureMeasurements(initial));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        var changed = initial with { TileSize = initial.TileSize + 1 };
        Assert.True(cache.EnsureMeasurements(changed));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));

        // Assert
        Assert.Equal(2, measurementCalls);
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(0, calls.MovableCalls);
        Assert.False(cache.EnsureMeasurements(changed));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        Assert.Equal(2, calls.FixedCalls);
    }

    [Fact]
    public void MeasurementSnapshotTracksScaleTileSizeAndCurrentAndDefaultLanguageIdentities()
    {
        // Arrange
        using var guiScale = new GuiScaleScope(1.25f);
        const string currentOne = "recipe-cache-current-one";
        const string currentTwo = "recipe-cache-current-two";
        const string defaultOne = "recipe-cache-default-one";
        const string defaultTwo = "recipe-cache-default-two";
        string originalLocale = Lang.CurrentLocale;
        string originalDefault = Lang.DefaultLocale;
        var oldServices = new Dictionary<string, ITranslationService?>
        {
            [currentOne] = Lang.AvailableLanguages.GetValueOrDefault(currentOne),
            [currentTwo] = Lang.AvailableLanguages.GetValueOrDefault(currentTwo),
            [defaultOne] = Lang.AvailableLanguages.GetValueOrDefault(defaultOne),
            [defaultTwo] = Lang.AvailableLanguages.GetValueOrDefault(defaultTwo)
        };
        IDictionary<string, string> currentEntries = new Dictionary<string, string>();
        IDictionary<string, string> defaultEntries = new Dictionary<string, string>();
        var currentLineBreak = EnumLinebreakBehavior.AfterWord;
        var defaultLineBreak = EnumLinebreakBehavior.AfterWord;
        int tileSize = 45;
        int measurementCalls = 0;
        var cache = new RecipeBookLayoutCache(() => measurementCalls++);
        RecipeBookMeasurementInputs Capture() => RecipeBookMeasurementInputs.Capture(tileSize);

        try
        {
            Lang.AvailableLanguages[currentOne] = TranslationService(() => currentEntries, () => currentLineBreak);
            Lang.AvailableLanguages[currentTwo] = TranslationService(() => currentEntries, () => currentLineBreak);
            Lang.AvailableLanguages[defaultOne] = TranslationService(() => defaultEntries, () => defaultLineBreak);
            Lang.AvailableLanguages[defaultTwo] = TranslationService(() => defaultEntries, () => defaultLineBreak);
            Lang.ChangeLanguage(currentOne);
            Lang.DefaultLocale = defaultOne;

            // Act
            Assert.True(cache.EnsureMeasurements(Capture()));

            // Assert
            Assert.Equal(1, measurementCalls);
            Assert.False(cache.EnsureMeasurements(Capture()));

            void AssertChangedInput(Action change)
            {
                int previousMeasurements = measurementCalls;
                change();
                Assert.True(cache.EnsureMeasurements(Capture()));
                Assert.Equal(previousMeasurements + 1, measurementCalls);
                Assert.False(cache.EnsureMeasurements(Capture()));
            }

            AssertChangedInput(() => RuntimeEnv.GUIScale = 1.5f);
            AssertChangedInput(() => tileSize++);
            AssertChangedInput(() => Lang.ChangeLanguage(currentTwo));

            var replacementCurrentService = TranslationService(() => currentEntries, () => currentLineBreak);
            AssertChangedInput(() => Lang.AvailableLanguages[currentTwo] = replacementCurrentService);

            AssertChangedInput(() => currentEntries = new Dictionary<string, string>());
            AssertChangedInput(() => currentLineBreak = EnumLinebreakBehavior.AfterCharacter);
            AssertChangedInput(() => Lang.DefaultLocale = defaultTwo);

            var replacementDefaultService = TranslationService(() => defaultEntries, () => defaultLineBreak);
            AssertChangedInput(() => Lang.AvailableLanguages[defaultTwo] = replacementDefaultService);

            AssertChangedInput(() => defaultEntries = new Dictionary<string, string>());
            AssertChangedInput(() => defaultLineBreak = EnumLinebreakBehavior.AfterCharacter);
            Assert.Equal(11, measurementCalls);
        }
        finally
        {
            foreach (var (locale, service) in oldServices)
            {
                if (service == null) Lang.AvailableLanguages.Remove(locale);
                else Lang.AvailableLanguages[locale] = service;
            }
            Lang.DefaultLocale = originalDefault;
            Lang.ChangeLanguage(originalLocale);
        }
    }

    [Fact]
    public void FixedPlacementInvalidatesForEveryFixedGeometryAndSizingInput()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        double minimumWidth = 200;
        double minimumHeight = 164;
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();

        GuiDialogPlacement? Place() => cache.EnsurePlacement(false, geometry.Dialog, geometry.Parent,
            geometry.Grid, geometry.Output, geometry.Window, minimumWidth, minimumHeight);

        Assert.NotNull(Place());
        Assert.Equal(1, calls.FixedCalls);

        // Parent height and dialog movement/size are not read by the Fixed docking algorithm.
        geometry.Parent.fixedHeight += 1;
        geometry.Parent.CalcWorldBounds();
        geometry.Dialog.fixedX += 25;
        geometry.Dialog.fixedY += 30;
        geometry.Dialog.fixedWidth += 10;
        geometry.Dialog.fixedHeight += 10;
        geometry.Dialog.CalcWorldBounds();
        geometry.Dialog = geometry.Copy(geometry.Dialog, geometry.Window);
        Assert.NotNull(Place());
        Assert.Equal(1, calls.FixedCalls);

        var changes = new (string Name, Action Change)[]
        {
            ("window bounds identity", () => geometry.Window = geometry.CopyWindow()),
            ("window inner width", () => geometry.Resize(geometry.Window, width: 1)),
            ("window inner height", () => geometry.Resize(geometry.Window, height: 1)),
            ("parent bounds identity", () => geometry.Parent = geometry.Copy(geometry.Parent, geometry.Window)),
            ("parent x", () => geometry.Move(geometry.Parent, x: 1)),
            ("parent y", () => geometry.Move(geometry.Parent, y: 1)),
            ("parent outer width", () => geometry.Resize(geometry.Parent, width: 1)),
            ("grid bounds identity", () => geometry.Grid = geometry.Copy(geometry.Grid, geometry.Parent)),
            ("grid x", () => geometry.Move(geometry.Grid, x: 1)),
            ("grid y", () => geometry.Move(geometry.Grid, y: 1)),
            ("grid outer width", () => geometry.Resize(geometry.Grid, width: 1)),
            ("grid outer height", () => geometry.Resize(geometry.Grid, height: 1)),
            ("output bounds identity", () => geometry.Output = geometry.Copy(geometry.Output, geometry.Parent)),
            ("output x", () => geometry.Move(geometry.Output, x: 1)),
            ("output y", () => geometry.Move(geometry.Output, y: 1)),
            ("output outer width", () => geometry.Resize(geometry.Output, width: 1)),
            ("output outer height", () => geometry.Resize(geometry.Output, height: 1)),
            ("GUI scale", () => RuntimeEnv.GUIScale = 1.25f),
            ("minimum width", () => minimumWidth++),
            ("minimum height", () => minimumHeight++)
        };

        // Act
        foreach (var (name, change) in changes)
        {
            change();
            int previousCalls = calls.FixedCalls;

            // Assert
            Assert.NotNull(Place());
            Assert.True(calls.FixedCalls == previousCalls + 1, $"{name} should trigger one fixed search.");
            Assert.NotNull(Place());
            Assert.True(calls.FixedCalls == previousCalls + 1,
                $"{name} should be stable after its placement snapshot is refreshed.");
        }
        Assert.Equal(0, calls.MovableCalls);
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(1.75f)]
    public void MovablePlacementReturnsLiveClampedOriginWithoutSearchingAfterMovementOrFixedGeometryChanges(float scale)
    {
        // Arrange
        using var guiScale = new GuiScaleScope(scale);
        var geometry = new LayoutGeometry(scale, 800, 600);
        geometry.Window.fixedWidth = 800;
        geometry.Window.fixedHeight = 600;
        geometry.Window.CalcWorldBounds();
        geometry.Dialog.fixedX = -20;
        geometry.Dialog.fixedY = 550;
        geometry.Dialog.CalcWorldBounds();
        var uncached = RecipeBookLayout.PlaceMovable(geometry.Dialog, geometry.Window, 300, 250)!;
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();

        // Act
        var initial = EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250)!.Value;
        geometry.Parent = geometry.Copy(geometry.Parent, geometry.Window);
        geometry.Parent.fixedX += 17;
        geometry.Parent.CalcWorldBounds();
        geometry.Grid = geometry.Copy(geometry.Grid, geometry.Parent);
        geometry.Grid.fixedX += 10;
        geometry.Grid.CalcWorldBounds();
        geometry.Output = geometry.Copy(geometry.Output, geometry.Parent);
        geometry.Output.fixedY += 5;
        geometry.Output.CalcWorldBounds();
        geometry.Dialog.fixedX = 600;
        geometry.Dialog.fixedY = -50;
        geometry.Dialog.CalcWorldBounds();
        var clamped = EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250)!.Value;

        // Assert
        Assert.Equal(1, calls.MovableCalls);
        Assert.Equal(0, calls.FixedCalls);
        Assert.Equal(uncached.fixedX, initial.X);
        Assert.Equal(uncached.fixedY, initial.Y);
        Assert.Equal(uncached.fixedWidth, initial.Width);
        Assert.Equal(uncached.fixedHeight, initial.Height);
        Assert.Equal(0, initial.X);
        Assert.Equal(180, initial.Y);
        Assert.Equal(480, clamped.X);
        Assert.Equal(0, clamped.Y);
        Assert.Equal(320, clamped.Width);
        Assert.Equal(420, clamped.Height);

        var translated = clamped.ToBounds();
        translated.CalcWorldBounds();
        Assert.Equal(480 * scale, translated.absX, 5);
        Assert.Equal(0, translated.absY, 5);

        geometry.Dialog.fixedX = 23;
        geometry.Dialog.fixedY = 37;
        var liveOrigin = EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250)!.Value;
        Assert.Equal(23, liveOrigin.X);
        Assert.Equal(37, liveOrigin.Y);
        Assert.Equal(1, calls.MovableCalls);
    }

    [Fact]
    public void MovablePlacementInvalidatesForItsWindowDialogAndSizingInputs()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry(1, 800, 600);
        double minimumWidth = 300;
        double minimumHeight = 250;
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();

        GuiDialogPlacement? Place() => cache.EnsurePlacement(true, geometry.Dialog, geometry.Parent,
            geometry.Grid, geometry.Output, geometry.Window, minimumWidth, minimumHeight);

        Assert.NotNull(Place());
        Assert.Equal(1, calls.MovableCalls);
        var changes = new (string Name, Action Change)[]
        {
            ("window identity", () => geometry.Window = geometry.CopyWindow()),
            ("window inner width", () => geometry.Resize(geometry.Window, width: 1)),
            ("window inner height", () => geometry.Resize(geometry.Window, height: 1)),
            ("dialog bounds identity", () => geometry.Dialog = geometry.Copy(geometry.Dialog, geometry.Window)),
            ("dialog width", () => geometry.Resize(geometry.Dialog, width: 1)),
            ("dialog height", () => geometry.Resize(geometry.Dialog, height: 1)),
            ("GUI scale", () => RuntimeEnv.GUIScale = 1.25f),
            ("minimum width", () => minimumWidth++),
            ("minimum height", () => minimumHeight++)
        };

        // Act
        foreach (var (name, change) in changes)
        {
            change();
            int previousCalls = calls.MovableCalls;

            // Assert
            Assert.NotNull(Place());
            Assert.True(calls.MovableCalls == previousCalls + 1, $"{name} should trigger one movable search.");
            Assert.NotNull(Place());
            Assert.True(calls.MovableCalls == previousCalls + 1,
                $"{name} should be stable after its placement snapshot is refreshed.");
        }
        Assert.Equal(0, calls.FixedCalls);
    }

    [Fact]
    public void MovableModeTransitionsSelectAndCacheTheMatchingPlacementAlgorithm()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();

        // Act
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: true));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: true));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));

        // Assert
        Assert.Equal(2, calls.FixedCalls);
        Assert.Equal(1, calls.MovableCalls);
    }

    [Fact]
    public void ComposedBoundsCommitEffectiveFixedAndMovableGeometryWithoutAnotherSearch()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();

        // Act: composition may clamp dimensions or replace the root after the search.
        var fixedResult = EnsurePlacement(cache, geometry, movable: false)!.Value;
        var composedFixed = ElementBounds.Fixed(fixedResult.X + 3, fixedResult.Y + 4,
            fixedResult.Width - 10, fixedResult.Height - 8).WithParent(geometry.Window);
        cache.AcceptComposedBounds(composedFixed);
        var acceptedFixed = EnsurePlacement(cache, geometry, movable: false)!.Value;

        var movableResult = EnsurePlacement(cache, geometry, movable: true)!.Value;
        var composedMovable = ElementBounds.Fixed(movableResult.X, movableResult.Y,
            movableResult.Width - 10, movableResult.Height - 8).WithParent(geometry.Window);
        cache.AcceptComposedBounds(composedMovable);
        var acceptedMovable = cache.EnsurePlacement(true, composedMovable, geometry.Parent, geometry.Grid,
            geometry.Output, geometry.Window, 200, 164)!.Value;

        // Assert
        Assert.Equal(composedFixed.fixedX, acceptedFixed.X);
        Assert.Equal(composedFixed.fixedY, acceptedFixed.Y);
        Assert.Equal(composedFixed.fixedWidth, acceptedFixed.Width);
        Assert.Equal(composedFixed.fixedHeight, acceptedFixed.Height);
        Assert.Equal(composedMovable.fixedX, acceptedMovable.X);
        Assert.Equal(composedMovable.fixedY, acceptedMovable.Y);
        Assert.Equal(composedMovable.fixedWidth, acceptedMovable.Width);
        Assert.Equal(composedMovable.fixedHeight, acceptedMovable.Height);
        Assert.Equal(1, calls.FixedCalls);
        Assert.Equal(1, calls.MovableCalls);
    }

    [Fact]
    public void ImpossiblePlacementIsRetriedAndCannotReuseAnEarlierSuccess()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();
        double originalWidth = geometry.Window.fixedWidth;
        double originalHeight = geometry.Window.fixedHeight;
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));

        // Act
        geometry.Window.fixedWidth = 100;
        geometry.Window.fixedHeight = 100;
        geometry.Window.CalcWorldBounds();
        var failed = EnsurePlacement(cache, geometry, movable: false);
        var retriedFailure = EnsurePlacement(cache, geometry, movable: false);
        Assert.Throws<InvalidOperationException>(() => cache.AcceptComposedBounds(
            ElementBounds.Fixed(0, 0, 320, 420).WithParent(geometry.Window)));
        geometry.Window.fixedWidth = originalWidth;
        geometry.Window.fixedHeight = originalHeight;
        geometry.Window.CalcWorldBounds();
        var recovered = EnsurePlacement(cache, geometry, movable: false);

        // Assert
        Assert.Null(failed);
        Assert.Null(retriedFailure);
        Assert.NotNull(recovered);
        Assert.Equal(4, calls.FixedCalls);
    }

    [Fact]
    public void MovablePlacementFailureIsRetriedAndCannotReuseAnEarlierSuccess()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry(1, 800, 600);
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();
        double originalHeight = geometry.Window.fixedHeight;
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250));

        // Act
        geometry.Window.fixedHeight = 200;
        geometry.Window.CalcWorldBounds();
        var failed = EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250);
        var retriedFailure = EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250);
        Assert.Throws<InvalidOperationException>(() => cache.AcceptComposedBounds(
            ElementBounds.Fixed(0, 0, 320, 420).WithParent(geometry.Window)));
        geometry.Window.fixedHeight = originalHeight;
        geometry.Window.CalcWorldBounds();
        var recovered = EnsurePlacement(cache, geometry, movable: true, minimumWidth: 300, minimumHeight: 250);

        // Assert
        Assert.Null(failed);
        Assert.Null(retriedFailure);
        Assert.NotNull(recovered);
        Assert.Equal(4, calls.MovableCalls);
    }

    [Fact]
    public void PlacementExceptionClearsThePriorSuccessAndTheNextCallRetries()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        var cache = new RecipeBookLayoutCache(() => { });
        using var calls = new RecipeBookLayoutCallCounter();
        var priorPlacement = EnsurePlacement(cache, geometry, movable: false)!.Value;
        double originalGridX = geometry.Grid.fixedX;
        geometry.Grid.fixedX += 1;
        geometry.Grid.CalcWorldBounds();
        calls.ThrowNextFixed = true;

        // Act
        var exception = Assert.Throws<RecipeBookLayoutCallCounter.InjectedPlacementException>(
            () => EnsurePlacement(cache, geometry, movable: false));
        var stalePlacementException = Assert.Throws<InvalidOperationException>(
            () => cache.AcceptComposedBounds(priorPlacement.ToBounds()));
        geometry.Grid.fixedX = originalGridX;
        geometry.Grid.CalcWorldBounds();
        var restoredGeometry = EnsurePlacement(cache, geometry, movable: false);
        geometry.Grid.fixedX += 1;
        geometry.Grid.CalcWorldBounds();
        var retriedChangedGeometry = EnsurePlacement(cache, geometry, movable: false);

        // Assert
        Assert.Equal("Injected fixed-placement failure.", exception.Message);
        Assert.Equal("Recipe book composition requires a successful placement.", stalePlacementException.Message);
        Assert.NotNull(restoredGeometry);
        Assert.NotNull(retriedChangedGeometry);
        Assert.Equal(4, calls.FixedCalls);
    }

    [Fact]
    public void MeasurementExceptionClearsEarlierMeasurementAndPlacementForRetry()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        var initial = RecipeBookMeasurementInputs.Capture(45);
        var changed = initial with { TileSize = 46 };
        int measurementCalls = 0;
        bool failMeasurement = false;
        var cache = new RecipeBookLayoutCache(() =>
        {
            measurementCalls++;
            if (failMeasurement) throw new InvalidOperationException("Injected measurement failure.");
        });
        using var calls = new RecipeBookLayoutCallCounter();
        Assert.True(cache.EnsureMeasurements(initial));
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        failMeasurement = true;

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => cache.EnsureMeasurements(changed));
        failMeasurement = false;
        bool originalRetried = cache.EnsureMeasurements(initial);
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));
        bool changedRetried = cache.EnsureMeasurements(changed);
        Assert.NotNull(EnsurePlacement(cache, geometry, movable: false));

        // Assert
        Assert.Equal("Injected measurement failure.", exception.Message);
        Assert.True(originalRetried);
        Assert.True(changedRetried);
        Assert.Equal(4, measurementCalls);
        Assert.Equal(3, calls.FixedCalls);
    }

    [Fact]
    public void SeparateCacheInstancesDoNotShareMeasurementOrPlacementState()
    {
        // Arrange
        using var guiScale = new GuiScaleScope();
        var geometry = new LayoutGeometry();
        int firstMeasurements = 0;
        int secondMeasurements = 0;
        var firstCache = new RecipeBookLayoutCache(() => firstMeasurements++);
        var secondCache = new RecipeBookLayoutCache(() => secondMeasurements++);
        var inputs = RecipeBookMeasurementInputs.Capture(45);
        using var calls = new RecipeBookLayoutCallCounter();

        // Act
        Assert.True(firstCache.EnsureMeasurements(inputs));
        Assert.True(secondCache.EnsureMeasurements(inputs));
        Assert.NotNull(EnsurePlacement(firstCache, geometry, movable: false));
        Assert.NotNull(EnsurePlacement(secondCache, geometry, movable: false));
        Assert.False(firstCache.EnsureMeasurements(inputs));
        Assert.False(secondCache.EnsureMeasurements(inputs));
        Assert.NotNull(EnsurePlacement(firstCache, geometry, movable: false));
        Assert.NotNull(EnsurePlacement(secondCache, geometry, movable: false));
        Assert.True(firstCache.EnsureMeasurements(inputs with { TileSize = 46 }));
        Assert.NotNull(EnsurePlacement(firstCache, geometry, movable: false));
        Assert.NotNull(EnsurePlacement(secondCache, geometry, movable: false));

        // Assert
        Assert.Equal(2, firstMeasurements);
        Assert.Equal(1, secondMeasurements);
        Assert.Equal(3, calls.FixedCalls);
    }

    private static ITranslationService TranslationService(Func<IDictionary<string, string>> entries,
        Func<EnumLinebreakBehavior> lineBreak)
    {
        var service = new Mock<ITranslationService>();
        service.SetupGet(value => value.LineBreakBehavior).Returns(() => lineBreak());
        service.Setup(value => value.GetAllEntries()).Returns(() => entries());
        return service.Object;
    }

    private static GuiDialogPlacement? EnsurePlacement(RecipeBookLayoutCache cache, LayoutGeometry geometry,
        bool movable, double minimumWidth = 200, double minimumHeight = 164) =>
        cache.EnsurePlacement(movable, geometry.Dialog, geometry.Parent, geometry.Grid, geometry.Output,
            geometry.Window, minimumWidth, minimumHeight);

    private sealed class LayoutGeometry
    {
        internal ElementBounds Window { get; set; }
        internal ElementBounds Parent { get; set; }
        internal ElementBounds Grid { get; set; }
        internal ElementBounds Output { get; set; }
        internal ElementBounds Dialog { get; set; }

        internal LayoutGeometry(float scale = 1, double viewportWidth = 1280, double viewportHeight = 720)
        {
            Window = Calculated(ElementBounds.Fixed(0, 0, viewportWidth / scale, viewportHeight / scale)
                .WithEmptyParent());
            Parent = Calculated(ElementBounds.Fixed(160, 80, 360, 400).WithParent(Window));
            Grid = Calculated(ElementBounds.Fixed(200, 100, 120, 220).WithParent(Parent));
            Output = Calculated(ElementBounds.Fixed(200, 320, 40, 40).WithParent(Parent));
            Dialog = Calculated(ElementBounds.Fixed(50, 60, 320, 420).WithParent(Window));
        }

        internal ElementBounds CopyWindow() => Calculated(ElementBounds.Fixed(0, 0,
            Window.fixedWidth, Window.fixedHeight).WithEmptyParent());

        internal ElementBounds Copy(ElementBounds bounds, ElementBounds parent) => Calculated(
            ElementBounds.Fixed(bounds.fixedX, bounds.fixedY, bounds.fixedWidth, bounds.fixedHeight)
                .WithParent(parent));

        internal void Move(ElementBounds bounds, double x = 0, double y = 0)
        {
            bounds.fixedX += x;
            bounds.fixedY += y;
            bounds.CalcWorldBounds();
        }

        internal void Resize(ElementBounds bounds, double width = 0, double height = 0)
        {
            bounds.fixedWidth += width;
            bounds.fixedHeight += height;
            bounds.CalcWorldBounds();
        }
    }
}
