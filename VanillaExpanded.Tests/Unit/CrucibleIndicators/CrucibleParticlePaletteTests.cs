using Moq;
using VanillaExpanded.CrucibleIndicators;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.CrucibleIndicators;

/// <summary>Verifies ingredient palette preservation, shader submission, and stable sampling.</summary>
[Trait("Category", "Unit")]
public sealed class CrucibleParticlePaletteTests
{
    #region Public API
    /// <summary>Different ore pixels remain distinct and changing ingredients invalidates the retained palette.</summary>
    [Fact]
    public void Ingredients_RetainDistinctColorsAndInvalidateOnReplacement()
    {
        var client = Mock.Of<ICoreClientAPI>();
        var first = new Mock<MockItem>(1, (byte)0, null!);
        var second = new Mock<MockItem>(2, (byte)0, null!);
        first.Setup(value => value.GetRandomColor(client, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFFFF0000));
        second.Setup(value => value.GetRandomColor(client, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFF0000FF));
        var crucible = new ItemStack(new Block());
        var contents = new[] { new ItemStack(first.Object), null!, new ItemStack(second.Object) };
        var cache = new CrucibleParticlePalette();
        var palette = cache.Resolve(client, crucible, contents);
        Assert.Same(palette, cache.Resolve(client, crucible, contents));
        var shader = new Mock<IShaderProgram>();
        float[]? values = null;
        shader.Setup(value => value.Uniforms4("metalPalette", 16, It.IsAny<float[]>()))
            .Callback<string, int, float[]>((_, _, data) => values = data);
        palette.Submit(shader.Object, "metalPalette");
        Assert.NotNull(values);
        Assert.Equal(new float[] { 1, 0, 0, 1, 0, 0, 1, 1 }, values![..8]);
        first.Verify(value => value.GetRandomColor(client, It.IsAny<ItemStack>()), Times.Exactly(8));
        second.Verify(value => value.GetRandomColor(client, It.IsAny<ItemStack>()), Times.Exactly(8));
        Assert.NotSame(palette, cache.Resolve(client, crucible, [new ItemStack(first.Object)]));
    }

    /// <summary>Missing texture data uses neutral metal rather than the generic food palette's beige.</summary>
    [Fact]
    public void MissingClient_UsesNeutralMetalColors()
    {
        var crucible = new ItemStack(new Block());
        var palette = new CrucibleParticlePalette().Resolve(null, crucible, []);
        var shader = new Mock<IShaderProgram>();
        float[]? values = null;
        shader.Setup(value => value.Uniforms4("metalPalette", 16, It.IsAny<float[]>()))
            .Callback<string, int, float[]>((_, _, data) => values = data);
        palette.Submit(shader.Object, "metalPalette");
        Assert.NotNull(values);
        Assert.Equal(140 / 255f, values![0]);
        Assert.Equal(145 / 255f, values[1]);
        Assert.Equal(153 / 255f, values[2]);
    }
    #endregion
}
