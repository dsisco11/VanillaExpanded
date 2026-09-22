using ProtoBuf;

namespace VanillaExpanded.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class Packet_AlloyDepositResult
{
    public required string RequestId;
    public AlloyDepositResultCode ResultCode;
}
