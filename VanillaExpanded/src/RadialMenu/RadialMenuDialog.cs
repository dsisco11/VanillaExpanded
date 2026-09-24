using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RadialMenu;

/// <summary>Captures pointer and keyboard input for one reusable radial interaction.</summary>
internal sealed class RadialMenuDialog : GuiDialog
{
    #region State
    private const float ScreenRadiusFraction = 0.252f;
    private readonly RadialMenuRenderer renderer;
    private RadialMenuLayout? layout;
    private RadialMenuInteraction? interaction;
    private bool closing;
    private bool waitForMouseUp;
    #endregion

    #region Dialog contract
    /// <summary>Creates the dialog and its persistent renderer.</summary>
    public RadialMenuDialog(ICoreClientAPI capi) : base(capi)
    {
        renderer = new RadialMenuRenderer(capi);
    }

    /// <inheritdoc />
    public override string ToggleKeyCombinationCode => string.Empty;
    /// <inheritdoc />
    public override bool PrefersUngrabbedMouse => true;
    /// <inheritdoc />
    public override bool DisableMouseGrab => true;
    /// <inheritdoc />
    public override bool CaptureAllInputs() => true;
    /// <inheritdoc />
    public override bool CaptureRawMouse() => true;

    /// <inheritdoc />
    public override void OnRenderGUI(float deltaTime)
    {
        if (Vintagestory.Client.ScreenManager.Platform?.IsFocused != true)
        {
            Cancel();
            return;
        }
        if (waitForMouseUp)
        {
            if (!capi.Input.MouseButton.Left) TryClose();
            else if (interaction is not null && layout is not null)
            {
                float heldRadius = MathF.Min(capi.Render.FrameWidth, capi.Render.FrameHeight) * ScreenRadiusFraction;
                renderer.Render(layout, interaction, deltaTime, capi.Render.FrameWidth / 2f, capi.Render.FrameHeight / 2f, heldRadius);
            }
            return;
        }
        if (interaction?.IsOpen != true || layout is null) return;
        float radius = MathF.Min(capi.Render.FrameWidth, capi.Render.FrameHeight) * ScreenRadiusFraction;
        float x = capi.Render.FrameWidth / 2f;
        float y = capi.Render.FrameHeight / 2f;
        interaction.MovePointer(capi.Input.MouseX, capi.Input.MouseY, x, y, radius);
        renderer.Render(layout, interaction, deltaTime, x, y, radius);
    }

    /// <inheritdoc />
    public override void OnMouseMove(MouseEvent args)
    {
        if (!IsOpened()) return;
        if (interaction?.IsOpen == true && layout is not null)
        {
            float radius = MathF.Min(capi.Render.FrameWidth, capi.Render.FrameHeight) * ScreenRadiusFraction;
            interaction.MovePointer(args.X, args.Y, capi.Render.FrameWidth / 2f, capi.Render.FrameHeight / 2f, radius);
        }
        args.Handled = true;
    }

    /// <inheritdoc />
    public override void OnMouseDown(MouseEvent args)
    {
        if (!IsOpened()) return;
        args.Handled = true;
        if (args.Button == EnumMouseButton.Right)
        {
            Cancel();
            return;
        }
        if (args.Button != EnumMouseButton.Left || interaction?.IsOpen != true || layout is null) return;
        if (Vintagestory.Client.ScreenManager.Platform?.IsFocused != true)
        {
            Cancel();
            return;
        }
        float radius = MathF.Min(capi.Render.FrameWidth, capi.Render.FrameHeight) * ScreenRadiusFraction;
        interaction.MovePointer(args.X, args.Y, capi.Render.FrameWidth / 2f, capi.Render.FrameHeight / 2f, radius);
        waitForMouseUp = true;
        if (!interaction.SelectHovered()) waitForMouseUp = false;
    }

    /// <inheritdoc />
    public override void OnMouseUp(MouseEvent args)
    {
        if (!IsOpened()) return;
        args.Handled = true;
        if (waitForMouseUp && args.Button == EnumMouseButton.Left)
        {
            waitForMouseUp = false;
            TryClose();
        }
    }

    /// <inheritdoc />
    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (IsOpened()) args.SetHandled(true);
    }

    /// <inheritdoc />
    public override void OnKeyDown(KeyEvent args)
    {
        if (IsOpened()) args.Handled = true;
    }

    /// <inheritdoc />
    public override void OnKeyUp(KeyEvent args)
    {
        if (IsOpened()) args.Handled = true;
    }

    /// <inheritdoc />
    public override bool OnEscapePressed()
    {
        if (!IsOpened()) return false;
        Cancel();
        return true;
    }

    /// <inheritdoc />
    public override void UnFocus()
    {
        base.UnFocus();
        if (!closing && IsOpened()) Cancel();
    }

    /// <inheritdoc />
    public override bool TryClose()
    {
        if (closing || !IsOpened()) return true;
        closing = true;
        try
        {
            interaction?.Cancel();
            waitForMouseUp = false;
            bool closed = base.TryClose();
            interaction = null;
            layout = null;
            return closed;
        }
        finally
        {
            closing = false;
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        TryClose();
        renderer.Dispose();
        base.Dispose();
    }
    #endregion

    #region Caller interaction
    /// <summary>Opens a complete fixed layout and reports its selection and cancellation.</summary>
    public bool Open(RadialMenuLayout nextLayout, IEnumerable<RadialMenuEntry> entries, Action<string> selected, Action cancelled)
    {
        if (IsOpened() || !renderer.IsReady) return false;
        renderer.ResetInteraction();
        layout = nextLayout ?? throw new ArgumentNullException(nameof(nextLayout));
        renderer.PrepareLayout(layout);
        interaction = new RadialMenuInteraction(layout, entries);
        interaction.Selected += selected;
        interaction.Cancelled += cancelled;
        interaction.Open();
        if (TryOpen()) return true;
        interaction.Cancel();
        interaction = null;
        layout = null;
        return false;
    }

    /// <summary>Changes labels, icons, and availability while retaining the fixed wedge mesh.</summary>
    public void UpdateEntries(IEnumerable<RadialMenuEntry> entries) => interaction?.UpdateEntries(entries);

    /// <summary>Changes an open layout and its entries without releasing the dialog's input capture.</summary>
    public void UpdateLayout(RadialMenuLayout nextLayout, IEnumerable<RadialMenuEntry> entries)
    {
        if (interaction?.IsOpen != true) return;
        interaction.UpdateLayout(nextLayout, entries);
        layout = nextLayout;
        renderer.PrepareLayout(nextLayout);
    }

    /// <summary>Selects the current enabled hover target without requiring a mouse click.</summary>
    public bool SelectHovered() => interaction?.SelectHovered() == true;

    /// <summary>Cancels the current interaction and restores normal GUI input ownership.</summary>
    public void Cancel()
    {
        interaction?.Cancel();
        TryClose();
    }

    /// <summary>Reloads menu-owned shaders after the engine requests a reload.</summary>
    public bool ReloadShader()
    {
        bool success = renderer.ReloadShader();
        if (!success) Cancel();
        return success;
    }
    #endregion
}










