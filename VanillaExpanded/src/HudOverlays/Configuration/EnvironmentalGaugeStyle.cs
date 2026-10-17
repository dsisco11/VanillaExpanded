using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Selects the environmental instrument presentation independently of its sampled reading.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum EnvironmentalGaugeStyle
{
    /// <summary>Detailed copper instruments with mechanical readings.</summary>
    [EnumMember(Value = "immersive")]
    Immersive = 0,
    /// <summary>Compact HUD symbols with ordinary HUD-size readings.</summary>
    [EnumMember(Value = "icon")]
    Icon = 1,
    /// <summary>Uses a short text name and the sampled reading without instrument artwork.</summary>
    [EnumMember(Value = "text")]
    Text = 2
}
