using System;
using System.Drawing;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Owns one group's native saved origin and cancellable drag preview, independently of its presentation.</summary>
internal sealed class HudOverlayGroupMovement
{
    public string Id { get; }
    public string Key { get; }
    private readonly IGuiAPI gui;
    private PointF? saved;
    private PointF? preview;
    private PointF start;
    private PointF pointerStart;
    private double dragScale;
    public bool Movable { get; private set; }
    public bool Dragging { get; private set; }
    public PointF? Position => preview ?? saved;

    #region Public API
    #region Construction and mode
    /// <summary>Restores native integer GUI-unit coordinates without writing settings during composition or rendering.</summary>
    public HudOverlayGroupMovement(IGuiAPI gui, string groupId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        this.gui = gui;
        Id = groupId;
        Key = HudOverlayPositionKeys.Group(groupId);
        var position = gui.GetDialogPosition(Key);
        saved = position == null ? null : new PointF(position.X, position.Y);
        Movable = saved.HasValue;
    }

    /// <summary>Enables dragging, or restores registered placement and clears only this group's native key.</summary>
    /// <param name="value">Whether the group accepts dragging.</param>
    /// <param name="clearNativePosition">False when the native Fixed dropdown callback already cleared the key.</param>
    public void SetMovable(bool value, bool clearNativePosition = true)
    {
        Cancel();
        if (!value)
        {
            if (clearNativePosition) gui.SetDialogPosition(Key, null!);
            saved = null;
        }
        Movable = value;
    }
    #endregion

    #region Drag lifecycle
    /// <summary>Captures the displayed content origin and scale so a cancelled drag can restore its prior preference.</summary>
    public void Begin(PointF contentOrigin, PointF pointer, double scale)
    {
        if (!Movable || Dragging) return;
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        start = contentOrigin;
        pointerStart = pointer;
        dragScale = scale;
        preview = start;
        Dragging = true;
    }

    /// <summary>Uses total pointer displacement, leaving screen clamping to the existing layout owner.</summary>
    public void Move(PointF pointer)
    {
        if (!Dragging) return;
        preview = new PointF(start.X + (float)((pointer.X - pointerStart.X) / dragScale),
            start.Y + (float)((pointer.Y - pointerStart.Y) / dragScale));
    }

    /// <summary>Stores the displayed, clamped content origin using native truncation only on successful release.</summary>
    public void Commit(PointF displayedOrigin)
    {
        if (!Dragging) return;
        var position = new Vec2i((int)displayedOrigin.X, (int)displayedOrigin.Y);
        gui.SetDialogPosition(Key, position);
        saved = new PointF(position.X, position.Y);
        Cancel();
    }

    /// <summary>Discards transient geometry without modifying native position storage.</summary>
    public void Cancel()
    {
        preview = null;
        Dragging = false;
    }
    #endregion
    #endregion
}
