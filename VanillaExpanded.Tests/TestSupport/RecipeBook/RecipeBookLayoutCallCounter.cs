using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using VanillaExpanded.RecipeBook;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

/// <summary>Fixture-scoped observation of the mod's real layout algorithms.</summary>
internal sealed class RecipeBookLayoutCallCounter : IDisposable
{
    private static int fixedCalls;
    private static int movableCalls;
    private static int measurementCalls;
    private static int compositionCalls;
    private static int refreshCalls;
    private static int failNextFixed;
    private static bool countDialogBounds;
    private static Func<IRecipeBookIconShaderProgram>? createIconShader;
    private static readonly List<string> operations = new();
    private static readonly object operationsLock = new();
    private readonly Harmony harmony = new("VanillaExpanded.Tests.RecipeBookLayout." + Guid.NewGuid().ToString("N"));

    internal int FixedCalls => Volatile.Read(ref fixedCalls);
    internal int MovableCalls => Volatile.Read(ref movableCalls);
    internal int MeasurementCalls => Volatile.Read(ref measurementCalls);
    internal int CompositionCalls => Volatile.Read(ref compositionCalls);
    internal int RefreshCalls => Volatile.Read(ref refreshCalls);
    /// <summary>Returns the observed computation order for the active serialized test scope.</summary>
    internal string[] Operations
    {
        get { lock (operationsLock) return operations.ToArray(); }
    }

    internal bool ThrowNextFixed
    {
        set => Volatile.Write(ref failNextFixed, value ? 1 : 0);
    }

    /// <summary>Intercepts layout computations, optionally replacing the composed root and one real child bound with counters.</summary>
    internal RecipeBookLayoutCallCounter(bool countBounds = false,
        Func<IRecipeBookIconShaderProgram>? createIconShader = null)
    {
        Interlocked.Exchange(ref fixedCalls, 0);
        Interlocked.Exchange(ref movableCalls, 0);
        Interlocked.Exchange(ref measurementCalls, 0);
        Interlocked.Exchange(ref compositionCalls, 0);
        Interlocked.Exchange(ref refreshCalls, 0);
        Interlocked.Exchange(ref failNextFixed, 0);
        countDialogBounds = countBounds;
        RecipeBookLayoutCallCounter.createIconShader = createIconShader;
        lock (operationsLock) operations.Clear();
        harmony.Patch(LayoutMethod(nameof(RecipeBookLayout.Place),
                typeof(ElementBounds), typeof(ElementBounds), typeof(ElementBounds), typeof(ElementBounds),
                typeof(double), typeof(double)),
            prefix: Prefix(nameof(CountFixed)));
        harmony.Patch(LayoutMethod(nameof(RecipeBookLayout.PlaceMovable),
                typeof(ElementBounds), typeof(ElementBounds), typeof(double), typeof(double)),
            prefix: Prefix(nameof(CountMovable)));
        harmony.Patch(DialogMethod("MeasureLayout"), prefix: Prefix(nameof(CountMeasurement)));
        harmony.Patch(DialogMethod("Compose"), prefix: Prefix(nameof(CountComposition)),
            postfix: Prefix(nameof(CountComposedChild)));
        harmony.Patch(DialogMethod("Refresh"), prefix: Prefix(nameof(CountRefresh)));
    }

    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        Interlocked.Exchange(ref failNextFixed, 0);
        countDialogBounds = false;
        createIconShader = null;
    }

    /// <summary>Returns the active dialog's composed root when bounds counting was enabled.</summary>
    internal CountingBounds GetRootBounds(GuiDialogRecipeBook book) =>
        Assert.IsType<CountingBounds>(book.SingleComposer.Bounds);

    /// <summary>Returns the real recipe-frame child bound replaced by the fixture-scoped counter.</summary>
    internal CountingBounds GetChildBounds(GuiDialogRecipeBook book) =>
        Assert.IsType<CountingBounds>(book.SingleComposer.GetElement("recipes-background").Bounds);

    private static MethodInfo LayoutMethod(string name, params Type[] parameterTypes) =>
        typeof(RecipeBookLayout).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: parameterTypes, modifiers: null)!;

    /// <summary>Finds one nonpublic Recipe Book lifecycle method for fixture-scoped interception.</summary>
    private static MethodInfo DialogMethod(string name) =>
        typeof(GuiDialogRecipeBook).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>Builds a Harmony patch descriptor for one counter callback.</summary>
    private static HarmonyMethod Prefix(string name) =>
        new(typeof(RecipeBookLayoutCallCounter).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!);

    /// <summary>Counts Fixed placement and throws once when a placement-failure case requests injection.</summary>
    private static void CountFixed()
    {
        Interlocked.Increment(ref fixedCalls);
        Record("fixed");
        if (Interlocked.Exchange(ref failNextFixed, 0) != 0)
            throw new InjectedPlacementException();
    }

    /// <summary>Counts calls to the native-policy Movable placement computation.</summary>
    private static void CountMovable()
    {
        Interlocked.Increment(ref movableCalls);
        Record("movable");
    }

    /// <summary>Counts actual execution of the Recipe Book measurement callback.</summary>
    private static void CountMeasurement()
    {
        Interlocked.Increment(ref measurementCalls);
        Record("measure");
    }

    /// <summary>Counts composition and optionally supplies a real test-only counted root bound.</summary>
    private static void CountComposition(ref ElementBounds __0)
    {
        Interlocked.Increment(ref compositionCalls);
        Record("compose");
        if (countDialogBounds) __0 = new CountingBounds(__0);
    }

    /// <summary>Counts recipe refreshes to verify initial layout precedes opening-time content refresh.</summary>
    private static void CountRefresh()
    {
        Interlocked.Increment(ref refreshCalls);
        Record("refresh");
    }

    /// <summary>Records one intercepted operation in the serialized fixture's sequence.</summary>
    private static void Record(string operation)
    {
        lock (operationsLock) operations.Add(operation);
    }

    /// <summary>Replaces one actual composed descendant with a counter while preserving its native parent tree.</summary>
    private static void CountComposedChild(GuiDialogRecipeBook __instance)
    {
        if (createIconShader != null
            && __instance.SingleComposer.GetElement("tiles") is GuiElementRecipeBookTiles tiles)
        {
            var renderer = typeof(GuiElementRecipeBookTiles).GetField("icons",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tiles)!;
            var shader = typeof(RecipeBookIconRenderer).GetField("shader",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            if (shader.GetValue(renderer) == null) shader.SetValue(renderer, createIconShader());
        }

        if (!countDialogBounds || __instance.SingleComposer.Bounds is not CountingBounds) return;

        var element = __instance.SingleComposer.GetElement("recipes-background");
        if (element.Bounds is CountingBounds) return;

        var original = element.Bounds;
        var parent = original.ParentBounds
            ?? throw new InvalidOperationException("The counted dialog child must retain its parent bounds.");
        int childIndex = parent.ChildBounds.IndexOf(original);
        if (childIndex < 0)
            throw new InvalidOperationException("The counted dialog child must remain in its native parent's child list.");

        var counted = new CountingBounds(original);
        parent.ChildBounds[childIndex] = counted;
        element.Bounds = counted;
    }

    internal sealed class InjectedPlacementException() : Exception("Injected fixed-placement failure.");

    /// <summary>Counts native root or child world-bound recalculation while retaining the original bounds inputs.</summary>
    internal sealed class CountingBounds : ElementBounds
    {
        /// <summary>Gets the number of native world-bound calculations performed on this bound.</summary>
        internal int Calculations { get; private set; }

        /// <summary>Copies the geometry and parent relationship of an existing composer bound for test-only counting.</summary>
        internal CountingBounds(ElementBounds source)
        {
            ParentBounds = source.ParentBounds;
            LeftOfBounds = source.LeftOfBounds;
            ChildBounds = new(source.ChildBounds);
            Code = source.Code;
            Name = source.Name;
            Alignment = source.Alignment;
            horizontalSizing = source.horizontalSizing;
            verticalSizing = source.verticalSizing;
            percentPaddingX = source.percentPaddingX;
            percentPaddingY = source.percentPaddingY;
            percentX = source.percentX;
            percentY = source.percentY;
            percentWidth = source.percentWidth;
            percentHeight = source.percentHeight;
            fixedMarginX = source.fixedMarginX;
            fixedMarginY = source.fixedMarginY;
            fixedPaddingX = source.fixedPaddingX;
            fixedPaddingY = source.fixedPaddingY;
            fixedX = source.fixedX;
            fixedY = source.fixedY;
            fixedWidth = source.fixedWidth;
            fixedHeight = source.fixedHeight;
            fixedOffsetX = source.fixedOffsetX;
            fixedOffsetY = source.fixedOffsetY;
            absPaddingX = source.absPaddingX;
            absPaddingY = source.absPaddingY;
            absMarginX = source.absMarginX;
            absMarginY = source.absMarginY;
            absOffsetX = source.absOffsetX;
            absOffsetY = source.absOffsetY;
            absFixedX = source.absFixedX;
            absFixedY = source.absFixedY;
            absInnerWidth = source.absInnerWidth;
            absInnerHeight = source.absInnerHeight;
            IsDrawingSurface = source.IsDrawingSurface;
            AllowNoChildren = source.AllowNoChildren;
            Initialized = source.Initialized;
            requiresrelculation = source.RequiresRecalculation;
        }

        /// <summary>Counts this actual composer bound before delegating its calculation to the native implementation.</summary>
        public override void CalcWorldBounds()
        {
            Calculations++;
            base.CalcWorldBounds();
        }
    }
}
