namespace VanillaExpanded.HudOverlays.Anchoring;

/// <summary>Owns canonical external target identities and compatibility with previously saved native attachment IDs.</summary>
internal static class HudOverlayTargets
{
    public const string Screen = "screen";
    public const string Hotbar = "hotbar";
    public const string Saturation = "saturation";
    public const string Health = "health";
    public const string TemporalStability = "temporal-stability";
    public const string HealthElement = "healthstatbar";
    public const string SaturationElement = "saturationstatbar";

    #region Public API
    /// <summary>Maps saved native-prefixed aliases to the same identities used by registered placements.</summary>
    public static string Canonical(string id) => id switch
    {
        "native:hotbar" => Hotbar,
        "native:saturationstatbar" => Saturation,
        "native:healthstatbar" => Health,
        "native:temporal-stability" => TemporalStability,
        _ => id
    };

    /// <summary>Identifies supported persisted external targets without accepting arbitrary native GUI keys.</summary>
    public static bool IsExternal(string id) => Canonical(id) is Screen or Hotbar or Saturation or Health or TemporalStability;

    /// <summary>Returns the boundary identity for a canonical target or previously saved alias.</summary>
    public static string? Boundary(string target) => Canonical(target) switch
    {
        Hotbar => Hotbar,
        Saturation => SaturationElement,
        Health => HealthElement,
        TemporalStability => TemporalStability,
        _ => null
    };

    /// <summary>Converts a discovered native boundary identity into its canonical placement target.</summary>
    public static string Target(string boundary) => boundary switch
    {
        SaturationElement => Saturation,
        HealthElement => Health,
        _ => boundary
    };
    #endregion
}
