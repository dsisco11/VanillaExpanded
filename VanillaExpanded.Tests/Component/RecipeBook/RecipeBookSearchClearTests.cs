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
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookSearchClearTests
{
    [Theory]
    [InlineData("release-only")]
    [InlineData("right-button")]
    [InlineData("outside-release")]
    [InlineData("changed-text")]
    [InlineData("disabled")]
    [InlineData("focus-lost")]
    public void InlineSearchClearRequiresAnUninterruptedLeftClick(string action)
    {
        using var f = new MenuFixture();
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var input = Assert.IsType<GuiElementClearableTextInput>(menu.SingleComposer.GetTextInput("search"));
        input.SetValue("original");
        Assert.True(menu.SingleComposer.FocusElement(input.TabIndex));
        int x = (int)(input.ClearBounds.absX + input.ClearBounds.OuterWidth / 2);
        int y = (int)(input.ClearBounds.absY + input.ClearBounds.OuterHeight / 2);
        var button = action == "right-button" ? EnumMouseButton.Right : EnumMouseButton.Left;
        if (action != "release-only")
            input.OnMouseDown(f.Lifecycle.Api.Object, new MouseEvent(x, y, button, 0));
        switch (action)
        {
            case "outside-release": x = (int)input.FieldBounds.absX - 10; break;
            case "changed-text": input.SetValue("updated"); break;
            case "disabled": input.Enabled = false; input.Enabled = true; break;
            case "focus-lost": input.OnFocusLost(); break;
        }
        input.OnMouseUp(f.Lifecycle.Api.Object, new MouseEvent(x, y, button, 0));
        Assert.Equal(action == "changed-text" ? "updated" : "original", input.GetText());
    }
}
