using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Owns one native popup surface independently of host hit-testing and feature configuration.</summary>
internal interface IHudMenuPopup : IDisposable
{
    #region Public API
    #region State and rendering
    /// <summary>Gets the actual visible physical bounds, excluding native anchor rows.</summary>
    RectangleF VisibleBounds { get; }
    /// <summary>Gets whether native focus and popup state remain active.</summary>
    bool IsOpened { get; }
    /// <summary>Renders the prepared native controls.</summary>
    void Draw(float deltaTime);
    #endregion
    #region Pointer delegation
    /// <summary>Delegates a pointer press within the visible popup.</summary>
    void MouseDown(MouseEvent args);
    /// <summary>Delegates release, including a captured scrollbar release.</summary>
    void MouseUp(MouseEvent args);
    /// <summary>Delegates hover and scrollbar movement.</summary>
    void MouseMove(MouseEvent args);
    /// <summary>Delegates scrolling within the popup viewport.</summary>
    void MouseWheel(MouseWheelEventArgs args);
    #endregion
    #region Keyboard delegation
    /// <summary>Delegates navigation and activation keys.</summary>
    void KeyDown(KeyEvent args);
    /// <summary>Delegates native type-to-select behavior.</summary>
    void KeyPress(KeyEvent args);
    /// <summary>Commits completed numeric editing before an outside click dismisses the surface.</summary>
    void CommitPendingInput() { }
    #endregion
    #endregion
}
