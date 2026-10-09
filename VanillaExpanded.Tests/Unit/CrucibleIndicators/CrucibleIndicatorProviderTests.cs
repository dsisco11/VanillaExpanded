using Moq;
using VanillaExpanded.CrucibleIndicators;
using VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;
using VanillaExpanded.Tests.Mocks;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.CrucibleIndicators;

/// <summary>Checks crucible quantity, engine solidification, and independently configurable presentation.</summary>
[Trait("Category", "Unit")]
public sealed class CrucibleIndicatorProviderTests
{
    #region Public API
    /// <summary>Poured units determine fill independently of temperature, while the engine threshold selects phase.</summary>
    [Theory]
    [InlineData(640, 20, false, 0.25f)]
    [InlineData(1280, 899, false, 0.5f)]
    [InlineData(1280, 900, true, 0.5f)]
    [InlineData(2560, 1100, true, 1)]
    [InlineData(3000, 1100, true, 1)]
    public void Contents_MapUnitsAndUseMetalMeltingPoint(int units, float temperature, bool molten, float fill)
    {
        var slot = CreateSlot(units, temperature);
        Assert.True(new CrucibleIndicatorProvider(molten).TryGetIndicator(slot, out var indicator));
        Assert.False(new CrucibleIndicatorProvider(!molten).TryGetIndicator(slot, out _));
        Assert.Equal(fill, indicator.Fill);
    }

    /// <summary>Empty, negative-unit, and unresolved-output crucibles do not invent metal contents.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EmptyContents_HideIndicator(int units)
    {
        var slot = CreateSlot(units, 1100);
        Assert.False(new CrucibleIndicatorProvider(true).TryGetIndicator(slot, out _));
        Assert.False(new CrucibleIndicatorProvider(false).TryGetIndicator(slot, out _));
        slot.Itemstack!.Attributes.RemoveAttribute("output");
        Assert.False(new CrucibleIndicatorProvider(true).TryGetIndicator(slot, out _));
    }

    /// <summary>Plain fired crucibles outside a firepit and unrelated items have no persisted metal amount.</summary>
    [Fact]
    public void OrdinaryAndEmptyFiredItems_AreExcluded()
    {
        var slot = CreateSlot(100, 1100);
        foreach (var block in new Block[] { new Block(), new BlockSmeltingContainer() })
        {
            slot.Itemstack = new ItemStack(block);
            Assert.False(new CrucibleIndicatorProvider(false).TryGetIndicator(slot, out _));
            Assert.False(new CrucibleIndicatorProvider(true).TryGetIndicator(slot, out _));
        }
    }

    /// <summary>The visual reference changes fill immediately, and disabling the provider hides both phases.</summary>
    [Fact]
    public void Configuration_ControlsReferenceAndEnablement()
    {
        var config = VanillaExpandedModSystem.Config;
        float originalCapacity = config.CrucibleIndicatorCapacityUnits;
        bool originalEnabled = config.EnableCrucibleIndicators;
        try
        {
            config.CrucibleIndicatorCapacityUnits = 1000;
            config.EnableCrucibleIndicators = true;
            var slot = CreateSlot(500, 1100);
            Assert.True(new CrucibleIndicatorProvider(true).TryGetIndicator(slot, out var indicator));
            Assert.Equal(0.5f, indicator.Fill);
            VanillaExpandedModSystem.Config.EnableCrucibleIndicators = false;
            Assert.False(new CrucibleIndicatorProvider(true).TryGetIndicator(slot, out _));
        }
        finally
        {
            config.CrucibleIndicatorCapacityUnits = originalCapacity;
            config.EnableCrucibleIndicators = originalEnabled;
        }
    }

    /// <summary>Invalid reference quantities cannot produce NaN geometry or inverted fills.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidReference_HidesIndicator(float capacity)
    {
        var config = VanillaExpandedModSystem.Config;
        float originalCapacity = config.CrucibleIndicatorCapacityUnits;
        try
        {
            config.CrucibleIndicatorCapacityUnits = capacity;
            Assert.False(new CrucibleIndicatorProvider(true).TryGetIndicator(CreateSlot(100, 1100), out _));
        }
        finally { config.CrucibleIndicatorCapacityUnits = originalCapacity; }
    }

    /// <summary>Heating within the solid range preserves the reported quantity.</summary>
    [Fact]
    public void Heating_PreservesAmount()
    {
        var provider = new CrucibleIndicatorProvider(false);
        Assert.True(provider.TryGetIndicator(CreateSlot(1280, 20), out var cold));
        Assert.True(provider.TryGetIndicator(CreateSlot(1280, 800), out var hot));
        Assert.Equal(cold.Fill, hot.Fill);
    }

    /// <summary>Metal has a separate engine identity, stronger damping, and restrained camera impulses.</summary>
    [Fact]
    public void MetalProfile_HasIndependentSlowerDynamics()
    {
        var water = LiquidSloshSimulationProfile.Water;
        var metal = LiquidSloshSimulationProfile.Metal;
        Assert.NotEqual(water.ShaderName, metal.ShaderName);
        Assert.Equal(water.Gravity, metal.Gravity);
        Assert.True(metal.Damping > water.Damping);
        Assert.True(metal.AccelerationScale.X < water.AccelerationScale.X);
        Assert.True(metal.AccelerationScale.Y < metal.AccelerationScale.X);
        Assert.True(metal.MaximumAcceleration < water.MaximumAcceleration);
    }

    /// <summary>Metal colors use the contained collectible and remain stable across repeated amount samples.</summary>
    [Fact]
    public void MetalColors_SampleContentsOnceAndReuseAcrossStacks()
    {
        var client = Mock.Of<ICoreClientAPI>();
        var metal = new Mock<MockItem>(1, (byte)0, null!) { CallBase = true };
        metal.Setup(value => value.GetRandomColor(client, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFF123456));
        var colors = new CrucibleMetalColors();
        var first = colors.Resolve(client, new ItemStack(metal.Object));
        var second = colors.Resolve(client, new ItemStack(metal.Object));
        Assert.Equal(first, second);
        Assert.Equal(0x12 / 255f, first.X, 5);
        Assert.Equal(0x34 / 255f, first.Y, 5);
        Assert.Equal(0x56 / 255f, first.Z, 5);
        metal.Verify(value => value.GetRandomColor(client, It.IsAny<ItemStack>()), Times.Exactly(16));
    }

    /// <summary>Four-slot firepit ingredients convert to metal units and remain solid before the game's smelt operation.</summary>
    [Fact]
    public void FirepitInput_UsesIngredientsAndExcludesOtherSlots()
    {
        var world = new Mock<IClientWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(value => value.World).Returns(world.Object);
        var client = api.As<ICoreClientAPI>();
        client.Setup(value => value.World).Returns(world.Object);
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.GetModSystem<RecipeRegistrySystem>(true)).Returns(new RecipeRegistrySystem());
        api.Setup(value => value.ModLoader).Returns(loader.Object);
        var registry = new Mock<IClassRegistryAPI>();
        registry.Setup(value => value.CreateInvNetworkUtil(It.IsAny<InventoryBase>(), api.Object))
            .Returns(Mock.Of<IInventoryNetworkUtil>());
        api.Setup(value => value.ClassRegistry).Returns(registry.Object);
        var inventory = new InventorySmelting("crucible-test", api.Object);
        var input = new BlockSmeltingContainer();
        input.OnLoadedNative(api.Object);
        inventory[1].Itemstack = new ItemStack(input);
        var metal = new Mock<MockItem>(1, (byte)0, api.Object) { CallBase = true };
        metal.Setup(value => value.GetRandomColor(client.Object, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFFFF0000));
        var nuggets = new Mock<MockItem>(2, (byte)0, api.Object) { CallBase = true };
        nuggets.Setup(value => value.GetRandomColor(client.Object, It.IsAny<ItemStack>())).Returns(unchecked((int)0xFF0000FF));
        var properties = new CombustibleProperties
        {
            RequiresContainer = true, MeltingPoint = 1000, SmeltedRatio = 20,
            SmeltedStack = new JsonItemStack { ResolvedItemstack = new ItemStack(metal.Object) }
        };
        nuggets.Setup(value => value.GetCombustibleProperties(It.IsAny<IWorldAccessor>(), It.IsAny<ItemStack>(), null))
            .Returns(properties);
        nuggets.Setup(value => value.GetTemperature(world.Object, It.IsAny<ItemStack>())).Returns(1100);
        inventory.Slots[0].Itemstack = new ItemStack(nuggets.Object, 128);
        Assert.True(new CrucibleIndicatorProvider(false).TryGetIndicator(inventory[1], out var indicator));
        Assert.Equal(0.25f, indicator.Fill);
        Assert.NotNull(indicator.ParticlePalette);
        float[]? paletteValues = null;
        var shader = new Mock<IShaderProgram>();
        shader.Setup(value => value.Uniforms4("metalPalette", 16, It.IsAny<float[]>()))
            .Callback<string, int, float[]>((_, _, values) => paletteValues = values);
        indicator.ParticlePalette.Submit(shader.Object, "metalPalette");
        // The output metal is red, but the actual ore is blue: pieces must retain the ore color.
        Assert.Equal(new float[] { 0, 0, 1, 1 }, paletteValues![..4]);
        Assert.False(new CrucibleIndicatorProvider(true).TryGetIndicator(inventory[1], out _));
        Assert.False(new CrucibleIndicatorProvider(false).TryGetIndicator(inventory.Slots[0], out _));
        inventory.Slots[0].Itemstack = null;
        Assert.False(new CrucibleIndicatorProvider(false).TryGetIndicator(inventory[1], out _));
    }
    #endregion

    #region Private
    /// <summary>Creates real persisted crucible contents with deterministic engine temperature and melting APIs.</summary>
    private static ItemSlot CreateSlot(int units, float temperature)
    {
        var world = new Mock<IWorldAccessor>();
        var api = new Mock<ICoreAPI>();
        api.Setup(value => value.World).Returns(world.Object);
        var inventory = new InventoryGeneric(1, "crucibleindicator", "test", null!)
        {
            Api = api.Object, InvNetworkUtil = Mock.Of<IInventoryNetworkUtil>()
        };
        var metal = new Mock<MockItem>(1, (byte)0, api.Object) { CallBase = true };
        metal.Setup(value => value.GetMeltingPoint(world.Object, null, It.IsAny<ItemSlot>())).Returns(1000);
        world.Setup(value => value.GetItem(1)).Returns(metal.Object);
        var block = new Mock<BlockSmeltedContainer> { CallBase = true };
        block.Setup(value => value.GetTemperature(world.Object, It.IsAny<ItemStack>())).Returns(temperature);
        var stack = new ItemStack(block.Object);
        block.Object.SetContents(stack, new ItemStack(metal.Object), units);
        inventory[0].Itemstack = stack;
        return inventory[0];
    }
    #endregion
}
