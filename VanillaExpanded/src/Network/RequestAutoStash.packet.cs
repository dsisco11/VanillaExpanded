using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_RequestAutoStash
{
    public required BlockPos position;
}
