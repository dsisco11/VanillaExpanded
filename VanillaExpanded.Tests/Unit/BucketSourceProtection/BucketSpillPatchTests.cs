using HarmonyLib;
using Moq;
using VanillaExpanded.BucketSourceProtection;
using VanillaExpanded.ModSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.BucketSourceProtection;

/// <summary>Exercises source protection using real container contents and fluid-layer destinations.</summary>
public class BucketSpillPatchTests
{
    #region Public API
    /// <summary>Preserves both fresh and salt sources through either spill destination.</summary>
    [Theory]
    [InlineData(false, "water", "water")]
    [InlineData(true, "water", "saltwater")]
    [InlineData(false, "saltwater", "water")]
    [InlineData(true, "saltwater", "saltwater")]
    public void SourceSpillIsRejectedWithoutConsumption(bool adjacent, string spill, string source)
    {
        var f = Create(adjacent, spill, source, BucketSpillPatch.MaxLiquidLevel);
        bool result = true;
        Assert.NotNull(f.Bucket.GetContentProps(f.Slot.Itemstack!));
        Assert.False(BucketSpillPatch.Prefix(f.Bucket, f.Slot, f.Entity, f.Selection, ref result));
        Assert.False(result);
        Assert.Equal(1000, f.Bucket.GetContent(f.Slot.Itemstack!)!.StackSize);
        f.Accessor.Verify(a => a.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>(), It.IsAny<int>()), Times.Never);
        f.Accessor.Verify(a => a.GetBlock(It.Is<BlockPos>(p => p.Equals(f.Destination)), BlockLayersAccess.Fluid), Times.Once);
    }

    /// <summary>Allows vanilla behavior for flowing destinations, partial contents, and unrestricted worlds.</summary>
    [Theory]
    [InlineData(6, 1000, true)]
    [InlineData(7, 500, true)]
    [InlineData(7, 1000, false)]
    public void UnprotectedSpillsPassThrough(int level, int amount, bool restricted)
    {
        var f = Create(false, "water", "water", level, amount, restricted);
        bool result = true;
        Assert.True(BucketSpillPatch.Prefix(f.Bucket, f.Slot, f.Entity, f.Selection, ref result));
        Assert.True(result);
    }

    /// <summary>Allows empty destinations and leaves non-bucket containers untouched.</summary>
    [Fact]
    public void DryDestinationAndNonBucketPassThrough()
    {
        var f = Create(false, "water", "water", 0);
        bool result = true;
        Assert.True(BucketSpillPatch.Prefix(f.Bucket, f.Slot, f.Entity, f.Selection, ref result));
        Assert.True(BucketSpillPatch.Prefix(new Mock<BlockLiquidContainerBase>().Object, f.Slot, f.Entity, f.Selection, ref result));
    }

    /// <summary>Allows vanilla refusal at a blocked adjacent destination and source-level spill mappings.</summary>
    [Fact]
    public void BlockedDestinationAndSourceMappingPassThrough()
    {
        var f = Create(true, "water", "water", BucketSpillPatch.MaxLiquidLevel);
        f.Accessor.Setup(a => a.GetBlock(It.IsAny<BlockPos>())).Returns(new Block { SideSolid = new SmallBoolArray(SmallBoolArray.OnAllSides) });
        bool result = true;
        Assert.True(BucketSpillPatch.Prefix(f.Bucket, f.Slot, f.Entity, f.Selection, ref result));
        f.Accessor.Verify(a => a.GetBlock(It.IsAny<BlockPos>(), BlockLayersAccess.Fluid), Times.Never);
        f = Create(false, "water", "water", BucketSpillPatch.MaxLiquidLevel);
        f.Bucket.GetContent(f.Slot.Itemstack!)!.Collectible.Attributes = JsonObject.FromJson("""{"waterTightContainerProps":{"itemsPerLitre":100,"allowSpill":true,"whenSpilled":{"action":"PlaceBlock","stack":{"type":"block","code":"water-still-3"},"stackByFillLevel":{"10":{"type":"block","code":"water-still-7"}}}}}""");
        f.World.Setup(w => w.GetBlock(It.Is<AssetLocation>(c => c.Path == "water-still-7"))).Returns(new Block { MatterState = EnumMatterState.Liquid, LiquidCode = "water", LiquidLevel = BucketSpillPatch.MaxLiquidLevel });
        Assert.True(BucketSpillPatch.Prefix(f.Bucket, f.Slot, f.Entity, f.Selection, ref result));
    }

    /// <summary>Rejects through the actual patched engine method before any vanilla effects run.</summary>
    [Fact]
    public void PatchedEngineSpillPreservesContents()
    {
        var f = Create(true, "water", "water", BucketSpillPatch.MaxLiquidLevel);
        bool saved = VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch;
        var system = new BucketSourceProtectionModSystem();
        try
        {
            VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch = true;
            system.OnConfigReloaded(null!);
            Assert.Equal(false, AccessTools.Method(typeof(BlockLiquidContainerBase), "SpillContents").Invoke(f.Bucket, [f.Slot, f.Entity, f.Selection]));
            Assert.Equal(1000, f.Bucket.GetContent(f.Slot.Itemstack!)!.StackSize);
            f.Accessor.Verify(a => a.SetBlock(It.IsAny<int>(), It.IsAny<BlockPos>(), It.IsAny<int>()), Times.Never);
        }
        finally { system.Dispose(); VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch = saved; }
    }

    /// <summary>Verifies actual Harmony registration, idempotent reload, removal, and disposal.</summary>
    [Fact]
    public void EnablementControlsOwnedHarmonyPatch()
    {
        const string owner = "vanillaexpanded.bucketsourceprotection";
        var original = AccessTools.Method(typeof(BlockLiquidContainerBase), "SpillContents");
        bool saved = VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch;
        var system = new BucketSourceProtectionModSystem();
        try
        {
            VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch = true;
            system.OnConfigReloaded(null!);
            system.OnConfigReloaded(null!);
            Assert.Single(Harmony.GetPatchInfo(original).Prefixes, p => p.owner == owner);
            VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch = false;
            system.OnConfigReloaded(null!);
            Assert.DoesNotContain(Harmony.GetPatchInfo(original)?.Owners ?? [], id => id == owner);
            VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch = true;
            system.OnConfigReloaded(null!);
            system.Dispose();
            Assert.DoesNotContain(Harmony.GetPatchInfo(original)?.Owners ?? [], id => id == owner);
        }
        finally { system.Dispose(); VanillaExpandedModSystem.Config.EnableBucketSourceProtectionPatch = saved; }
    }
    #endregion

    #region Private
    /// <summary>Builds a real water-portion stack while mocking only world lookups.</summary>
    private static Fixture Create(bool adjacent, string spill, string source, int level, int amount = 1000, bool restricted = true)
    {
        var bucket = new BlockBucket { Code = new AssetLocation("bucket") };
        var item = new Item { Code = new AssetLocation("waterportion"), Attributes = JsonObject.FromJson("""{"waterTightContainerProps":{"itemsPerLitre":100,"allowSpill":true,"whenSpilled":{"action":"PlaceBlock","stack":{"type":"block","code":"water-still-3"}}}}""") };
        var slot = new ItemSlot(null) { Itemstack = new ItemStack(bucket) };
        bucket.SetContent(slot.Itemstack, new ItemStack(item, amount));
        var world = new Mock<IWorldAccessor>();
        var config = new TreeAttribute();
        config.SetBool("noLiquidSourceTransport", restricted);
        world.Setup(w => w.Config).Returns(config);
        world.Setup(w => w.GetItem(It.IsAny<int>())).Returns(item);
        world.Setup(w => w.GetItem(It.IsAny<AssetLocation>())).Returns(item);
        var accessor = new Mock<IBlockAccessor>();
        world.Setup(w => w.BlockAccessor).Returns(accessor.Object);
        world.Setup(w => w.GetBlock(It.IsAny<AssetLocation>())).Returns(new Block { Code = new AssetLocation(spill + "-still-3"), MatterState = EnumMatterState.Liquid, LiquidCode = spill, LiquidLevel = 3 });
        var api = new Mock<ICoreAPI>();
        api.Setup(a => a.World).Returns(world.Object);
        AccessTools.Field(typeof(CollectibleObject), "api").SetValue(bucket, api.Object);
        var selection = new BlockSelection { Position = new BlockPos(5, 6, 7), Face = BlockFacing.UP };
        var destination = adjacent ? selection.Position.AddCopy(selection.Face) : selection.Position;
        accessor.Setup(a => a.GetBlock(It.IsAny<BlockPos>())).Returns((BlockPos p) => new Block { SideSolid = new SmallBoolArray(adjacent && p.Equals(selection.Position) ? SmallBoolArray.OnAllSides : 0) });
        accessor.Setup(a => a.GetBlock(It.IsAny<BlockPos>(), BlockLayersAccess.Fluid)).Returns(new Block { Code = new AssetLocation(source + "-still-" + level), MatterState = EnumMatterState.Liquid, LiquidCode = source, LiquidLevel = level });
        return new Fixture(bucket, slot, new EntityAgent { World = world.Object }, selection, accessor, destination, world);
    }

    /// <summary>Holds the independently observable spill inputs.</summary>
    private sealed record Fixture(BlockBucket Bucket, ItemSlot Slot, EntityAgent Entity, BlockSelection Selection, Mock<IBlockAccessor> Accessor, BlockPos Destination, Mock<IWorldAccessor> World);
    #endregion
}
