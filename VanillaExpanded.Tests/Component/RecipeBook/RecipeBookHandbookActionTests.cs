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
using VanillaExpanded.Tests;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookHandbookActionTests
{
    [Fact]
    public void HandbookButtonReportsUnavailableNativeHandler()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        session.Select(Assert.Single(session.Rows).Output.Id);
        f.Lifecycle.Api.SetupGet(api => api.LinkProtocols).Returns(new Dictionary<string, Action<LinkTextComponent>>());
        f.Click(menu.SingleComposer.GetButton("handbook"));
        f.Lifecycle.Api.Verify(api => api.TriggerIngameError(menu, "recipebook",
            "vanillaexpanded:recipebook-handbook-unavailable"), Times.Once);
    }

    [Fact]
    public void HandbookButtonIsDisabledForSelectedOutputWithoutResolvedPreview()
    {
        using var f = new MenuFixture(includeRecipes: true);
        Assert.Single(f.Lifecycle.Recipes).Output!.ResolvedItemStack = null;
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        session.Select(Assert.Single(session.Rows).Output.Id);
        Assert.False(menu.SingleComposer.GetButton("handbook").Enabled);
        Assert.False(menu.SingleComposer.GetButton("handbook").Focusable);
        f.Click(menu.SingleComposer.GetButton("handbook"), handled: false);
    }
}
