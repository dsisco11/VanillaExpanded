using System;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Immutable overlay metadata; ownership transfers only when the registry accepts it.</summary>
internal sealed record HudOverlayRegistration
{
    public const int DefaultRefreshIntervalMs = 1000;
    public string Id { get; }
    public IHudOverlay Overlay { get; }
    public Func<bool> IsEnabled { get; }
    public string GroupId { get; }
    public int Order { get; }
    public int RefreshIntervalMs { get; }
    #region Public API
    /// <summary>Defines a namespaced overlay, live enabled selector, group, deterministic order and positive interval.</summary>
    public HudOverlayRegistration(string id, IHudOverlay overlay, Func<bool> isEnabled, string groupId, int order = 0,
        int refreshIntervalMs = DefaultRefreshIntervalMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        int separator = id.IndexOf(':');
        if (separator <= 0 || separator == id.Length - 1 || id.IndexOf(':', separator + 1) >= 0 || ContainsWhitespace(id))
            throw new ArgumentException("Overlay IDs must have a nonempty namespace and name separated by one colon.", nameof(id));
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(isEnabled);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (refreshIntervalMs <= 0) throw new ArgumentOutOfRangeException(nameof(refreshIntervalMs));
        Id = id;
        Overlay = overlay;
        IsEnabled = isEnabled;
        GroupId = groupId;
        Order = order;
        RefreshIntervalMs = refreshIntervalMs;
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
