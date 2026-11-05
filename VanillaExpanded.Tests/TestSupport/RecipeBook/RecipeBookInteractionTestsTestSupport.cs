using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeBookInteractionTestsTestSupport
{

internal sealed class CallbackFilter(Action callback) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection)
        {
            callback();
            return true;
        }
    }

    internal sealed class PredicateFilter(System.Func<CraftingRecipeSelection, bool> matches) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => matches(selection);
    }

internal sealed class CustomGridRecipe : GridRecipe { }

    internal sealed class NamedOutputItem(int id) : MockItem(id)
    {
        public override string GetHeldItemName(ItemStack stack) => Code.ToString();
    }

internal static TagSet Tag(params ushort[] handles)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write((ushort)handles.Length);
        foreach (ushort handle in handles) writer.Write(handle);
        writer.Flush();
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);
        return TagSet.FromBytes(reader);
    }

    internal static RecipeBookSession Session(AllocationFixture f, params GridRecipe[] recipes) =>
        new(f.Catalogue.Build(recipes), f.Allocator,
            () => CraftingInventorySnapshot.Capture(f.Player, 3, 3, Environment.CurrentManagedThreadId, true),
            (_, _) => throw new InvalidOperationException("This test must not dispatch."));

    internal static CraftingRecipeSelection WithTool(AllocationFixture f)
    {
        var ingredient = f.Ingredient(f.Stack(3, "tool", 1));
        ingredient.Consume = false;
        ingredient.ToolDurabilityCost = 0;
        var recipe = f.Recipe(ingredient);
        f.Register(recipe);
        var session = Session(f, recipe);
        session.Refresh();
        return Assert.Single(session.Rows);
    }

    internal sealed class GuiScaleScope : IDisposable
    {
        private readonly float previous = RuntimeEnv.GUIScale;
        internal GuiScaleScope(float scale = 1) => RuntimeEnv.GUIScale = scale;
        public void Dispose() => RuntimeEnv.GUIScale = previous;
    }

    internal static ElementBounds Calculated(ElementBounds bounds)
    {
        bounds.CalcWorldBounds();
        return bounds;
    }

    internal static void PrepareTiles(GuiElementRecipeBookTiles tiles)
    {
        foreach (GuiElementRecipeBookTile tile in tiles.Elements) tile.PlaySound = false;
        tiles.BeforeCalcBounds();
        tiles.Bounds.CalcWorldBounds();
    }

    internal sealed class HeadlessScrollbar(ICoreClientAPI api, Action<float> changed, ElementBounds bounds)
        : GuiElementScrollbar(api, changed, bounds)
    {
        public override void RecomposeHandle() { }
    }

}
