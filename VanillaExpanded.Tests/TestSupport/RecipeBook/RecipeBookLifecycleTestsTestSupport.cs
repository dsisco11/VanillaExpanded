using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeBookLifecycleTestsTestSupport
{

internal static void AssertEmptyStateCentered(GuiDialogRecipeBook menu)
    {
        var composer = menu.SingleComposer;
        var empty = composer.GetDynamicText("empty");
        Assert.Equal(EnumTextOrientation.Center, empty.Font.Orientation);
        if (empty.GetText().Length == 0) return;
        var viewport = composer.GetElement("tiles").InsideClipBounds;
        double centerX = viewport.absX + viewport.InnerWidth / 2;
        Assert.Equal(centerX, empty.Bounds.absX + empty.Bounds.InnerWidth / 2, 4);
        double bottom = empty.Bounds.absY + empty.Bounds.OuterHeight;
        foreach (string key in new[] { "clear-search", "show-uncraftable" })
        {
            var button = composer.GetButton(key);
            if (!button.Visible) continue;
            Assert.Equal(centerX, button.Bounds.absX + button.Bounds.OuterWidth / 2, 4);
            Assert.Equal(bottom + GuiElement.scaled(8), button.Bounds.absY, 4);
            bottom = button.Bounds.absY + button.Bounds.OuterHeight;
        }
        Assert.Equal(viewport.absY + viewport.InnerHeight / 2, (empty.Bounds.absY + bottom) / 2, 4);
    }

internal sealed class HandbookDialog(ICoreClientAPI api) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => "handbook";
        public override double DrawOrder => 0.2;
    }

    internal sealed class HandbookPageBehavior(CollectibleObject collectible)
        : CollectibleBehavior(collectible), IHandBookPageCodeProvider
    {
        internal ItemStack? Stack;
        public string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
        {
            Stack = stack;
            stack.Attributes.SetBool("provider-mutation", true);
            return "custom-output-" + stack.Attributes.GetString("wood");
        }
    }

internal sealed class HiddenFilter : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => false;
    }

internal static void AssertBookTabPosition(MenuFixture f)
    {
        var composer = f.Parent.Composers["maininventory"];
        var button = f.ToggleButton.Bounds;
        var hint = f.ToggleButton.Hint.Bounds;
        var output = composer.GetElement("outputslot").Bounds;
        var inputs = composer.GetElement("craftinggrid").Bounds;
        Assert.Same(output, button.ParentBounds);
        Assert.Equal(output.absX - GuiElement.scaled(4), button.absX + button.OuterWidth, 4);
        Assert.Equal(output.absY + output.InnerHeight / 2, button.absY + button.InnerHeight / 2, 4);
        Assert.True(button.absY >= inputs.absY + inputs.OuterHeight);
        Assert.Equal(button.absX, hint.absX, 4);
        Assert.Equal(button.absY, hint.absY, 4);
        Assert.Equal(button.OuterWidth, hint.OuterWidth, 4);
        Assert.Equal(button.OuterHeight, hint.OuterHeight, 4);
    }

internal sealed class LifecycleFilterProvider : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => true;
    }

internal static GuiElementIconTabs GetCategoryTabs(GuiComposer composer) =>
        Assert.IsType<GuiElementIconTabs>(composer.GetElement("categories"));

}
