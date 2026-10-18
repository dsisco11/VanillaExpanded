using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Owns one prepared native popup and its stable target, leaving host hit-testing and activation policy to the caller.</summary>
internal sealed class HudOverlayContextMenu : IDisposable
{
    private readonly Func<HudMenuPage, IReadOnlyList<HudMenuEntry>, Action<string, bool>, PointF, HudMenuEnvironment, IHudMenuPopup?> create;
    private readonly Func<string, string, string> localize;
    private IHudMenuPopup? popup;
    private HudMenuTarget? target;
    private HudMenuEnvironment? environment;
    private HudMenuPage page;
    private HudMenuEntry[] entries = Array.Empty<HudMenuEntry>();
    private bool dispatching, closeRequested, disposed;

    #region Public API
    #region Construction and lifetime
    /// <summary>Creates a shared native surface owner with injectable graphics and localization boundaries.</summary>
    public HudOverlayContextMenu(ICoreClientAPI api,
        Func<HudMenuPage, IReadOnlyList<HudMenuEntry>, Action<string, bool>, PointF, HudMenuEnvironment, IHudMenuPopup?>? create = null,
        Func<string, string, string>? localize = null)
    {
        this.create = create ?? new HudMenuPopupFactory(api).Create;
        this.localize = localize ?? ((locale, key) => Lang.GetL(locale, key));
    }

    /// <summary>Gets whether a prepared popup is owned; callers still synchronize host conditions before routing.</summary>
    public bool IsOpened => popup != null;
    /// <summary>Gets the registration held independently of pointer hover.</summary>
    public string? TargetId => target?.Registration.Id;
    /// <summary>Opens an explicit page at physical pointer coordinates; it does not route the initiating click.</summary>
    public bool Open(HudMenuTarget nextTarget, HudMenuPage nextPage, PointF pointer, HudMenuEnvironment context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Close();
        if (!context.EditingAvailable || !nextTarget.IsAvailable() || !double.IsFinite(context.GuiScale) || context.GuiScale <= 0) return false;
        target = nextTarget; environment = context; page = nextPage;
        try
        {
            entries = ReadEntries();
            if (entries.Length == 0) { Close(); return false; }
            popup = create(page, entries, Select, pointer, context);
        }
        catch { Close(); throw; }
        if (popup == null) { Close(); return false; }
        return true;
    }

    /// <summary>Closes stale surfaces instead of recomposing on every frame or applying callbacks to retired targets.</summary>
    public void Synchronize(HudMenuEnvironment context)
    {
        if (popup == null) return;
        try
        {
            if (!context.EditingAvailable || environment == null || !ReferenceEquals(context.Session, environment.Session)
                || context.Viewport != environment.Viewport || context.GuiScale != environment.GuiScale || context.Locale != environment.Locale
                || target?.IsAvailable() != true || !popup.IsOpened || !entries.SequenceEqual(ReadEntries())) Close();
        }
        catch { Close(); throw; }
    }

    /// <summary>Releases focus and owned graphics; callback-triggered closure waits until native dispatch returns.</summary>
    public void Close()
    {
        if (dispatching) { closeRequested = true; return; }
        var retired = popup;
        popup = null; target = null; environment = null; entries = Array.Empty<HudMenuEntry>(); closeRequested = false;
        retired?.Dispose();
    }

    /// <summary>Retires session resources idempotently.</summary>
    public void Dispose() { if (disposed) return; disposed = true; Close(); }
    #endregion

    #region Rendering
    /// <summary>Draws cached native graphics after the caller synchronizes editing conditions.</summary>
    public void Draw(float deltaTime) { if (popup != null) Dispatch(() => popup?.Draw(deltaTime)); }
    #endregion
    #region Pointer delegation
    /// <summary>Dismisses outside clicks and delegates inside presses without allowing click-through.</summary>
    public void OnMouseDown(MouseEvent args)
    {
        if (popup == null) return;
        if (!popup.VisibleBounds.Contains(args.X, args.Y)) Close();
        else Dispatch(() => popup?.MouseDown(args));
        args.Handled = true;
    }
    /// <summary>Releases captured native scrollbar input.</summary>
    public void OnMouseUp(MouseEvent args) { if (popup == null) return; Dispatch(() => popup?.MouseUp(args)); args.Handled = true; }
    /// <summary>Retains menu ownership when pointer motion leaves the indicator or popup.</summary>
    public void OnMouseMove(MouseEvent args) { if (popup == null) return; Dispatch(() => popup?.MouseMove(args)); args.Handled = true; }
    /// <summary>Routes scrolling to the owned native popup.</summary>
    public void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (popup == null) return;
        Dispatch(() => popup?.MouseWheel(args));
        args.SetHandled();
    }
    #endregion
    #region Keyboard delegation
    /// <summary>Dismisses Escape locally or delegates native navigation and activation.</summary>
    public void OnKeyDown(KeyEvent args)
    {
        if (popup == null) return;
        if (args.KeyCode == (int)GlKeys.Escape) Close();
        else Dispatch(() => popup?.KeyDown(args));
        args.Handled = true;
    }
    /// <summary>Routes type-to-select to the native style list.</summary>
    public void OnKeyPress(KeyEvent args) { if (popup == null) return; Dispatch(() => popup?.KeyPress(args)); args.Handled = true; }
    #endregion
    #endregion

    #region Private
    /// <summary>Builds current localized rows without caching mutable configuration state.</summary>
    private HudMenuEntry[] ReadEntries()
    {
        if (target == null || environment == null) return Array.Empty<HudMenuEntry>();
        string Label(string suffix) => localize(environment.Locale, "vanillaexpanded:hud-menu-" + suffix);
        var settings = target.Registration.MenuSettings;
        if (page == HudMenuPage.Toggles)
        {
            var rows = new List<HudMenuEntry>();
            if (settings?.Enabled is { } enabled) rows.Add(new("enabled", Label("enabled"), enabled.Value));
            if (target.CanChangeLock) rows.Add(new("lock", Label("lock"), target.IsLocked()));
            rows.AddRange(ReadStyleEntries());
            return rows.ToArray();
        }
        return ReadStyleEntries();
        // Keep enum identities inside the feature binding. Only stable per-popup codes cross native GUI delegates.
        HudMenuEntry[] ReadStyleEntries()
        {
            var style = settings?.Style;
            return style == null ? Array.Empty<HudMenuEntry>() : style.Choices.Select((value, index) =>
                new HudMenuEntry("style:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Label("style-" + value.ToString().ToLowerInvariant()), value.Equals(style.Value), HudMenuEntryKind.StyleChoice)).ToArray();
        }
    }

    /// <summary>Applies only current target settings and defers disposal until native callbacks finish using their controls.</summary>
    private void Select(string code, bool selected)
    {
        if (target == null || target.IsAvailable() != true || !entries.SequenceEqual(ReadEntries())) { Close(); return; }
        try
        {
            if (selected && code.StartsWith("style:", StringComparison.Ordinal)
                && int.TryParse(code.AsSpan(6), out int index) && target.Registration.MenuSettings?.Style is { } style
                && index >= 0 && index < style.Choices.Count) style.Set(style.Choices[index]);
            else if (page == HudMenuPage.Toggles)
            {
                if (code == "enabled") target.Registration.MenuSettings?.Enabled?.Set(selected);
                else if (code == "lock" && target.CanChangeLock) target.SetLocked(selected);
            }
        }
        finally { Close(); }
    }

    /// <summary>Protects native call stacks from synchronous settings notifications disposing their active controls.</summary>
    private void Dispatch(Action action)
    {
        dispatching = true;
        try { action(); }
        catch { Close(); throw; }
        finally
        {
            dispatching = false;
            if (closeRequested || popup?.IsOpened == false) Close();
        }
    }
    #endregion
}
