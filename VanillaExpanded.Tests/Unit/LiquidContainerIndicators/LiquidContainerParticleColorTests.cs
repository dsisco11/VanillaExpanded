using System.Numerics;
using Moq;
using VanillaExpanded.LiquidContainerIndicators;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.LiquidContainerIndicators;

/// <summary>Checks stable material-color sampling and replacement without vessel-color sampling.</summary>
public sealed class LiquidContainerParticleColorTests
{
    #region Public API
    /// <summary>The atlas owns the average color and the material's random-color method is bypassed.</summary>
    [Fact]
    public void BakedParticleTextureUsesAtlasAverage()
    {
        var api = new Mock<ICoreClientAPI>();
        var atlas = new Mock<IItemTextureAtlasAPI>();
        var position = new TextureAtlasPosition();
        atlas.SetupGet(value => value.Positions).Returns([position]);
        atlas.Setup(value => value.GetAverageColor(0)).Returns(unchecked((int)0xFFCC6633));
        api.SetupGet(value => value.ItemTextureAtlas).Returns(atlas.Object);
        var material = new Mock<MockItem>(1, (byte)0, api.Object);
        material.Object.ParticlesTextureCode = "liquid";
        material.Object.Textures = new() { ["liquid"] = new CompositeTexture { Baked = new BakedCompositeTexture { TextureSubId = 0 } } };
        var liquid = new ItemStack(material.Object);
        var sampler = new LiquidContainerParticleColor();
        var tint = sampler.Resolve(api.Object, liquid, liquid, new Vector4(0, 1, 0, 0.5f));
        Assert.Equal(new Vector4(0.8f, 0.4f, 0.2f, 0.5f), tint);
        material.Verify(value => value.GetRandomColor(It.IsAny<ICoreClientAPI>(), It.IsAny<ItemStack>()), Times.Never);
        atlas.Verify(value => value.GetAverageColor(0), Times.Once);
    }

    /// <summary>Samples material colors once and resamples when a vessel receives another material.</summary>
    [Fact]
    public void MaterialReplacementRefreshesCachedTint()
    {
        var api = Mock.Of<ICoreClientAPI>();
        var material = new Mock<MockItem>(1, (byte)0, api);
        material.Setup(item => item.GetRandomColor(api, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFFCC6633));
        var replacement = new Mock<MockItem>(2, (byte)0, api);
        replacement.Setup(item => item.GetRandomColor(api, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFF339966));
        var vessel = new ItemStack(new MockItem(3, api: api));
        var sampler = new LiquidContainerParticleColor();
        var fallback = new Vector4(0, 0, 1, 0.5f);
        var expected = new Vector4(0.8f, 0.4f, 0.2f, 0.5f);
        Assert.Equal(expected, sampler.Resolve(api, vessel, new ItemStack(material.Object), fallback));
        Assert.Equal(expected, sampler.Resolve(api, vessel, new ItemStack(material.Object), fallback));
        material.Verify(item => item.GetRandomColor(api, It.IsAny<ItemStack>()), Times.Once);
        Assert.Equal(new Vector4(0.2f, 0.6f, 0.4f, 0.5f),
            sampler.Resolve(api, vessel, new ItemStack(replacement.Object), fallback));
    }
    #endregion
}
