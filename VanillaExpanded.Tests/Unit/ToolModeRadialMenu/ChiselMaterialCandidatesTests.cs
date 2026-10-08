using Moq;
using Vintagestory.Server;
using Newtonsoft.Json.Linq;
using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

/// <summary>Checks read-only material discovery against native eligibility and inventory boundaries.</summary>
[Trait("Category", "Unit")]
public sealed class ChiselMaterialCandidatesTests
{
    #region Public API
    #region Eligibility
    /// <summary>Matches native material rules while config and player mode change.</summary>
    [Theory]
    [InlineData("off", EnumBlockMaterial.Stone, EnumDrawType.Cube, null, false)]
    [InlineData("stonewood", EnumBlockMaterial.Stone, EnumDrawType.Cube, null, true)]
    [InlineData("stonewood", EnumBlockMaterial.Metal, EnumDrawType.Cube, null, false)]
    [InlineData("all", EnumBlockMaterial.Water, EnumDrawType.Cube, null, false)]
    [InlineData("all", EnumBlockMaterial.Stone, EnumDrawType.Cross, null, false)]
    [InlineData("stonewood", EnumBlockMaterial.Metal, EnumDrawType.Cross, true, true)]
    [InlineData("all", EnumBlockMaterial.Stone, EnumDrawType.Cube, false, false)]
    public void QuietValidator_MatchesNativeRules(string mode, EnumBlockMaterial material, EnumDrawType draw, bool? flag, bool expected)
    {
        var fixture = new Fixture();
        fixture.Config.SetString("microblockChiseling", mode);
        var block = new MaterialBlock(1) { BlockMaterial = material, DrawType = draw, Shape = new CompositeShape { Base = new AssetLocation("block/basic/cross") } };
        if (flag.HasValue) block.Attributes = new JsonObject(JObject.FromObject(new { canChisel = flag.Value }));
        Assert.Equal(expected, ItemChisel.IsValidChiselingMaterial(fixture.Api.Object, fixture.Position, block, fixture.Player.Object));
        Assert.Equal(expected, ChiselMaterialCandidates.IsValidMaterial(fixture.Api.Object, fixture.Player.Object, fixture.Position, block));
        fixture.GameMode = EnumGameMode.Creative;
        Assert.Equal(ItemChisel.IsValidChiselingMaterial(fixture.Api.Object, fixture.Position, block, fixture.Player.Object),
            ChiselMaterialCandidates.IsValidMaterial(fixture.Api.Object, fixture.Player.Object, fixture.Position, block));
    }

    /// <summary>Preserves native short-circuit conditional calls without scanning error messages.</summary>
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 2, true)]
    public void QuietValidator_ConditionalRuleDoesNotEmitErrors(bool allowed, int calls, bool expected)
    {
        var fixture = new Fixture();
        var block = new ConditionalMaterial(1, allowed);
        Assert.Equal(expected, ChiselMaterialCandidates.IsValidMaterial(fixture.Api.Object, fixture.Player.Object, fixture.Position, block));
        Assert.Equal(calls, block.Calls);
        var secondRejects = new ConditionalMaterial(2, true) { RejectSecond = true };
        Assert.False(ChiselMaterialCandidates.IsValidMaterial(fixture.Api.Object, fixture.Player.Object, fixture.Position, secondRejects));
        Assert.Equal(2, secondRejects.Calls);
        fixture.Api.Verify(api => api.TriggerIngameError(It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>Offers replenishment and rejects full/zero-volume ordinary materials without mutating supply.</summary>
    [Fact]
    public void Eligibility_OrdinaryCapacityIsReadOnly()
    {
        var fixture = new Fixture();
        var block = new MaterialBlock(1);
        var stack = new ItemStack(block, 7);
        fixture.Target.BlockIds = [1];
        fixture.Target.AvailMaterialQuantities = [4095];
        Assert.True(fixture.Eligible(stack));
        Assert.Equal((ushort)4095, fixture.Target.AvailMaterialQuantities[0]);
        fixture.Target.AvailMaterialQuantities[0] = 4096;
        Assert.False(fixture.Eligible(stack));
        Assert.Equal((ushort)4096, fixture.Target.AvailMaterialQuantities[0]);
        fixture.Target.BlockIds = [];
        fixture.Target.AvailMaterialQuantities = [];
        block.Boxes = [];
        Assert.False(fixture.Eligible(stack));
        Assert.Empty(fixture.Target.BlockIds);
        Assert.Equal(7, stack.StackSize);
    }

    /// <summary>Requires safely resolvable composite contributions and preserves their source attributes.</summary>
    [Fact]
    public void Eligibility_CompositeRejectsMalformedAndNoncontributingStacks()
    {
        var fixture = new Fixture();
        var material = new MaterialBlock(2);
        fixture.World.Setup(world => world.GetBlock(2)).Returns(material);
        var stack = new ItemStack(new BlockChisel { BlockId = 10, Code = new AssetLocation("chiseledblock") });
        Assert.False(fixture.Eligible(stack));
        stack.Attributes["materials"] = new IntArrayAttribute([2]);
        Assert.False(fixture.Eligible(stack));
        stack.Attributes["availMaterialQuantities"] = new IntArrayAttribute(Array.Empty<int>());
        Assert.False(fixture.Eligible(stack));
        stack.Attributes["availMaterialQuantities"] = new IntArrayAttribute([0]);
        Assert.False(fixture.Eligible(stack));
        stack.Attributes["availMaterialQuantities"] = new IntArrayAttribute([-1]);
        Assert.False(fixture.Eligible(stack));
        stack.Attributes["availMaterialQuantities"] = new IntArrayAttribute([65536]);
        Assert.False(fixture.Eligible(stack));
        stack.Attributes["availMaterialQuantities"] = new IntArrayAttribute([64]);
        Assert.True(fixture.Eligible(stack));
        fixture.Target.BlockIds = [2];
        fixture.Target.AvailMaterialQuantities = [4096];
        Assert.False(fixture.Eligible(stack));
        fixture.Target.AvailMaterialQuantities[0] = 4095;
        Assert.True(fixture.Eligible(stack));
        Assert.Equal([64], ((IntArrayAttribute)stack.Attributes["availMaterialQuantities"]).value);
        Assert.Equal((ushort)4095, fixture.Target.AvailMaterialQuantities[0]);
        stack.Attributes["materials"] = new IntArrayAttribute([999]);
        Assert.False(fixture.Eligible(stack));
    }

    #endregion
    #region Discovery
    /// <summary>Enumerates only carried native storage and retains separate attribute-sensitive stacks.</summary>
    [Fact]
    public void Enumerate_UsesOwnedStorageAndSnapshotIdentity()
    {
        var fixture = new Fixture();
        var block = new MaterialBlock(1);
        foreach (ItemSlot slot in fixture.Hotbar) slot.Itemstack = new ItemStack(block, 4);
        foreach (ItemSlot slot in fixture.Backpack) slot.Itemstack = new ItemStack(block, 5);
        fixture.External[0].Itemstack = new ItemStack(block, 99);
        fixture.Hotbar[0].Itemstack!.Attributes.SetString("marker", "original");
        var candidates = ChiselMaterialCandidates.Enumerate(fixture.Api.Object, fixture.Player.Object, fixture.Position, fixture.Target);
        Assert.Equal(4, candidates.Count);
        Assert.Equal([0, 1, 2, 1], candidates.Select(candidate => candidate.SlotIndex));
        Assert.Equal([fixture.Hotbar, fixture.Hotbar, fixture.Hotbar, fixture.Backpack], candidates.Select(candidate => candidate.Inventory));
        Assert.Same(fixture.Hotbar[0].Itemstack, candidates[0].SourceStack);
        Assert.NotSame(candidates[0].SourceStack, candidates[0].Stack);
        fixture.Hotbar[0].Itemstack!.Attributes.SetString("marker", "changed");
        Assert.Equal("original", candidates[0].Stack.Attributes.GetString("marker"));
        Assert.Equal(99, fixture.External[0].StackSize);
        fixture.Config.SetString("microblockChiseling", "off");
        Assert.Empty(ChiselMaterialCandidates.Enumerate(fixture.Api.Object, fixture.Player.Object, fixture.Position, fixture.Target));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Provides player-owned native slot kinds and independent external storage.</summary>
    private sealed class Fixture
    {
        public Mock<ICoreClientAPI> Api { get; } = new();
        public Mock<IClientWorldAccessor> World { get; } = new();
        public Mock<IClientPlayer> Player { get; } = new Mock<ServerPlayer>((ServerMain)null!, new ServerWorldPlayerData()).As<IClientPlayer>();
        public TreeAttribute Config { get; } = new();
        public BlockPos Position { get; } = new(1, 2, 3);
        public BlockEntityChisel Target { get; } = new() { BlockIds = [], AvailMaterialQuantities = [] };
        public EnumGameMode GameMode { get; set; } = EnumGameMode.Survival;
        public InventoryGeneric Hotbar { get; } = new(3, "hotbar", "test", null!, (i, inv) => i == 2 ? new ItemSlotOffhand(inv) : new ItemSlotSurvival(inv));
        public InventoryGeneric Backpack { get; } = new(2, "backpack", "test", null!, (i, inv) => i == 0 ? new ItemSlotBackpack(inv) : new ItemSlotBagContent(inv, 0, i, EnumItemStorageFlags.General));
        public InventoryGeneric External { get; } = new(1, "chest", "test", null!);

        /// <summary>Wires current world config, game mode and owned inventory membership.</summary>
        public Fixture()
        {
            Config.SetString("microblockChiseling", "all");
            World.Setup(world => world.Config).Returns(Config);
            World.Setup(world => world.BlockAccessor).Returns(new Mock<IBlockAccessor>().Object);
            Api.As<ICoreAPI>().Setup(api => api.World).Returns(World.Object);
            Api.Setup(api => api.World).Returns(World.Object);
            var data = new Mock<IWorldPlayerData>();
            data.Setup(player => player.CurrentGameMode).Returns(() => GameMode);
            Player.Setup(player => player.WorldData).Returns(data.Object);
            var manager = new Mock<IPlayerInventoryManager>();
            manager.Setup(value => value.GetOwnInventory(GlobalConstants.hotBarInvClassName)).Returns(Hotbar);
            manager.Setup(value => value.GetOwnInventory(GlobalConstants.backpackInvClassName)).Returns(Backpack);
            manager.Setup(value => value.OffhandHotbarSlot).Returns(Hotbar[2]);
            Player.Setup(player => player.InventoryManager).Returns(manager.Object);
        }

        /// <summary>Evaluates a stack at the captured target without applying an addition.</summary>
        public bool Eligible(ItemStack stack) => ChiselMaterialCandidates.IsEligible(Api.Object, Player.Object, Position, Target, stack);
    }

    /// <summary>Supplies deterministic collision volume without a game world.</summary>
    private class MaterialBlock : Block
    {
        public Cuboidf[] Boxes { get; set; } = [Cuboidf.Default()];
        /// <summary>Creates a cubic stone material with a stable identity.</summary>
        public MaterialBlock(int id)
        {
            BlockId = id;
            Code = new AssetLocation("material" + id);
            BlockMaterial = EnumBlockMaterial.Stone;
            DrawType = EnumDrawType.Cube;
        }
        /// <summary>Returns controlled material contribution volume.</summary>
        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor accessor, BlockPos pos) => Boxes;
    }

    /// <summary>Models the conditional interface's observable native call count.</summary>
    private sealed class ConditionalMaterial(int id, bool allowed) : MaterialBlock(id), IConditionalChiselable
    {
        public int Calls { get; private set; }
        public bool RejectSecond { get; init; }
        /// <summary>Returns controlled eligibility while counting native conditional checks.</summary>
        public bool CanChisel(IWorldAccessor world, BlockPos pos, IPlayer player, out string errorCode)
        {
            Calls++;
            errorCode = "fixture-rejection";
            return allowed && !(RejectSecond && Calls == 2);
        }
    }
    #endregion
}
