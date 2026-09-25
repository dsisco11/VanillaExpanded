using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Owns one open menu's hover, selection, and cancellation state.</summary>
public sealed class RadialMenuInteraction
{
    #region State
    private RadialMenuLayout layout;
    private readonly Dictionary<string, RadialMenuEntry> entries = new(StringComparer.Ordinal);
    private bool isOpen;
    private bool isFinished;
    #endregion

    #region Public API
    /// <summary>Creates interaction state for a fixed layout and its current content.</summary>
    public RadialMenuInteraction(RadialMenuLayout layout, IEnumerable<RadialMenuEntry> entries)
    {
        this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
        UpdateEntries(entries);
    }

    /// <summary>Reports one enabled selected identifier.</summary>
    public event Action<string>? Selected;
    /// <summary>Reports a close without selection once.</summary>
    public event Action? Cancelled;
    /// <summary>Reports a changed hover target, including transitions to or from no target.</summary>
    public event Action<string?>? HoverChanged;
    /// <summary>Gets whether the interaction is currently open.</summary>
    public bool IsOpen => isOpen;
    /// <summary>Gets the current hovered identifier, including disabled entries.</summary>
    public string? HoveredId { get; private set; }
    /// <summary>Gets the selected identifier until a new interaction opens.</summary>
    public string? SelectedId { get; private set; }

    /// <summary>Replaces content while preserving layout geometry and identifiers.</summary>
    public void UpdateEntries(IEnumerable<RadialMenuEntry> newEntries)
    {
        ArgumentNullException.ThrowIfNull(newEntries);
        var replacement = new Dictionary<string, RadialMenuEntry>(StringComparer.Ordinal);
        foreach (RadialMenuEntry entry in newEntries)
        {
            if (!replacement.TryAdd(entry.Id, entry)) throw new ArgumentException("Duplicate entry identifier.", nameof(newEntries));
        }

        // Every fixed wedge and the center keep a visible entry even when unavailable.
        if (replacement.Count != layout.WedgeIds.Count + 1 || !replacement.ContainsKey(layout.CenterId)) throw new ArgumentException("Content must cover the complete layout.", nameof(newEntries));
        foreach (string id in layout.WedgeIds)
        {
            if (!replacement.ContainsKey(id)) throw new ArgumentException("Content must cover the complete layout.", nameof(newEntries));
        }

        entries.Clear();
        foreach (var pair in replacement) entries.Add(pair.Key, pair.Value);
    }

    /// <summary>Replaces the current geometry and content as one interaction update.</summary>
    public void UpdateLayout(RadialMenuLayout nextLayout, IEnumerable<RadialMenuEntry> newEntries)
    {
        ArgumentNullException.ThrowIfNull(nextLayout);
        var replacement = new RadialMenuInteraction(nextLayout, newEntries);
        layout = nextLayout;
        entries.Clear();
        foreach (string id in nextLayout.WedgeIds) entries.Add(id, replacement.GetEntry(id));
        entries.Add(nextLayout.CenterId, replacement.GetEntry(nextLayout.CenterId));
        HoveredId = null;
    }

    /// <summary>Gets content by its stable identifier.</summary>
    public RadialMenuEntry GetEntry(string id) => entries[id];

    /// <summary>Begins one interaction; a caller owns the hold and reopen latch.</summary>
    public void Open()
    {
        if (isOpen) throw new InvalidOperationException("The menu is already open.");
        isOpen = true;
        isFinished = false;
        HoveredId = null;
        SelectedId = null;
    }

    /// <summary>Updates the hovered identifier using the same coordinates as the renderer.</summary>
    public void MovePointer(double x, double y, double centerX, double centerY, double radiusPixels)
    {
        if (!isOpen) return;
        string? nextHoveredId = layout.HitTest(x, y, centerX, centerY, radiusPixels);
        if (nextHoveredId == HoveredId) return;
        HoveredId = nextHoveredId;
        HoverChanged?.Invoke(nextHoveredId);
    }

    /// <summary>Selects the enabled hovered entry at most once and closes the interaction.</summary>
    public bool SelectHovered()
    {
        if (!isOpen || isFinished || HoveredId is null || !entries[HoveredId].Enabled) return false;
        string selected = HoveredId;
        SelectedId = selected;
        isFinished = true;
        isOpen = false;
        HoveredId = null;
        Selected?.Invoke(selected);
        return true;
    }

    /// <summary>Cancels an unfinished interaction exactly once.</summary>
    public void Cancel()
    {
        if (!isOpen || isFinished) return;
        isFinished = true;
        isOpen = false;
        HoveredId = null;
        Cancelled?.Invoke();
    }
    #endregion
}

