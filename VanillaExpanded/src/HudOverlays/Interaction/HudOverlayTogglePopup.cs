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
    private readonly List<ElementBounds> switchRows = new();
    private readonly HudOverlayStyleDropDown? style;
    private readonly HudOverlaySizeInput? sizeInput;
    private readonly List<GuiElementTextButton> actions = new();
    private readonly RectangleF panelBounds;
    private bool disposed;
    #region Public API
    #region Construction and state
    /// <summary>Composes compact native controls in padded, separated rows, shrinking only when the viewport cannot fit the panel.</summary>
    public HudOverlayTogglePopup(ICoreClientAPI api, IReadOnlyList<HudMenuEntry> entries, Action<string, bool> select,
        PointF pointer, HudMenuEnvironment environment)
    {
        this.api = api;
        var font = CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.MenuText);
        var toggles = entries.Where(entry => entry.Kind == HudMenuEntryKind.Toggle).ToArray();
        var choices = entries.Where(entry => entry.Kind == HudMenuEntryKind.StyleChoice).ToArray();
        var commands = entries.Where(entry => entry.Kind == HudMenuEntryKind.Action).ToArray();
        var sizeEntry = entries.SingleOrDefault(entry => entry.Kind == HudMenuEntryKind.Number);
        string styleLabel = Lang.GetL(environment.Locale, "vanillaexpanded:hud-menu-style");
        double scale = environment.GuiScale;
        double textHeight = font.GetFontExtents().Height / scale;
        double row = Math.Max(24, Math.Ceiling(textHeight) + 8);
        double padding = 8, gap = 8, columnGap = 10, switchSize = 16;
        double Measure(string text)
        {
            var extents = font.GetTextExtents(text);
            return Math.Ceiling(Math.Max(extents.Width, extents.XAdvance) / scale) + 2;
        }
        double labelWidth = Math.Max(choices.Length > 0 ? Measure(styleLabel) : 0, sizeEntry == null ? 0 : Measure(sizeEntry.Label));
        double fieldWidth = Math.Max(80, choices.Select(entry => Measure(entry.Label) + 30).DefaultIfEmpty(0).Max());
        double contentWidth = Math.Max(80, toggles.Select(entry => Measure(entry.Label) + switchSize + columnGap).DefaultIfEmpty(0).Max());
        if (choices.Length > 0 || sizeEntry != null) contentWidth = Math.Max(contentWidth, labelWidth + columnGap + fieldWidth);
        contentWidth = Math.Max(contentWidth, commands.Select(entry => Measure(entry.Label) + 16).DefaultIfEmpty(0).Max());
        int rowCount = toggles.Length + (choices.Length > 0 ? 1 : 0) + (sizeEntry == null ? 0 : 1) + commands.Length;
        double contentHeight = row * rowCount + gap * Math.Max(0, rowCount - 1);
        double width = contentWidth + 2 * padding, height = contentHeight + 2 * padding;
        // Fit the compact panel uniformly only when required. The expanded dropdown scrolls independently.
        double fit = Math.Min(1, Math.Min(environment.Viewport.Width / (width * scale), environment.Viewport.Height / (height * scale)));
        if (!double.IsFinite(fit) || fit <= 0) throw new ArgumentOutOfRangeException(nameof(environment));
        row *= fit; width *= fit; height *= fit;
        contentWidth *= fit; contentHeight *= fit; padding *= fit; gap *= fit;
        columnGap *= fit; switchSize *= fit; labelWidth *= fit; textHeight *= fit;
        font.WithFontSize((float)(HudOverlaySizes.MenuText * fit));
        panelBounds = HudPopupPlacement.Fit(pointer, new((float)(width * scale), (float)(height * scale)), environment.Viewport);
        var bounds = ElementBounds.Fixed(panelBounds.X / scale, panelBounds.Y / scale, width, height).WithParent(api.Gui.WindowBounds);
        composer = api.Gui.CreateCompo("vanillaexpanded:hud-context-toggles", bounds);
        try
        {
            var content = ElementBounds.Fixed(0, 0, contentWidth, contentHeight).WithFixedPadding(padding);
            composer.AddDialogBG(content, false).BeginChildElements(content);
            ElementBounds? previousRow = null;
            ElementBounds BeginRow()
            {
                var next = previousRow == null ? ElementBounds.Fixed(0, 0, contentWidth, row) : previousRow.BelowCopy(0, gap);
                composer.BeginChildElements(next);
                previousRow = next;
                return next;
            }
            ElementBounds LabelBounds(double left, double labelSpace) =>
                ElementBounds.Fixed(left, 0, labelSpace, textHeight).WithAlignment(EnumDialogArea.LeftMiddle);
            for (int index = 0; index < toggles.Length; index++)
            {
                var entry = toggles[index];
                switchRows.Add(BeginRow());
                var control = new GuiElementSwitch(api, value => select(entry.Code, value),
                    ElementBounds.Fixed(0, 0, switchSize, switchSize).WithAlignment(EnumDialogArea.LeftMiddle), switchSize, switchSize * .15)
                { On = entry.Selected };
                switches.Add(control);
                composer.AddInteractiveElement(control, "toggle:" + index);
                composer.AddStaticText(entry.Label, font, LabelBounds(switchSize + columnGap, contentWidth - switchSize - columnGap), "toggle-label:" + index);
                composer.EndChildElements();
            }
            if (choices.Length > 0)
            {
                BeginRow();
                composer.AddStaticText(styleLabel, font, LabelBounds(0, labelWidth), "style-label");
                style = new HudOverlayStyleDropDown(api, choices.Select(entry => entry.Code).ToArray(), choices.Select(entry => entry.Label).ToArray(),
                    Math.Max(0, Array.FindIndex(choices, entry => entry.Selected)), (code, on) => select(code, on),
                    ElementBounds.Fixed(labelWidth + columnGap, 0, contentWidth - labelWidth - columnGap, row), font, environment.Viewport);
                composer.AddInteractiveElement(style, "style");
                composer.EndChildElements();
            }
            if (sizeEntry?.Number is int value)
            {
                BeginRow();
                composer.AddStaticText(sizeEntry.Label, font, LabelBounds(0, labelWidth), "size-label");
                sizeInput = new HudOverlaySizeInput(api, ElementBounds.Fixed(labelWidth + columnGap, 0, contentWidth - labelWidth - columnGap, row), font, value,
                    next => select("size:" + next.ToString(System.Globalization.CultureInfo.InvariantCulture), true));
                composer.AddInteractiveElement(sizeInput, "size");
                composer.EndChildElements();
            }
            for (int index = 0; index < commands.Length; index++)
            {
                var command = commands[index];
                string key = "action:" + index;
                BeginRow();
                composer.AddButton(command.Label, () => { select(command.Code, true); return true; },
                    ElementBounds.Fixed(0, 0, contentWidth, row),
                    font, key: key);
                actions.Add(composer.GetButton(key));
                composer.EndChildElements();
            }
            composer.EndChildElements();
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
        for (int index = 0; index < switchRows.Count; index++)
        {
            if (!switchRows[index].PointInside(args.X, args.Y)) continue;
            composer.FocusElement(index);
            switches[index].OnMouseDownOnElement(api, args);
            args.Handled = true;
            return;
        }
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
