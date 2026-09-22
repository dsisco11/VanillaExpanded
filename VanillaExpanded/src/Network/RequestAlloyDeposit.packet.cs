using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class Packet_RequestAlloyDeposit
{
    public required string RequestId;
    public required BlockPos Position;
    public required string AlloyCode;
    public required int[] SlotIndices;
    public required string[] SlotIngredientCodes;
    public required int[] SlotAmounts;
}
