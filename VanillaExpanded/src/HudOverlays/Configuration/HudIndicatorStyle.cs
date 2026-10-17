using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Selects artwork or a short text name for a compact HUD indicator.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum HudIndicatorStyle
{
    /// <summary>Uses the indicator's existing artwork.</summary>
    [EnumMember(Value = "icon")]
    Icon = 0,
    /// <summary>Replaces artwork with a short name while retaining sampled readings.</summary>
    [EnumMember(Value = "text")]
    Text = 1
}
