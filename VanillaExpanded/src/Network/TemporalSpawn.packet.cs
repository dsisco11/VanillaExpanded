using ProtoBuf;

namespace VanillaExpanded.Network;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class Packet_TemporalSpawn
{
    /// <summary> Whether the player has an active temporal spawn point. </summary>
    public bool HasSpawn { get; set; }

    /// <summary> X coordinate of the spawn position. </summary>
    public double X { get; set; }

    /// <summary> Y coordinate of the spawn position. </summary>
    public double Y { get; set; }

    /// <summary> Z coordinate of the spawn position. </summary>
    public double Z { get; set; }
}
