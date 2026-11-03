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
    private static int failNextFixed;
    private readonly Harmony harmony = new("VanillaExpanded.Tests.RecipeBookLayout." + Guid.NewGuid().ToString("N"));

    internal int FixedCalls => Volatile.Read(ref fixedCalls);
    internal int MovableCalls => Volatile.Read(ref movableCalls);

    internal bool ThrowNextFixed
    {
        set => Volatile.Write(ref failNextFixed, value ? 1 : 0);
    }

    internal RecipeBookLayoutCallCounter()
    {
        Interlocked.Exchange(ref fixedCalls, 0);
        Interlocked.Exchange(ref movableCalls, 0);
        Interlocked.Exchange(ref failNextFixed, 0);
        harmony.Patch(LayoutMethod(nameof(RecipeBookLayout.Place),
                typeof(ElementBounds), typeof(ElementBounds), typeof(ElementBounds), typeof(ElementBounds),
                typeof(double), typeof(double)),
            prefix: Prefix(nameof(CountFixed)));
        harmony.Patch(LayoutMethod(nameof(RecipeBookLayout.PlaceMovable),
                typeof(ElementBounds), typeof(ElementBounds), typeof(double), typeof(double)),
            prefix: Prefix(nameof(CountMovable)));
    }

    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        Interlocked.Exchange(ref failNextFixed, 0);
    }

    private static MethodInfo LayoutMethod(string name, params Type[] parameterTypes) =>
        typeof(RecipeBookLayout).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic,
            binder: null, types: parameterTypes, modifiers: null)!;

    private static HarmonyMethod Prefix(string name) =>
        new(typeof(RecipeBookLayoutCallCounter).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!);

    private static void CountFixed()
    {
        Interlocked.Increment(ref fixedCalls);
        if (Interlocked.Exchange(ref failNextFixed, 0) != 0)
            throw new InjectedPlacementException();
    }

    private static void CountMovable() => Interlocked.Increment(ref movableCalls);

    internal sealed class InjectedPlacementException() : Exception("Injected fixed-placement failure.");
}
