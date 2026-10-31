using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Owns native switches and labels directly, avoiding the private switch lifetime in native list-menu multi-select mode.</summary>
internal sealed class HudOverlayTogglePopup : IHudMenuPopup
{
    private readonly ICoreClientAPI api;
    private readonly GuiComposer composer;
    private readonly List<GuiElementSwitch> switches = new();
    private readonly HudOverlayStyleDropDown? style;
    private readonly HudOverlaySizeInput? sizeInput;
    private readonly List<GuiElementTextButton> actions = new();
    private readonly RectangleF panelBounds;
    private bool disposed;
    #region Public API
    #region Construction and state
    /// <summary>Composes normal HUD lettering and native switches, shrinking only when the entire viewport cannot fit the compact panel.</summary>
    public HudOverlayTogglePopup(ICoreClientAPI api, IReadOnlyList<HudMenuEntry> entries, Action<string, bool> select,
        PointF pointer, HudMenuEnvironment environment)
    {
        this.api = api;
        var font = CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text);
        var toggles = entries.Where(entry => entry.Kind == HudMenuEntryKind.Toggle).ToArray();
        var choices = entries.Where(entry => entry.Kind == HudMenuEntryKind.StyleChoice).ToArray();
        var commands = entries.Where(entry => entry.Kind == HudMenuEntryKind.Action).ToArray();
        var sizeEntry = entries.SingleOrDefault(entry => entry.Kind == HudMenuEntryKind.Number);
        string styleLabel = Lang.GetL(environment.Locale, "vanillaexpanded:hud-menu-style");
        double scale = environment.GuiScale;
        double row = Math.Max(30, font.GetFontExtents().Height / scale + 8);
        double width = entries.Max(entry => font.GetTextExtents(entry.Label).Width / scale) + row + 16;
        double styleLabelWidth = font.GetTextExtents(styleLabel).Width / scale + 12;
        if (choices.Length > 0) width += styleLabelWidth;
        double sizeLabelWidth = sizeEntry == null ? 0 : font.GetTextExtents(sizeEntry.Label).Width / scale + 12;
        if (sizeEntry != null) width = Math.Max(width, sizeLabelWidth + 100);
        double height = row * (toggles.Length + (choices.Length > 0 ? 1 : 0) + (sizeEntry == null ? 0 : 1) + commands.Length);
        // Fit the compact panel uniformly only when required. The expanded dropdown scrolls independently.
        double fit = Math.Min(1, Math.Min(environment.Viewport.Width / (width * scale), environment.Viewport.Height / (height * scale)));
        if (!double.IsFinite(fit) || fit <= 0) throw new ArgumentOutOfRangeException(nameof(environment));
        row *= fit; width *= fit; height *= fit;
        font.WithFontSize((float)(HudOverlaySizes.Text * fit));
        panelBounds = HudPopupPlacement.Fit(pointer, new((float)(width * scale), (float)(height * scale)), environment.Viewport);
        var bounds = ElementBounds.Fixed(panelBounds.X / scale, panelBounds.Y / scale, width, height).WithParent(api.Gui.WindowBounds);
        composer = api.Gui.CreateCompo("vanillaexpanded:hud-context-toggles", bounds);
        try
        {
            composer.AddDialogBG(ElementBounds.Fixed(0, 0, width, height), false);
            for (int index = 0; index < toggles.Length; index++)
            {
                var entry = toggles[index];
                double size = row * .65;
                var control = new GuiElementSwitch(api, value => select(entry.Code, value),
                    ElementBounds.Fixed(row * .15, index * row + (row - size) / 2, size, size), size, size * .15)
                { On = entry.Selected };
                switches.Add(control);
                composer.AddInteractiveElement(control);
                composer.AddStaticText(entry.Label, font, ElementBounds.Fixed(row, index * row + row * .2, width - row, row));
            }
            if (choices.Length > 0)
            {
                double labelWidth = styleLabelWidth * fit;
                composer.AddStaticText(styleLabel, font, ElementBounds.Fixed(row * .15, toggles.Length * row + row * .2, labelWidth, row));
                style = new HudOverlayStyleDropDown(api, choices.Select(entry => entry.Code).ToArray(), choices.Select(entry => entry.Label).ToArray(),
                    Math.Max(0, Array.FindIndex(choices, entry => entry.Selected)), (code, on) => select(code, on),
                    ElementBounds.Fixed(labelWidth, toggles.Length * row, width - labelWidth, row), font, environment.Viewport);
                composer.AddInteractiveElement(style);
            }
            if (sizeEntry?.Number is int value)
            {
                double labelWidth = sizeLabelWidth * fit;
                double top = (toggles.Length + (choices.Length > 0 ? 1 : 0)) * row;
                composer.AddStaticText(sizeEntry.Label, font, ElementBounds.Fixed(row * .15, top + row * .2, labelWidth, row));
                sizeInput = new HudOverlaySizeInput(api, ElementBounds.Fixed(labelWidth, top, width - labelWidth, row), font, value,
                    next => select("size:" + next.ToString(System.Globalization.CultureInfo.InvariantCulture), true));
                composer.AddInteractiveElement(sizeInput);
            }
            for (int index = 0; index < commands.Length; index++)
            {
                var command = commands[index];
                string key = "action:" + index;
                composer.AddButton(command.Label, () => { select(command.Code, true); return true; },
                    ElementBounds.Fixed(0, (toggles.Length + (choices.Length > 0 ? 1 : 0) + (sizeInput == null ? 0 : 1) + index) * row, width, row),
                    font, key: key);
                actions.Add(composer.GetButton(key));
            }
            composer.Compose();
            composer.FocusElement(0);
        }
        catch { composer.Dispose(); throw; }
    }
    /// <inheritdoc />
    public RectangleF VisibleBounds => style?.ExpandedBounds is { IsEmpty: false } expanded ? RectangleF.Union(panelBounds, expanded) : panelBounds;
    /// <inheritdoc />
    public bool IsOpened => !disposed && (switches.Any(control => control.HasFocus) || style?.HasFocus == true
        || sizeInput?.HasFocus == true || actions.Any(control => control.HasFocus));
    #endregion
    #region Rendering and pointer delegation
    /// <inheritdoc />
    public void Draw(float deltaTime) { if (!disposed) composer.Render(deltaTime); }
    /// <summary>Allows clicking the entire native switch row, including its label.</summary>
    public void MouseDown(MouseEvent args)
    {
        if (disposed || !VisibleBounds.Contains(args.X, args.Y) || args.Button != Vintagestory.API.Common.EnumMouseButton.Left) return;
        // An expanded list has priority where it overlaps the panel. A press on another panel row
        // dismisses the list and continues to that row instead of trapping input in the dropdown.
        if (style?.listMenu.IsOpened == true)
        {
            if (style.ExpandedBounds.Contains(args.X, args.Y))
            {
                composer.FocusElement(switches.Count);
                style.OnMouseDown(api, args);
                return;
            }
            style.OnFocusLost();
        }
        if (style != null && style.IsPositionInside(args.X, args.Y))
        {
            composer.FocusElement(switches.Count);
            style.OnMouseDown(api, args);
            return;
        }
        if (sizeInput != null && sizeInput.IsPositionInside(args.X, args.Y))
        {
            composer.FocusElement(switches.Count + (style == null ? 0 : 1));
            sizeInput.OnMouseDownOnElement(api, args);
            args.Handled = true;
            return;
        }
        for (int index = 0; index < actions.Count; index++)
        {
            if (!actions[index].IsPositionInside(args.X, args.Y)) continue;
            composer.FocusElement(switches.Count + (style == null ? 0 : 1) + (sizeInput == null ? 0 : 1) + index);
            actions[index].OnMouseDownOnElement(api, args);
            args.Handled = true;
            return;
        }
        int row = (int)((args.Y - panelBounds.Top) * (switches.Count + (style == null ? 0 : 1) + (sizeInput == null ? 0 : 1) + actions.Count) / panelBounds.Height);
        if (row < 0 || row >= switches.Count) return;
        composer.FocusElement(row);
        switches[row].OnMouseDownOnElement(api, args);
        args.Handled = true;
    }
    /// <inheritdoc />
    public void MouseUp(MouseEvent args) { if (!disposed) composer.OnMouseUp(args); }
    /// <inheritdoc />
    public void MouseMove(MouseEvent args) { if (!disposed) composer.OnMouseMove(args); }
    /// <inheritdoc />
    public void MouseWheel(MouseWheelEventArgs args) { if (!disposed) composer.OnMouseWheel(args); }
    #endregion
    #region Keyboard and disposal
    /// <inheritdoc />
    public void KeyDown(KeyEvent args) { if (!disposed) composer.OnKeyDown(args, true); }
    /// <inheritdoc />
    public void KeyPress(KeyEvent args) { if (!disposed) composer.OnKeyPress(args); }
    public void CommitPendingInput() { if (!disposed && sizeInput?.HasFocus == true) sizeInput.OnFocusLost(); }
    /// <summary>Releases owned native controls and composer textures once.</summary>
    public void Dispose() { if (disposed) return; disposed = true; composer.Dispose(); }
    #endregion
    #endregion
}
