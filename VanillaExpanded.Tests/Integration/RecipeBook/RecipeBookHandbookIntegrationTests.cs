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

namespace VanillaExpanded.Tests.Integration.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Integration)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookHandbookIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HandbookButtonOpensSelectedOutputUsingNativePageCodesWithoutPlacing(bool customPage, bool keyboard)
    {
        using var f = new MenuFixture(includeRecipes: true);
        var recipe = Assert.Single(f.Lifecycle.Recipes);
        var output = recipe.Output!.ResolvedItemStack!;
        output.Attributes.SetString("wood", "oak");
        var provider = new HandbookPageBehavior(output.Collectible);
        if (customPage) output.Collectible.CollectibleBehaviors = [provider];
        f.Lifecycle.Recipes.Add(f.Lifecycle.F.Catalogue.Recipe("other-output"));
        f.Lifecycle.F.Register(f.Lifecycle.Recipes.ToArray());
        f.Lifecycle.FinalizeWorld();
        f.Recompose();
        var links = new List<string>();
        using var handbook = new HandbookDialog(f.Lifecycle.Api.Object);
        f.Lifecycle.Api.SetupGet(api => api.LinkProtocols).Returns(
            new Dictionary<string, Action<LinkTextComponent>> { ["handbook"] = link =>
            {
                links.Add(link.Href);
                if (!handbook.IsOpened()) handbook.TryOpen();
            } });
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var button = menu.SingleComposer.GetButton("handbook");
        Assert.False(button.Enabled);
        f.Click(button, handled: false);
        Assert.Empty(links);
        var row = Assert.Single(session.Rows, row => row.Recipe.Recipe == recipe);
        session.Select(row.Output.Id);
        Assert.True(button.Enabled);
        var network = Mock.Get(f.Lifecycle.Api.Object.Network);
        network.Invocations.Clear();
        if (keyboard)
        {
            Assert.True(menu.SingleComposer.FocusElement(button.TabIndex));
            var enter = new KeyEvent { KeyCode = (int)GlKeys.Enter };
            menu.OnKeyDown(enter);
            Assert.True(enter.Handled);
        }
        else f.Click(button);
        Assert.Equal(customPage ? "handbook://custom-output-oak"
            : "handbook://" + GuiHandbookItemStackPage.PageCodeForStack(row.Recipe.GetPreview()!), Assert.Single(links));
        Assert.Equal(handbook.DrawOrder, menu.DrawOrder);
        Assert.True(handbook.IsOpened());
        var gui = Mock.Get(f.Lifecycle.Api.Object.Gui);
        gui.Verify(api => api.RequestFocus(handbook), Times.Exactly(2));
        Assert.True(f.Parent.IsOpened());
        Assert.True(menu.IsOpened());
        Assert.Equal(row.Output.Id, session.SelectedOutput);
        Assert.Equal("oak", row.Recipe.GetPreview()!.Attributes.GetString("wood"));
        Assert.False(row.Recipe.GetPreview()!.Attributes.HasAttribute("provider-mutation"));
        Assert.False(output.Attributes.HasAttribute("provider-mutation"));
        Assert.Empty(network.Invocations);
        if (customPage) Assert.NotSame(output, provider.Stack);
        var composer = menu.SingleComposer;
        f.ResizePanelSpace(composer.Bounds.fixedWidth, composer.Bounds.fixedHeight - 10);
        menu.TryOpen();
        Assert.NotSame(composer, menu.SingleComposer);
        button = menu.SingleComposer.GetButton("handbook");
        Assert.True(button.Enabled);
        f.Click(button);
        Assert.Equal(2, links.Count);
        gui.Verify(api => api.RequestFocus(handbook), Times.Exactly(3));
        session.Select(session.Rows.Single(row => row.Recipe.Recipe != recipe).Output.Id);
        f.Click(button);
        Assert.Equal("handbook://" + GuiHandbookItemStackPage.PageCodeForStack(
            session.Rows.Single(row => row.Output.Id == session.SelectedOutput).Recipe.GetPreview()!), links[^1]);
    }
}
