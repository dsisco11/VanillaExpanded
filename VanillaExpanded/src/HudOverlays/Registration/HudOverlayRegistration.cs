using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Immutable overlay metadata; ownership transfers only when the registry accepts it.</summary>
public sealed record HudOverlayRegistration
{
    public const int DefaultRefreshIntervalMs = 1000;
    public string Id { get; }
    public IHudOverlay Overlay { get; }
    public Func<bool> IsEnabled { get; }
    public string GroupId { get; }
    public int Order { get; }
    public int RefreshIntervalMs { get; }
    /// <summary>Optional localization key explaining this indicator; the host resolves it in the current locale.</summary>
    public string? HoverLabelKey { get; }
    /// <summary>Optional fixed GUI-unit footprint used while drawable; effective containers still collapse hidden members.</summary>
    public SizeF? ReservedSize { get; }
    /// <summary>Optional configured settings; these do not describe gameplay visibility or integration availability.</summary>
    public HudOverlayMenuSettings? MenuSettings { get; }
    #region Public API
    /// <summary>Defines identity, live visibility, ordering, refresh interval, optional hover label and optional fixed layout reservation.</summary>
    public HudOverlayRegistration(string id, IHudOverlay overlay, Func<bool> isEnabled, string groupId, int order = 0,
        int refreshIntervalMs = DefaultRefreshIntervalMs, string? hoverLabelKey = null, SizeF? reservedSize = null)
        : this(id, overlay, isEnabled, groupId, null, order, refreshIntervalMs, hoverLabelKey, reservedSize)
    {
    }

    /// <summary>Defines a registration with optional feature-owned menu capabilities while preserving the original constructor contract.</summary>
    public HudOverlayRegistration(string id, IHudOverlay overlay, Func<bool> isEnabled, string groupId,
        HudOverlayMenuSettings? menuSettings, int order = 0, int refreshIntervalMs = DefaultRefreshIntervalMs,
        string? hoverLabelKey = null, SizeF? reservedSize = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        int separator = id.IndexOf(':');
        if (separator <= 0 || separator == id.Length - 1 || id.IndexOf(':', separator + 1) >= 0 || ContainsWhitespace(id))
            throw new ArgumentException("Overlay IDs must have a nonempty namespace and name separated by one colon.", nameof(id));
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (refreshIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(refreshIntervalMs));
        if (reservedSize is SizeF size && (!float.IsFinite(size.Width) || !float.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0))
            throw new ArgumentOutOfRangeException(nameof(reservedSize), "Reserved dimensions must be finite and positive.");
        Id = id;
        Overlay = overlay;
        IsEnabled = isEnabled;
        GroupId = groupId;
        Order = order;
        RefreshIntervalMs = refreshIntervalMs;
        HoverLabelKey = hoverLabelKey;
        ReservedSize = reservedSize;
        MenuSettings = menuSettings;
    }
    #endregion
    #region Private
    /// <summary>Rejects whitespace anywhere in a stable identity without culture-dependent normalization.</summary>
    private static bool ContainsWhitespace(string id)
    {
        foreach (char character in id) if (char.IsWhiteSpace(character)) return true;
        return false;
    }
    #endregion
}
