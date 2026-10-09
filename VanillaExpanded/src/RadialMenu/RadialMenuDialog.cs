using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RadialMenu;

/// <summary>Captures pointer and keyboard input for one reusable radial interaction.</summary>
internal sealed class RadialMenuDialog : GuiDialog
{
    #region State
    private const float ScreenRadiusFraction = 0.25f;
    private const float HoverSoundVolume = 1.0f;
    private readonly RadialMenuRenderer renderer;
    private RadialMenuLayout? layout;
    private RadialMenuInteraction? interaction;
    private bool closing;
    private bool waitForMouseUp;
    private string toggleKeyCode = string.Empty;
    #endregion

    #region Dialog contract
    /// <summary>Creates the dialog and its persistent renderer.</summary>
    public RadialMenuDialog(ICoreClientAPI capi) : base(capi)
    {
        renderer = new RadialMenuRenderer(capi);
    }

    /// <inheritdoc />
    public override string ToggleKeyCombinationCode => toggleKeyCode;

    /// <summary>Keeps opening-hotkey registration with the caller while using its binding for dismissal.</summary>
    public override void OnBlockTexturesLoaded()
    {
        // The base implementation would replace the caller's hotkey handler with this shared dialog.
    }
    /// <inheritdoc />
    public override double InputOrder => 0;
    /// <inheritdoc />
    public override double DrawOrder => 1.1;
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
        SuppressWorldLeftClick();
        if (interaction is null || layout is null) return;
        float radius = GetRadiusPixels(layout);
        float x = capi.Render.FrameWidth / 2f;
        float y = capi.Render.FrameHeight / 2f;
        renderer.Render(layout, interaction, deltaTime, x, y, radius);
    }

    /// <inheritdoc />
    public override void OnMouseMove(MouseEvent args)
    {
        if (!IsOpened()) return;
        if (interaction?.IsOpen == true && layout is not null)
        {
            float radius = GetRadiusPixels(layout);
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
        float radius = GetRadiusPixels(layout);
        interaction.MovePointer(args.X, args.Y, capi.Render.FrameWidth / 2f, capi.Render.FrameHeight / 2f, radius);
        waitForMouseUp = true;
    }

    /// <inheritdoc />
    public override void OnMouseUp(MouseEvent args)
    {
        if (!IsOpened()) return;
        args.Handled = true;
        if (waitForMouseUp && args.Button == EnumMouseButton.Left)
        {
            waitForMouseUp = false;
            if (interaction?.IsOpen != true || layout is null) return;
            float radius = GetRadiusPixels(layout);
            interaction.MovePointer(args.X, args.Y, capi.Render.FrameWidth / 2f, capi.Render.FrameHeight / 2f, radius);
            RadialMenuInteraction selectedInteraction = interaction;
            if (selectedInteraction.SelectHovered() && !selectedInteraction.IsOpen && IsOpened()) TryClose();
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
        if (!IsOpened()) return;
        // Let the native dialog match the caller's current hotkey binding before swallowing modal input.
        base.OnKeyDown(args);
        if (args.Handled) return;
        // Escape remains unhandled so the engine routes it through OnEscapePressed.
        if (args.KeyCode != (int)GlKeys.Escape) args.Handled = true;
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
            toggleKeyCode = string.Empty;
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

    private float GetRadiusPixels(RadialMenuLayout currentLayout) =>
        MathF.Min(capi.Render.FrameWidth, capi.Render.FrameHeight) * ScreenRadiusFraction * (float)currentLayout.RadiusScale;
    #endregion

    #region Caller interaction
    /// <summary>Opens a complete layout with optional native hotkey dismissal and selection/cancellation callbacks.</summary>
    public bool Open(RadialMenuLayout nextLayout, IEnumerable<RadialMenuEntry> entries,
        System.Func<string, RadialMenuSelectionResult> selected, Action cancelled, string toggleKeyCode = "")
    {
        if (IsOpened() || !renderer.IsReady) return false;
        this.toggleKeyCode = toggleKeyCode;
        renderer.ResetInteraction();
        layout = nextLayout ?? throw new ArgumentNullException(nameof(nextLayout));
        renderer.PrepareLayout(layout);
        interaction = new RadialMenuInteraction(layout, entries);
        interaction.Selected += selected;
        interaction.Cancelled += cancelled;
        interaction.HoverChanged += OnHoverChanged;
        interaction.Open();
        float radius = GetRadiusPixels(layout);
        interaction.MovePointer(capi.Input.MouseX, capi.Input.MouseY,
            capi.Render.FrameWidth / 2f, capi.Render.FrameHeight / 2f, radius);
        if (TryOpen())
        {
            SuppressWorldLeftClick();
            return true;
        }
        interaction.Cancel();
        interaction = null;
        layout = null;
        this.toggleKeyCode = string.Empty;
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

    /// <summary>Plays the game's standard button hover sound for each changed menu target.</summary>
    private void OnHoverChanged(string? hoveredId) => capi.Gui.PlaySound("menubutton", volume: HoverSoundVolume);

    /// <summary>Prevents a left click held before the dialog opened from continuing as an in-world action.</summary>
    private void SuppressWorldLeftClick() => capi.Input.InWorldMouseButton.Left = false;
    #endregion
}










