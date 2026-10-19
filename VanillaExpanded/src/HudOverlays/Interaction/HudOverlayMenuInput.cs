using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Resolves HUD menu targets and host eligibility without owning feature configuration or layout policy.</summary>
internal sealed class HudOverlayMenuInput : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly GuiDialog host;
    private readonly HudOverlayContainers containers;
    private readonly Func<HudOverlayRegistration, bool> drawable;
    private readonly HudOverlayContextMenu menu;
    private readonly Func<bool> enabled;
    private IReadOnlyList<HudOverlayGroupLayout> groups = Array.Empty<HudOverlayGroupLayout>();
    private GuiDialog? previousFocus;
    private bool ownsFocus;
    private readonly HashSet<Vintagestory.API.Common.EnumMouseButton> pressedButtons = new();
    private readonly HashSet<int> pressedKeys = new();
    private bool disposed;
    #region Public API
    #region Construction and targeting
    /// <summary>Borrows layout/feature decisions and owns the supplied session menu controller.</summary>
    public HudOverlayMenuInput(ICoreClientAPI api, GuiDialog host, HudOverlayContainers containers,
        Func<HudOverlayRegistration, bool> drawable, HudOverlayContextMenu? menu = null, Func<bool>? enabled = null)
    {
        this.api = api; this.host = host; this.containers = containers; this.drawable = drawable;
        this.menu = menu ?? new HudOverlayContextMenu(api);
        this.enabled = enabled ?? (() => VanillaExpandedModSystem.Config.EnableHudOverlays);
    }
    /// <summary>Gets popup input ownership after synchronizing current host conditions.</summary>
    public bool IsOpened { get { SynchronizeEnvironment(); return menu.IsOpened; } }
    /// <summary>Retains native input ownership until matching releases for consumed presses have been swallowed.</summary>
    public bool Capturing { get { SynchronizeEnvironment(); return menu.IsOpened || pressedButtons.Count > 0 || pressedKeys.Count > 0; } }
    /// <summary>Replaces target geometry in actual content draw order, including editable stand-ins.</summary>
    public void Synchronize(IReadOnlyList<HudOverlayGroupLayout> layouts)
    {
        groups = layouts;
        SynchronizeEnvironment();
    }
    /// <summary>Opens only unclaimed right presses over the topmost eligible clipped member.</summary>
    public bool TryOpen(MouseEvent args)
    {
        if (args.Handled || args.Button != Vintagestory.API.Common.EnumMouseButton.Right) return false;
        var match = FindTarget(new(args.X, args.Y));
        if (match == null) return false;
        var owner = containers.Owner(match.Registration.Id);
        if (owner == null) return false;
        var movement = containers.Get(owner.Id, match.Registration.Id);
        var registration = match.Registration;
        var target = new HudMenuTarget(registration, () => groups.Any(layout => layout.Available && layout.Members.Any(member =>
            ReferenceEquals(member.Registration, registration) && drawable(registration))), () => movement.Locked,
            value => SetLocked(registration, value), !containers.ReadOnly,
            containers.ReadOnly ? null : () => containers.ResetIndicator(registration.Id));
        if (!menu.Open(target, HudMenuPage.Toggles, new(args.X, args.Y), Environment())) return false;
        // Opening is separate from native row activation. Taking native focus gives Escape priority over inventory windows.
        // Reopening while an earlier consumed key is still held belongs to the same focus lease.
        // Keep its original return target instead of replacing it with the already-focused HUD.
        if (!ownsFocus)
            previousFocus = api.Gui.OpenedGuis?.LastOrDefault(dialog => !ReferenceEquals(dialog, host) && dialog.IsOpened() && dialog.Focused);
        ownsFocus = true;
        pressedButtons.Add(args.Button);
        api.Gui.RequestFocus(host);
        args.Handled = true;
        return true;
    }
    /// <summary>Finds the topmost editable indicator under the cursor, excluding popup and foreground-window ownership.</summary>
    public HudOverlayMemberLayout? HoveredMember() => menu.IsOpened ? null : FindTarget(new(api.Input.MouseX, api.Input.MouseY));
    #endregion
    #region Rendering and pointer delegation
    /// <summary>Draws the prepared popup after indicator content and chrome.</summary>
    public void Draw(float deltaTime) { SynchronizeEnvironment(); if (menu.IsOpened) menu.Draw(deltaTime); }
    /// <summary>Routes popup presses before drag initiation, yielding to foreground windows at that position.</summary>
    public void MouseDown(MouseEvent args)
    {
        SynchronizeEnvironment();
        if (!menu.IsOpened)
        {
            // Pending releases still belong to us, but must not prevent a new context-menu gesture.
            // In particular, the cursor-unlock modifier can remain held across multiple selections.
            if (TryOpen(args)) return;
            if (pressedButtons.Count > 0 || pressedKeys.Count > 0) { pressedButtons.Add(args.Button); args.Handled = true; }
            return;
        }
        if (args.Handled || Blocked(new(args.X, args.Y))) { menu.Close(); return; }
        pressedButtons.Add(args.Button);
        menu.OnMouseDown(args);
    }
    /// <summary>Routes popup release while its controller still owns the interaction.</summary>
    public void MouseUp(MouseEvent args)
    {
        SynchronizeEnvironment();
        bool owned = pressedButtons.Remove(args.Button);
        if (menu.IsOpened) menu.OnMouseUp(args);
        args.Handled |= owned;
        if (!menu.IsOpened) RestoreFocus();
    }
    /// <summary>Keeps popup hover independent of indicator hover.</summary>
    public void MouseMove(MouseEvent args)
    {
        SynchronizeEnvironment();
        if (menu.IsOpened) menu.OnMouseMove(args);
        else if (pressedButtons.Count > 0) args.Handled = true;
    }
    /// <summary>Routes popup scrolling without changing underlying gameplay selections.</summary>
    public void MouseWheel(MouseWheelEventArgs args)
    {
        SynchronizeEnvironment();
        if (menu.IsOpened) menu.OnMouseWheel(args);
        else if (pressedButtons.Count > 0 || pressedKeys.Count > 0) args.SetHandled();
    }
    #endregion
    #region Keyboard delegation
    /// <summary>Routes native keyboard navigation and local Escape dismissal.</summary>
    public void KeyDown(KeyEvent args)
    {
        SynchronizeEnvironment();
        if (!menu.IsOpened && pressedButtons.Count == 0 && pressedKeys.Count == 0) return;
        pressedKeys.Add(args.KeyCode);
        if (menu.IsOpened) menu.OnKeyDown(args);
        else args.Handled = true;
    }
    /// <summary>Consumes matching releases even when the setting selection or Escape already closed the popup.</summary>
    public void KeyUp(KeyEvent args)
    {
        SynchronizeEnvironment();
        args.Handled |= pressedKeys.Remove(args.KeyCode) || menu.IsOpened;
        if (!menu.IsOpened) RestoreFocus();
    }
    /// <summary>Cancels the popup through the native Escape hook while retaining the corresponding key release.</summary>
    public void Escape()
    {
        pressedKeys.Add((int)GlKeys.Escape);
        menu.Close();
    }
    /// <summary>Routes native type-to-select characters.</summary>
    public void KeyPress(KeyEvent args)
    {
        SynchronizeEnvironment();
        if (menu.IsOpened) menu.OnKeyPress(args);
        else if (pressedButtons.Count > 0 || pressedKeys.Count > 0) args.Handled = true;
    }
    #endregion
    #region Lifetime
    /// <summary>Ends transient popup ownership on host focus loss or closure.</summary>
    public void Close(bool restoreFocus = true)
    {
        menu.Close();
        if (restoreFocus) RestoreFocus();
        else { ownsFocus = false; previousFocus = null; pressedButtons.Clear(); pressedKeys.Clear(); }
    }
    /// <summary>Retires the session controller and releases references to feature registrations.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pressedButtons.Clear(); pressedKeys.Clear();
        groups = Array.Empty<HudOverlayGroupLayout>();
        try { menu.Dispose(); }
        finally { RestoreFocus(); }
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Shares clipped content targeting between context menus and the edit-mode hover cue.</summary>
    private HudOverlayMemberLayout? FindTarget(PointF pointer)
    {
        if (!Environment().EditingAvailable || Blocked(pointer)) return null;
        return groups.Where(group => group.Available).SelectMany(group => group.Members).Reverse()
            .FirstOrDefault(member => drawable(member.Registration) && member.Clip.Contains(pointer));
    }

    /// <summary>Captures the latest displayed owner origin before the existing permission operation persists it.</summary>
    private void SetLocked(HudOverlayRegistration registration, bool locked)
    {
        var layout = groups.FirstOrDefault(group => group.Available && group.Members.Any(member => ReferenceEquals(member.Registration, registration)));
        var owner = containers.Owner(registration.Id);
        if (layout == null || owner == null) return;
        var movement = containers.Get(owner.Id, registration.Id);
        var window = api.Gui.WindowBounds;
        movement.ContainerOrigin = new((float)((layout.Root.renderX - window.renderX) / RuntimeEnv.GUIScale),
            (float)((layout.Root.renderY - window.renderY) / RuntimeEnv.GUIScale));
        movement.SetLocked(locked);
    }
    /// <summary>Builds current native viewport and lifecycle conditions without depending on pointer hover.</summary>
    private HudMenuEnvironment Environment()
    {
        var window = api.Gui.WindowBounds;
        bool modal = api.Gui.OpenedGuis?.Any(dialog => !ReferenceEquals(dialog, host) && dialog.IsOpened() && dialog.CaptureAllInputs()) == true;
        return new(host, new((float)window.renderX, (float)window.renderY, (float)window.OuterWidth, (float)window.OuterHeight),
            RuntimeEnv.GUIScale, Lang.CurrentLocale ?? "en", !disposed && host.IsOpened() && enabled() && !api.HideGuis && !api.Input.MouseGrabbed && !modal);
    }
    /// <summary>Closes stale popups before each input or drawing boundary.</summary>
    private void SynchronizeEnvironment()
    {
        if (disposed) return;
        var environment = Environment();
        if (!environment.EditingAvailable) { pressedButtons.Clear(); pressedKeys.Clear(); }
        menu.Synchronize(environment);
        if (!menu.IsOpened) RestoreFocus();
    }
    /// <summary>Returns focus to the still-open previous window without stealing it from a newly focused dialog.</summary>
    private void RestoreFocus()
    {
        if (!ownsFocus || pressedButtons.Count > 0 || pressedKeys.Count > 0) return;
        ownsFocus = false;
        var previous = previousFocus;
        previousFocus = null;
        if (!host.Focused) return;
        if (previous is { Focusable: true } && previous.IsOpened()) api.Gui.RequestFocus(previous);
        else host.UnFocus();
    }
    /// <summary>Defers to foreground windows over their native bounds and to modal ownership everywhere.</summary>
    private bool Blocked(PointF pointer)
    {
        return (api.Gui.OpenedGuis ?? Enumerable.Empty<GuiDialog>()).Any(dialog => !ReferenceEquals(dialog, host) && dialog.IsOpened()
            && (dialog.CaptureAllInputs() || dialog.DialogType != EnumDialogType.HUD && dialog.Composers.Values.Any(composer =>
                composer.Enabled && composer.Bounds.PointInside(pointer.X, pointer.Y))));
    }
    #endregion
}
