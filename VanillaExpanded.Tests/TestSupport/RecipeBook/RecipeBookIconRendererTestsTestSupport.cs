using Cairo;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.TestSupport.RecipeBook;

internal static class RecipeBookIconRendererTestsTestSupport
{

internal static void AssertPopupDepth()
    {
        using var graphics = new RecipeBookRenderFixture();
        graphics.Render.SetupGet(value => value.FrameHeight).Returns(1080);
        var f = new AllocationFixture();
        var recipe = f.Recipe(f.Ingredient(f.Stack(1, "board", 1)));
        recipe.Output!.Quantity = 4;
        f.Register(recipe);
        var session = new RecipeBookSession(f.Catalogue.Build(recipe), f.Allocator,
            () => f.Snapshot(), (_, _) => throw new InvalidOperationException());
        session.Refresh();
        using var tiles = CreateTiles(graphics, session);
        using var popup = new GuiElementListMenu(graphics.Api.Object, ["availability", "name"],
            ["Availability", "Name"], 0, (_, _) => { },
            ElementBounds.Fixed(0, -30, 144, 30).WithEmptyParent(), CairoFont.WhiteSmallText(), false);
        popup.Bounds.CalcWorldBounds();
        popup.ComposeDynamicElements();
        popup.Open();
        graphics.Render.Invocations.Clear();
        tiles.RenderInteractiveElements(0);
        var label = Assert.Single(graphics.Render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexturePremultipliedAlpha)
            && call.Arguments.Count == 7 && call.Arguments[1] is double);
        popup.RenderInteractiveElements(0);
        var background = Assert.Single(graphics.Render.Invocations, call =>
            call.Method.Name == nameof(IRenderAPI.Render2DTexture) && call.Arguments.Count == 7);
        Assert.True(Convert.ToSingle(label.Arguments[5]) < Convert.ToSingle(background.Arguments[5]));
        var popupClip = Assert.IsAssignableFrom<ElementBounds>(graphics.Render.Invocations.Last(call =>
            call.Method.Name == nameof(IRenderAPI.PushScissor)).Arguments[0]);
        Assert.True(popupClip.renderY <= (double)label.Arguments[2]);
        Assert.True(popupClip.renderY + popupClip.InnerHeight >= (double)label.Arguments[2] + (double)label.Arguments[4]);
        Assert.Empty(graphics.Clips);
    }

internal static GuiElementRecipeBookTiles CreateTiles(RecipeBookRenderFixture graphics, RecipeBookSession session,
    int rows = 1, double tileSize = RecipeBookTileGeometry.TileSize)
    {
        var viewport = ElementBounds.Fixed(0, 0, 202, rows * 50 - 5).WithEmptyParent();
        viewport.CalcWorldBounds();
        var tiles = new GuiElementRecipeBookTiles(graphics.Api.Object,
            ElementBounds.Fixed(0, 0, 202, (rows + 1) * 50).WithParent(viewport), viewport, session, _ => { }, (_, _) => { },
            graphics.Cache, graphics.CreateShader, tileSize);
        tiles.BeforeCalcBounds();
        tiles.Bounds.CalcWorldBounds();
        return tiles;
    }

}
