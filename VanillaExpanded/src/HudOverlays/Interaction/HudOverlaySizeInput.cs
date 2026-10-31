using System;
using System.Globalization;
using VanillaExpanded.HudOverlays.Configuration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Commits spinner steps immediately, but allows a complete typed value before Enter or focus loss.</summary>
internal sealed class HudOverlaySizeInput : GuiElementNumberInput
{
    private readonly Action<int> commit;
    private int current;

    public HudOverlaySizeInput(ICoreClientAPI api, ElementBounds bounds, CairoFont font, int value, Action<int> commit)
        : base(api, bounds, _ => { }, font)
    {
        this.commit = commit;
        Interval = HudOverlaySizeSetting.Step;
        IntMode = true;
        current = value;
        SetValue(value);
    }

    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        string before = GetText();
        base.OnMouseDownOnElement(api, args);
        if (GetText() != before) Commit();
    }

    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        string before = GetText();
        base.OnMouseWheel(api, args);
        if (GetText() != before) Commit();
    }

    public override void OnKeyDown(ICoreClientAPI api, KeyEvent args)
    {
        if (args.KeyCode is (int)GlKeys.Enter or (int)GlKeys.KeypadEnter)
        {
            Commit(); args.Handled = true;
        }
        else if (args.KeyCode is (int)GlKeys.Up or (int)GlKeys.Down)
        {
            SetValue(Math.Clamp(current + (args.KeyCode == (int)GlKeys.Up ? HudOverlaySizeSetting.Step : -HudOverlaySizeSetting.Step),
                HudOverlaySizeSetting.Minimum, HudOverlaySizeSetting.Maximum).ToString(GlobalConstants.DefaultCultureInfo));
            Commit(); args.Handled = true;
        }
        else base.OnKeyDown(api, args);
    }

    public override void OnFocusLost()
    {
        base.OnFocusLost();
        Commit();
    }

    private void Commit()
    {
        string text = GetText();
        int next = HudOverlaySizeSetting.Normalize(text is "" or "-" ? HudOverlaySizeSetting.Minimum
            : int.Parse(text, NumberStyles.Integer, GlobalConstants.DefaultCultureInfo));
        SetValue(next);
        if (next == current) return;
        int previous = current;
        current = next;
        try { commit(next); }
        catch { current = previous; SetValue(previous); throw; }
    }
}
