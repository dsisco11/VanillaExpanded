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
        Assert.True(button.absX + button.OuterWidth <= output.absX
            || output.absX + output.OuterWidth <= button.absX
            || button.absY + button.OuterHeight <= output.absY
            || output.absY + output.OuterHeight <= button.absY);
        Assert.True(button.absY >= inputs.absY + inputs.OuterHeight
            || button.absY + button.OuterHeight <= inputs.absY);
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
