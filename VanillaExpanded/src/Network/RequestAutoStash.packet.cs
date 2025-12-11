using ProtoBuf;

using Vintagestory.API.MathTools;

namespace VanillaExpanded.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_RequestAutoStash
{
    /// <summary> The position of the container to stash into. </summary>
    public required BlockPos position;
}
