using ProtoBuf;

namespace VanillaExpanded.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_RequestEntityAutoStash
{
    public long EntityId;
    public int AttachmentSlotIndex;
}