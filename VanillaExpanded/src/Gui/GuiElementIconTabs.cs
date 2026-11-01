using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Gui;

internal sealed class GuiElementIconTabs : GuiElement
{
    internal const double TabSize = 28;
    private const double Spacing = 5;
    private const double ArrowWidth = 16;
    internal readonly GuiTab[] tabs;
    private readonly string[] icons;
    private readonly Action<int> changed;
    private GuiElementHoverText[] hints = [];
    private GuiElementToggleButton[] buttons = [];
    private ElementBounds? content;
    private double scroll;
    private bool keyboardHint;
    private bool enabled = true;
    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            if (!value) OnFocusLost();
        }
    }
    internal GuiComposer? Composer { get; private set; }
    internal ElementBounds? Viewport { get; private set; }
    internal IReadOnlyList<GuiElementHoverText> Hints => hints;
    internal int activeElement { get; private set; }
    public override bool Focusable => enabled;

    internal GuiElementIconTabs(ICoreClientAPI api, GuiTab[] tabs, IReadOnlyList<string> iconNames,
        ElementBounds bounds, Action<int> changed) : base(api, bounds)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(iconNames);
        ArgumentNullException.ThrowIfNull(changed);
        if (tabs.Length == 0 || tabs.Length != iconNames.Count)
            throw new ArgumentException("Icon tabs need a tooltip name and icon for every entry.", nameof(tabs));
        this.tabs = tabs;
        icons = iconNames.ToArray();
        this.changed = changed;
    }

    private double MaxScroll => Math.Max(0, (content?.OuterWidth ?? 0) - (Viewport?.InnerWidth ?? 0));

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        DisposeComposition();
        var root = ElementBounds.Fixed(0, 0, Bounds.InnerWidth / scaled(1),
            Bounds.InnerHeight / scaled(1)).WithParent(Bounds);
        Bounds.WithChild(root);
        Composer = api.Gui.CreateCompo("icon-tabs", root);
        Composer.Tabbable = false;
        Viewport = ElementBounds.Fixed(0, 0, root.fixedWidth, root.fixedHeight);
        content = ElementBounds.Fixed(0, 0, 0, root.fixedHeight);
        var buttonBounds = new ElementBounds[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            buttonBounds[i] = ElementBounds.Fixed(Spacing, 0, TabSize, TabSize)
                .WithAlignment(EnumDialogArea.LeftMiddle);
            if (i > 0) buttonBounds[i].FixedRightOf(buttonBounds[i - 1], Spacing);
        }
        content.WithFixedWidth(buttonBounds[^1].fixedX + TabSize + Spacing);
        bool overflow = content.fixedWidth > root.fixedWidth;
        if (overflow)
            Viewport.WithFixedPosition(ArrowWidth + Spacing, 0)
                .WithFixedWidth(Math.Max(1, root.fixedWidth - 2 * (ArrowWidth + Spacing)));
        Composer.BeginClip(Viewport)
            .BeginChildElements(content);
        buttons = new GuiElementToggleButton[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            buttons[i] = new GuiElementToggleButton(api, icons[i], "", CairoFont.WhiteSmallText(),
                _ => SetValue(index), buttonBounds[i], true);
            Composer.AddInteractiveElement(buttons[i], "tab-" + i);
        }
        Composer.EndChildElements()
            .EndClip()
            .AddSmallButton("<", () => Shift(-scaled(48)),
                ElementBounds.Fixed(0, 0, ArrowWidth, TabSize).WithAlignment(EnumDialogArea.LeftMiddle), key: "left")
            .AddSmallButton(">", () => Shift(scaled(48)),
                ElementBounds.Fixed(0, 0, ArrowWidth, TabSize).WithAlignment(EnumDialogArea.RightMiddle), key: "right");
        Composer.GetButton("left").Visible = Composer.GetButton("right").Visible = overflow;
        Composer.Compose(false);
        hints = buttonBounds.Select((bounds, index) =>
        {
            var hintBounds = ElementBounds.Fixed(0, TabSize, TabSize, TabSize).WithParent(bounds);
            bounds.WithChild(hintBounds);
            var hint = new GuiElementHoverText(api, tabs[index].Name, CairoFont.WhiteSmallText(), 256, hintBounds);
            hint.SetAutoDisplay(false);
            hint.SetAutoWidth(true);
            hint.BeforeCalcBounds();
            return hint;
        }).ToArray();
        Shift(0);
        SetValue(activeElement, false);
    }

    internal void SetValue(int index, bool callhandler = true)
    {
        if ((uint)index >= (uint)tabs.Length) throw new ArgumentOutOfRangeException(nameof(index));
        activeElement = index;
        if (Composer != null && Viewport != null && content != null)
        {
            Composer.ToggleButtonsSetValue("tab", index);
            var selected = buttons[index].Bounds;
            double adjustment = selected.absX < Viewport.absX ? selected.absX - Viewport.absX
                : Math.Max(0, selected.absX + selected.OuterWidth - Viewport.absX - Viewport.InnerWidth);
            Shift(adjustment);
        }
        if (callhandler) changed(index);
    }

    private bool Shift(double amount)
    {
        scroll = Math.Clamp(scroll + amount, 0, MaxScroll);
        if (content != null && Composer != null)
        {
            content.fixedX = -scroll / scaled(1);
            Composer.Bounds.MarkDirtyRecursive();
            Composer.Bounds.CalcWorldBounds();
        }
        return true;
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        foreach (var hint in hints) hint.SetVisible(false);
        if (!enabled || Composer == null || Viewport == null) return;
        int clipDepth = api.Render.ScissorStack.Count;
        try { Composer.Render(deltaTime); }
        finally
        {
            while (api.Render.ScissorStack.Count > clipDepth) api.Render.PopScissor();
        }
        bool overViewport = Viewport.PointInside(api.Input.MouseX, api.Input.MouseY);
        for (int i = 0; i < hints.Length; i++)
        {
            bool show = keyboardHint && HasFocus ? i == activeElement
                : overViewport && buttons[i].IsPositionInside(api.Input.MouseX, api.Input.MouseY);
            hints[i].SetVisible(show);
            hints[i].SetFollowMouse(!(keyboardHint && HasFocus));
            if (show) hints[i].RenderInteractiveElements(deltaTime);
        }
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!enabled || Composer == null || args.Button != EnumMouseButton.Left) return;
        keyboardHint = false;
        Composer.OnMouseDown(args);
        args.Handled = true;
    }

    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (enabled) Composer?.OnMouseUp(args);
    }

    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        keyboardHint = false;
        if (enabled) Composer?.OnMouseMove(args);
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!enabled || !Bounds.PointInside(api.Input.MouseX, api.Input.MouseY)) return;
        args.SetHandled(true);
        keyboardHint = false;
        Shift(args.deltaPrecise * scaled(10));
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (!enabled || !HasFocus) return;
        int next = args.KeyCode switch
        {
            (int)GlKeys.Left => (activeElement + tabs.Length - 1) % tabs.Length,
            (int)GlKeys.Right => (activeElement + 1) % tabs.Length,
            _ => -1
        };
        if (next < 0) { base.OnKeyDown(api, args); return; }
        args.Handled = true;
        keyboardHint = true;
        SetValue(next);
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        keyboardHint = false;
        foreach (var hint in hints) hint.SetVisible(false);
        Composer?.UnfocusOwnElements();
    }

    private void DisposeComposition()
    {
        foreach (var hint in hints) hint.Dispose();
        hints = [];
        buttons = [];
        if (Composer != null)
        {
            Bounds.ChildBounds.Remove(Composer.Bounds);
            Composer.Dispose();
        }
        Composer = null;
    }

    public override void Dispose()
    {
        DisposeComposition();
        base.Dispose();
    }
}
