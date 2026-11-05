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
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookIconRendererTestsTestSupport;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Unit)]
[Collection("Recipe book native GUI")]
public sealed class CraftingCataloguePreviewSharingTests
{
    [Fact]
    public void PreviewSharingUsesNativeEqualityForAttributesAndQuantity()
    {
        var f = new AllocationFixture();
        var output = f.Stack(99, "output", 4);
        output.Attributes.SetString("variant", "red");
        output.TempAttributes.SetString("renderVariant", "smooth");
        var first = f.Catalogue.Recipe("first", output, 4);
        var same = f.Catalogue.Recipe("same", CraftingStackSnapshot.Copy(output), 8);
        var persistentOutput = CraftingStackSnapshot.Copy(output);
        persistentOutput.Attributes.SetString("variant", "blue");
        var persistent = f.Catalogue.Recipe("persistent", persistentOutput);
        var temporaryOutput = CraftingStackSnapshot.Copy(output);
        temporaryOutput.TempAttributes.SetString("renderVariant", "rough");
        var temporary = f.Catalogue.Recipe("temporary", temporaryOutput);
        var entries = f.Catalogue.Build(first, same, persistent, temporary).Recipes;
        Assert.Equal(entries[0].PreviewId, entries[1].PreviewId);
        Assert.NotEqual(entries[0].PreviewId, entries[2].PreviewId);
        Assert.Equal(entries[0].PreviewId, entries[3].PreviewId);
        Assert.Equal("smooth", entries[0].GetPreview()!.TempAttributes.GetString("renderVariant"));
        Assert.Equal("rough", entries[3].GetPreview()!.TempAttributes.GetString("renderVariant"));
    }
}
