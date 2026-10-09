using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Exposes generic radial presentation and callbacks independently of its caller's domain.</summary>
public interface IRadialMenu
{
    /// <summary>Gets whether this menu owns an open dialog.</summary>
    bool IsOpen { get; }
    /// <summary>Opens fixed content with an optional dismissal hotkey and reports a selection or cancellation.</summary>
    bool Open(RadialMenuLayout layout, IEnumerable<RadialMenuEntry> entries,
        Func<string, RadialMenuSelectionResult> selected, Action cancelled, string toggleKeyCode = "");
    /// <summary>Updates content without changing layout.</summary>
    void UpdateEntries(IEnumerable<RadialMenuEntry> entries);
    /// <summary>Replaces geometry and content together when the caller's available set changes.</summary>
    void UpdateLayout(RadialMenuLayout layout, IEnumerable<RadialMenuEntry> entries);
    /// <summary>Selects the currently hovered entry if it is enabled.</summary>
    bool SelectHovered();
    /// <summary>Cancels the current dialog and releases its input.</summary>
    void Cancel();
}
