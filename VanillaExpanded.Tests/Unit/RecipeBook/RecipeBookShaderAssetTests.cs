using Cairo;
using Moq;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaExpanded.Tests;
using VanillaExpanded.Tests.TestSupport.Configuration;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookIconRendererTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookShaderAssetTests
{
    [Fact]
    public void ShaderSourceRetainsApprovedPaletteOutlineAndSeparateCornerGeometry()
    {
        string source = System.IO.File.ReadAllText(MenuFixture.GetAssetPath("shaders", "recipebook_icons.fsh"));
        Assert.Contains("vec4(73.0 / 255.0, 61.0 / 255.0, 44.0 / 255.0, 1.0)", source);
        Assert.Contains("vec4(113.0 / 255.0, 97.0 / 255.0, 70.0 / 255.0, 1.0)", source);
        Assert.Contains("recipeState == 2 ? vec4(0.0, 0.0, 0.0, 0.92)", source);
        Assert.Contains("icon.rgb = vec3(luminance * 0.12)", source);
        Assert.Contains("float borderWidth = max(1.0, floor(guiScale + 0.5))", source);
        Assert.Contains("if (edgeDistance < borderWidth)", source);
        Assert.Contains("recipeState == 3 ? vec4(0.95, 0.85, 0.12, 1.0)", source);
        Assert.Contains("recipeState == 1 ? vec4(0.85, 0.12, 0.12, 1.0)", source);
        Assert.Contains("vec2 corner = uv * tileBounds.zw - floor(3.0 * guiScale + 0.5)", source);
        Assert.Contains("float length = max(1.0, floor(8.0 * guiScale + 0.5))", source);
        Assert.Contains("if (selected != 0 && all(greaterThanEqual(corner, vec2(0.0))) &&", source);
        Assert.Contains("((corner.x < length && corner.y < borderWidth) || (corner.y < length && corner.x < borderWidth))", source);
        Assert.Contains("vec4(245.0 / 255.0, 213.0 / 255.0, 141.0 / 255.0, 1.0)", source);
        Assert.Contains("min(tileBounds.z, tileBounds.w) - 2.0 * floor(previewInset * guiScale + 0.5)", source);
        Assert.Contains("(uv * tileBounds.zw - (tileBounds.zw - size) * 0.5) / size", source);
        Assert.True(source.IndexOf("if (edgeDistance < borderWidth)", StringComparison.Ordinal)
            < source.IndexOf("if (selected != 0", StringComparison.Ordinal));
    }
}
