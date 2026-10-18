using ProtoBuf;

namespace VanillaExpanded.EcosystemMap.Transport;

/// <summary>Notifies a supporting server that this connection's client cache was intentionally cleared; carries no region or player identity.</summary>
[ProtoContract]
internal sealed class EcosystemCacheReset
{
    internal const string ChannelName = "vanillaexpanded-ecosystem-cache";
    [ProtoMember(1)]
    public int Version { get; set; } = 1;
}
