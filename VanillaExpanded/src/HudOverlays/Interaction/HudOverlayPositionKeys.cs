using System;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Defines disjoint native settings namespaces without changing registered identities.</summary>
internal static class HudOverlayPositionKeys
{
    #region Public API
    /// <summary>Builds a stable native group dialog key.</summary>
    public static string Group(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return "vanillaexpanded:hud:group:" + id;
    }

    /// <summary>Builds an independent indicator key, reserved for individual placement.</summary>
    public static string Indicator(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return "vanillaexpanded:hud:indicator:" + id;
    }
    #endregion
}
