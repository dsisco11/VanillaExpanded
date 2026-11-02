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
public sealed class RecipeBookShaderAdapterTests
{
    [Fact]
    public void DedicatedShaderUsesTheNativeBaseAndModOwnedStages()
    {
        var api = new Mock<ICoreClientAPI>();
        var shaders = new Mock<IShaderAPI>();
        var vertex = new Shader();
        var fragment = new Shader();
        shaders.Setup(value => value.NewShader(EnumShaderType.VertexShader)).Returns(vertex);
        shaders.Setup(value => value.NewShader(EnumShaderType.FragmentShader)).Returns(fragment);
        api.SetupGet(value => value.Shader).Returns(shaders.Object);

        var program = new RecipeBookIconShaderProgram(api.Object);

        Assert.IsAssignableFrom<ShaderProgram>(program);
        Assert.Equal(Constants.ModId, program.AssetDomain);
        Assert.Same(vertex, program.VertexShader);
        Assert.Same(fragment, program.FragmentShader);
        shaders.Verify(value => value.NewShaderProgram(), Times.Never);
    }
}
