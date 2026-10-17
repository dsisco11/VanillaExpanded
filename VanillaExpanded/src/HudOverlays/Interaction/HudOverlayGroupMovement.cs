using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Tracks one native control target while shared containers own permission, membership, and persistence.</summary>
internal sealed class HudOverlayGroupMovement
{
    private readonly HudOverlayContainers containers;
    private readonly string initialContainer;
    private PointF start, pointerStart;
    private double dragScale;
    public string? IndicatorId { get; }
    public string Id => Dragging && containers.ActiveContainer != null ? containers.ActiveContainer
        : IndicatorId == null ? initialContainer : containers.Owner(IndicatorId)?.Id ?? initialContainer;
    public string Key => "vanillaexpanded:hud:controls:" + (IndicatorId == null ? "container:" + initialContainer : "member:" + IndicatorId);
    public bool Movable => !containers.ReadOnly && containers.Resolved.FirstOrDefault(item => item.Id == Id)?.Movable == true;
    public bool Dragging { get; private set; }
    public bool HasMultipleMembers => containers.Resolved.FirstOrDefault(item => item.Id == Id)?.Tree.Leaves().Count() > 1;
    public PointF? Position
    {
        get
        {
            var item = containers.Resolved.FirstOrDefault(item => item.Id == Id);
            return item == null ? null : containers.Origin(item);
        }
    }
    public IReadOnlyDictionary<string, PointF> MemberOrigins { get; set; } = new Dictionary<string, PointF>();
    public PointF ContainerOrigin { get; set; }
    public PointF UnsnappedOrigin { get; private set; }
    public long DragRevision { get; private set; }
    public IReadOnlyCollection<string> StartingMembers { get; private set; } = Array.Empty<string>();

    #region Public API
    #region Binding and permission
    /// <summary>Binds a stable group or individual grip without reading or writing native coordinates.</summary>
    public HudOverlayGroupMovement(HudOverlayContainers containers, string containerId, string? indicator = null)
    {
        this.containers = containers;
        initialContainer = containerId;
        IndicatorId = indicator;
    }

    /// <summary>Changes only the effective owner's explicit permission at its current displayed content origin.</summary>
    public void SetMovable(bool value)
    {
        Cancel();
        containers.SetMovable(Id, value, ContainerOrigin);
    }

    #endregion
    #region Drag lifecycle
    /// <summary>Captures total pointer displacement and creates an atomic membership/position preview.</summary>
    public void Begin(PointF contentOrigin, PointF pointer, double scale)
    {
        if (Dragging || !Movable) return;
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        string source = Id;
        var members = containers.Resolved.First(item => item.Id == source).Tree.Leaves().ToArray();
        if (!containers.Begin(source, IndicatorId, contentOrigin, MemberOrigins, ContainerOrigin)) return;
        StartingMembers = members;
        start = contentOrigin;
        UnsnappedOrigin = contentOrigin;
        pointerStart = pointer;
        dragScale = scale;
        Dragging = true;
        DragRevision++;
    }

    /// <summary>Derives preview geometry from the captured origin and total physical pointer delta.</summary>
    public void Move(PointF pointer)
    {
        if (!Dragging) return;
        UnsnappedOrigin = new PointF(start.X + (float)((pointer.X - pointerStart.X) / dragScale),
            start.Y + (float)((pointer.Y - pointerStart.Y) / dragScale));
        containers.ClearSnap(UnsnappedOrigin);
    }

    /// <summary>Commits the displayed origin with the entire preview and ends local capture.</summary>
    public void Commit(PointF displayedOrigin)
    {
        if (!Dragging) return;
        containers.Commit(displayedOrigin);
        Dragging = false;
    }

    /// <summary>Restores starting membership and placement without saving a partial drag.</summary>
    public void Cancel()
    {
        bool wasDragging = Dragging;
        Dragging = false;
        if (wasDragging) containers.Cancel();
    }
    #endregion
    #endregion
}
