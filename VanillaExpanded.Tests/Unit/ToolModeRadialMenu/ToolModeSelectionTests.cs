using VanillaExpanded.ToolModeRadialMenu;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace VanillaExpanded.Tests.Unit.ToolModeRadialMenu;

[Trait("Category", "Unit")]
public sealed class ToolModeSelectionTests
{
    [Fact]
    public void CreatePacket_ReproducesBaseToolModeEnvelopeAndSelection()
    {
        var selection = new BlockSelection
        {
            Position = new BlockPos(10, 20, 30),
            Face = BlockFacing.EAST,
            SelectionBoxIndex = 4,
            HitPosition = new Vec3d(0.25, 0.5, 0.75)
        };

        Packet_Client packet = ToolModeSelection.CreatePacket(6, selection);

        Assert.Equal(27, packet.Id);
        Assert.Equal(6, packet.ToolMode.Mode);
        Assert.Equal(10, packet.ToolMode.X);
        Assert.Equal(20, packet.ToolMode.Y);
        Assert.Equal(30, packet.ToolMode.Z);
        Assert.Equal(BlockFacing.EAST.Index, packet.ToolMode.Face);
        Assert.Equal(4, packet.ToolMode.SelectionBoxIndex);
        Assert.Equal(CollectibleNet.SerializeDouble(0.25), packet.ToolMode.HitX);
        Assert.Equal(CollectibleNet.SerializeDouble(0.5), packet.ToolMode.HitY);
        Assert.Equal(CollectibleNet.SerializeDouble(0.75), packet.ToolMode.HitZ);
    }

    [Fact]
    public void CreatePacket_UsesDefaultSelectionFieldsWithoutBlockTarget()
    {
        Packet_Client packet = ToolModeSelection.CreatePacket(2, null);

        Assert.Equal(27, packet.Id);
        Assert.Equal(2, packet.ToolMode.Mode);
        Assert.Equal(0, packet.ToolMode.X);
        Assert.Equal(0, packet.ToolMode.Face);
    }
}