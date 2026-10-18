using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Owns native switches and labels directly, avoiding the private switch lifetime in native list-menu multi-select mode.</summary>
internal sealed class HudOverlayTogglePopup : IHudMenuPopup
{
    private readonly ICoreClientAPI api;
    private readonly GuiComposer composer;
    private readonly List<GuiElementSwitch> switches = new();
    private bool disposed;
    #region Public API
    #region Construction and state
    /// <summary>Composes normal HUD lettering and native switches, shrinking only when the entire viewport cannot fit the compact panel.</summary>
    public HudOverlayTogglePopup(ICoreClientAPI api, IReadOnlyList<HudMenuEntry> entries, Action<string, bool> select,
        PointF pointer, HudMenuEnvironment environment)
    {
        this.api = api;
        var font = CairoFont.WhiteSmallText().WithFontSize(HudOverlaySizes.Text);
        double scale = environment.GuiScale;
        double row = Math.Max(30, font.GetFontExtents().Height / scale + 8);
        double width = entries.Max(entry => font.GetTextExtents(entry.Label).Width / scale) + row + 16;
        double height = row * entries.Count;
        // Toggle pages have at most two built-in rows. Uniformly fit these controls in very small viewports;
        // style pages use the native scrolling list instead.
        double fit = Math.Min(1, Math.Min(environment.Viewport.Width / (width * scale), environment.Viewport.Height / (height * scale)));
        if (!double.IsFinite(fit) || fit <= 0) throw new ArgumentOutOfRangeException(nameof(environment));
        row *= fit; width *= fit; height *= fit;
        font.WithFontSize((float)(HudOverlaySizes.Text * fit));
        VisibleBounds = HudPopupPlacement.Fit(pointer, new((float)(width * scale), (float)(height * scale)), environment.Viewport);
        var bounds = ElementBounds.Fixed(VisibleBounds.X / scale, VisibleBounds.Y / scale, width, height).WithParent(api.Gui.WindowBounds);
        composer = api.Gui.CreateCompo("vanillaexpanded:hud-context-toggles", bounds);
        try
        {
            composer.AddDialogBG(ElementBounds.Fixed(0, 0, width, height), false);
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                double size = row * .65;
                var control = new GuiElementSwitch(api, value => select(entry.Code, value),
                    ElementBounds.Fixed(row * .15, index * row + (row - size) / 2, size, size), size, size * .15)
                { On = entry.Selected };
                switches.Add(control);
                composer.AddInteractiveElement(control);
                composer.AddStaticText(entry.Label, font, ElementBounds.Fixed(row, index * row + row * .2, width - row, row));
            }
            composer.Compose();
            composer.FocusElement(0);
        }
        catch { composer.Dispose(); throw; }
    }
    /// <inheritdoc />
    public RectangleF VisibleBounds { get; }
    /// <inheritdoc />
    public bool IsOpened => !disposed && switches.Any(control => control.HasFocus);
    #endregion
    #region Rendering and pointer delegation
    /// <inheritdoc />
    public void Draw(float deltaTime) { if (!disposed) composer.Render(deltaTime); }
    /// <summary>Allows clicking the entire native switch row, including its label.</summary>
    public void MouseDown(MouseEvent args)
    {
        if (disposed || !VisibleBounds.Contains(args.X, args.Y) || args.Button != Vintagestory.API.Common.EnumMouseButton.Left) return;
        int row = Math.Min(switches.Count - 1, (int)((args.Y - VisibleBounds.Top) * switches.Count / VisibleBounds.Height));
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
    /// <summary>Releases owned native controls and composer textures once.</summary>
    public void Dispose() { if (disposed) return; disposed = true; composer.Dispose(); }
    #endregion
    #endregion
}

