using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Persistence;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Resolves effective flat membership and owns transactional movement independently of feature lifetimes.</summary>
internal sealed class HudOverlayContainers
{
    private readonly HudOverlayRegistry registry;
    private readonly IGuiAPI gui;
    private readonly HudLayoutStore store;
    private readonly Dictionary<string, HudOverlayGroupMovement> movements = new(StringComparer.Ordinal);
    private readonly HashSet<string> legacyRead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HudLayoutContainer> imported = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HudOverlayGroup> templates = new(StringComparer.Ordinal);
    private HudOverlayRegistration[] registrations = Array.Empty<HudOverlayRegistration>();
    private HudLayoutContainer[]? preview;
    private HudLayoutContainer[]? dragBase;
    private string? dragContainer;
    private string? newGroupId;
    private string? active;
    private PointF? previewOrigin;
    private string? draggedIndicator;
    private HudOverlaySourceReservation? sourceReservation;
    private HashSet<string> sourceReservationMembers = new(StringComparer.Ordinal);
    private HudLayoutContainer[] resolved = Array.Empty<HudLayoutContainer>();
    public IReadOnlyList<HudLayoutContainer> Resolved => resolved;
    public bool ReadOnly => store.ReadOnly;

    #region Public API
    #region Binding and resolution
    /// <summary>Borrows client storage and registry; no feature artwork or native compositions are created here.</summary>
    public HudOverlayContainers(ICoreClientAPI api, HudOverlayRegistry registry, Action<Exception>? diagnostic = null)
    {
        this.registry = registry;
        gui = api.Gui;
        store = new HudLayoutStore(api.Settings.String, gui, diagnostic);
        Synchronize();
    }

    /// <summary>Refreshes registration/default snapshots without rewriting explicit saved layouts.</summary>
    public void Synchronize()
    {
        registry.RunSnapshotPass(snapshot => registrations = snapshot.ToArray());
        templates.Clear();
        foreach (var group in registry.GetGroups())
        {
            templates[group.Id] = group;
            store.Own(HudOverlayPositionKeys.Group(group.Id));
            if (!store.HasSnapshot && !store.ReadOnly && !legacyRead.Contains(group.Id))
            {
                var native = gui.GetDialogPosition(HudOverlayPositionKeys.Group(group.Id));
                var tree = DefaultTree(group, registrations.Where(item => item.GroupId == group.Id));
                if (tree != null) legacyRead.Add(group.Id);
                if (native != null && tree != null) imported[group.Id] = new HudLayoutContainer { Id = group.Id,
                    Key = HudOverlayPositionKeys.Group(group.Id), TemplateId = group.Id, Tree = tree,
                    Padding = group.Packing.Padding, X = native.X, Y = native.Y, Movable = true };
            }
        }
        if (draggedIndicator != null && registrations.All(item => item.Id != draggedIndicator)) Cancel();
        Resolve();
    }

    /// <summary>Repairs mirrors on heartbeat rather than rendering or every pointer update.</summary>
    public void RepairMirrors()
    {
        store.Recover();
        store.RepairMirrors();
        Resolve();
    }

    /// <summary>Returns placement metadata for an effective container without rewriting feature registrations.</summary>
    public HudOverlayGroup Definition(HudLayoutContainer container)
    {
        var template = container.TemplateId != null ? templates.GetValueOrDefault(container.TemplateId) : null;
        return new HudOverlayGroup(container.Id, template?.Placement ?? new HudOverlayPlacement(HudOverlayTargets.Screen, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop),
            template?.Packing ?? new HudOverlayPacking(padding: container.Padding));
    }

    /// <summary>Supplies the same effective anchor selection to scheduler eligibility and native geometry.</summary>
    public string TargetId(HudOverlayRegistration registration)
    {
        var container = resolved.FirstOrDefault(item => item.Tree.Leaves().Contains(registration.Id, StringComparer.Ordinal));
        return container == null || container.X.HasValue ? HudOverlayTargets.Screen : Definition(container).Placement.TargetId;
    }

    /// <summary>Finds the effective owner even when its feature came from a different default group.</summary>
    public HudLayoutContainer? Owner(string indicator) => resolved.FirstOrDefault(item => item.Tree.Leaves().Contains(indicator, StringComparer.Ordinal));

    /// <summary>Reuses interaction state for a stable group or individual target identity.</summary>
    public HudOverlayGroupMovement Get(string containerId, string? indicator = null)
    {
        string target = indicator == null ? "container:" + containerId : "member:" + indicator;
        if (!movements.TryGetValue(target, out var movement)) movements[target] = movement = new HudOverlayGroupMovement(this, containerId, indicator);
        return movement;
    }
    #endregion

    /// <summary>Returns precise transient GUI-unit geometry, rounding only when the displayed origin is committed.</summary>
    public PointF? Origin(HudLayoutContainer container) => active == container.Id && previewOrigin.HasValue
        ? previewOrigin : container.X.HasValue ? new PointF(container.X.Value, container.Y!.Value) : null;

    /// <summary>Retains explicit docking or the registered default anchor until a free position overrides it.</summary>
    public HudOverlayPlacement? Attachment(HudLayoutContainer container)
    {
        var placement = Definition(container).Placement;
        if (container.Attachment != null) return container.Attachment;
        if (container.X.HasValue || !HudOverlayTargets.IsExternal(placement.TargetId)) return null;
        // Native screen alignment includes dialog margins. Capture that residual offset when materializing
        // its external attachment, so permission changes and incoming joins do not move the existing group.
        if (placement.TargetId == HudOverlayTargets.Screen)
        {
            float x = HudOverlayPoints.Normalized(placement.Attachment).X;
            double margin = x == 0 ? GuiStyle.LeftDialogMargin : x == 1 ? -GuiStyle.RightDialogMargin : 0;
            double scale = RuntimeEnv.GUIScale;
            // Before native GUI initialization there is no scaled margin; keep the unscaled placement valid.
            double offset = margin == 0 || !double.IsFinite(scale) || scale <= 0 ? 0 : margin / scale;
            return new HudOverlayPlacement(placement.TargetId, placement.Attachment, placement.Pivot,
                placement.OffsetX + offset, placement.OffsetY, placement.ScreenPaddingPixels);
        }
        return placement;
    }

    /// <summary>Reserves only the detached source remainder, never an incoming item or its joined destination.</summary>
    public HudOverlaySourceReservation? Reservation(HudLayoutContainer container)
    {
        if (sourceReservation == null || draggedIndicator == null) return null;
        var members = container.Tree.Leaves().ToArray();
        return members.Length > 0 && members.All(id => id != draggedIndicator && sourceReservationMembers.Contains(id))
            ? sourceReservation : null;
    }
    #region Transactions
    /// <summary>Changes permission only, capturing an anchored origin without moving content or changing membership.</summary>
    public bool SetMovable(string id, bool movable, PointF displayed)
    {
        Cancel();
        var container = resolved.FirstOrDefault(item => item.Id == id);
        if (container == null || ReadOnly) return false;
        // Permission toggles materialize a default placement without converting its attachment into a free position.
        var attachment = Attachment(container);
        return Save(Replace(Records(), container with { Movable = movable, X = (int)displayed.X, Y = (int)displayed.Y, Attachment = attachment }));
    }

    /// <summary>Captures complete effective membership for cancellation and previews independent detachment when requested.</summary>
    public bool Begin(string id, string? indicator, PointF displayed, IReadOnlyDictionary<string, PointF> displayedMembers, PointF? containerOrigin = null, HudOverlaySourceReservation? reservation = null)
    {
        if (ReadOnly || active != null) return false;
        var container = indicator == null ? resolved.FirstOrDefault(item => item.Id == id) : Owner(indicator);
        if (container == null || !container.Movable) return false;
        var records = Records().ToList();
        // Materialize defaults only for touched containers. Other defaults continue following live configuration.
        records.RemoveAll(item => item.Id == container.Id);
        if (indicator != null && container.Tree.Leaves().Count() > 1)
        {
            sourceReservation = reservation;
            sourceReservationMembers = container.Tree.Leaves().ToHashSet(StringComparer.Ordinal);
            var remainder = container.Tree.Remove(indicator)!;
            if (remainder.Indicator != null)
            {
                string remaining = remainder.Indicator;
                PointF origin = displayedMembers.GetValueOrDefault(remaining,
                    container.X.HasValue ? new PointF(container.X.Value, container.Y!.Value) : displayed);
                records.Add(Independent(remaining, origin, container.Movable) with { Attachment = container.Attachment, Padding = container.Attachment != null ? container.Padding : 0 });
            }
            else records.Add(container with { Tree = remainder, X = (int)(containerOrigin ?? displayed).X, Y = (int)(containerOrigin ?? displayed).Y });
            var detached = Independent(indicator, displayed, container.Movable);
            records.Add(detached);
            active = detached.Id;
        }
        else
        {
            records.Add(container with { X = (int)displayed.X, Y = (int)displayed.Y, Attachment = null });
            active = container.Id;
        }
        previewOrigin = displayed;
        draggedIndicator = indicator;
        preview = records.ToArray();
        dragBase = preview;
        dragContainer = active;
        newGroupId = Guid.NewGuid().ToString("N");
        Resolve();
        return true;
    }

    /// <summary>Updates only preview origin; publication and native persistence occur on successful release.</summary>
    public void Move(PointF position, HudOverlayPlacement? attachment = null)
    {
        if (preview == null || active == null) return;
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y)) throw new ArgumentOutOfRangeException(nameof(position));
        previewOrigin = position;
        preview = preview.Select(item => item.Id == active ? item with { Attachment = attachment } : item).ToArray();
        Resolve();
    }

    /// <summary>Restores the unsnapped detachment transaction before recalculating against current measured destinations.</summary>
    public void ClearSnap(PointF position)
    {
        if (dragBase == null) return;
        preview = dragBase;
        active = dragContainer;
        Move(position);
    }

    /// <summary>Publishes a validated destination tree in memory only; the source retires in the same release snapshot.</summary>
    public bool PreviewJoin(HudSnapCandidate candidate, PointF destinationOrigin)
    {
        if (dragBase == null || dragContainer == null || candidate.Target == null) return false;
        var incoming = resolved.FirstOrDefault(item => item.Id == dragContainer);
        var destination = resolved.FirstOrDefault(item => item.Id == candidate.Target.Container);
        if (incoming == null || destination == null || incoming.Id == destination.Id) return false;
        if (candidate.Target.Member != null && !destination.Tree.Leaves().Contains(candidate.Target.Member)) return false;
        var tree = HudGroupingResolver.Insert(destination.Tree, incoming.Tree, candidate.Target.Member,
            candidate.Side, candidate.Alignment, Definition(destination).Packing.Gap);
        bool independent = destination.Tree.Indicator != null;
        string id = independent ? newGroupId! : destination.Id;
        var joined = destination with { Id = id, Key = independent ? "vanillaexpanded:hud:custom:" + id : destination.Key,
            Tree = tree, X = (int)destinationOrigin.X, Y = (int)destinationOrigin.Y, Attachment = Attachment(destination) };
        var records = dragBase.Where(item => item.Id != incoming.Id && item.Id != destination.Id).Append(joined).ToArray();
        try { new HudLayoutSnapshot { Containers = records }.Validate(); }
        catch (InvalidOperationException) { return false; }
        preview = records;
        active = id;
        previewOrigin = new PointF(joined.X.Value, joined.Y!.Value);
        Resolve();
        return true;
    }

    /// <summary>Commits the displayed clamped integer origin with membership in one authoritative snapshot.</summary>
    public bool Commit(PointF displayed)
    {
        if (preview == null || active == null) return false;
        previewOrigin = displayed;
        var candidate = preview.Select(item => item.Id == active ? item with { X = (int)displayed.X, Y = (int)displayed.Y } : item).ToArray();
        bool result = Save(candidate);
        Cancel();
        return result;
    }

    /// <summary>Discards the complete preview without changing saved membership, mode, or origins.</summary>
    public void Cancel()
    {
        sourceReservation = null;
        sourceReservationMembers.Clear();
        preview = null;
        dragBase = null;
        dragContainer = newGroupId = null;
        previewOrigin = null;
        active = draggedIndicator = null;
        foreach (var movement in movements.Values.ToArray()) movement.Cancel();
        Resolve();
    }

    /// <summary>Restores all templates using an initialized reset snapshot, including unavailable saved integrations.</summary>
    public bool Reset()
    {
        Cancel();
        bool result = store.Reset();
        if (result) imported.Clear();
        Resolve();
        return result;
    }

    /// <summary>Returns the preview container identity for a member detached during its current drag.</summary>
    public string? ActiveContainer => active;
    #endregion
    #endregion

    #region Private
    /// <summary>Builds effective defaults from unassigned registrations, retaining unknown explicit leaves.</summary>
    private void Resolve()
    {
        var records = preview ?? Records();
        var result = records.ToList();
        var assigned = records.SelectMany(item => item.Tree.Leaves()).ToHashSet(StringComparer.Ordinal);
        foreach (var group in templates.Values)
        {
            var tree = DefaultTree(group, registrations.Where(item => item.GroupId == group.Id && !assigned.Contains(item.Id)));
            if (tree == null) continue;
            string id = group.Id;
            string key = HudOverlayPositionKeys.Group(group.Id);
            int suffix = 0;
            while (result.Any(item => item.Id == id || item.Key == key))
            {
                suffix++;
                string remainder = group.Id + (suffix == 1 ? "" : ":" + suffix);
                id = "default-remainder:" + remainder;
                key = "vanillaexpanded:hud:default-remainder:" + remainder;
            }
            store.Own(key);
            result.Add(new HudLayoutContainer { Id = id,
                Key = key, TemplateId = group.Id, Tree = tree, Padding = group.Packing.Padding });
        }
        resolved = result.ToArray();
    }

    /// <summary>Reads explicit records only; default membership remains derived unless touched or imported.</summary>
    private HudLayoutContainer[] Records() => store.HasSnapshot ? store.Current.Containers : store.ReadOnly ? Array.Empty<HudLayoutContainer>() : imported.Values.ToArray();

    /// <summary>Preserves unrelated records when a touched default becomes an explicit override.</summary>
    private static HudLayoutContainer[] Replace(HudLayoutContainer[] records, HudLayoutContainer container) =>
        records.Where(item => item.Id != container.Id).Append(container).ToArray();

    /// <summary>Creates a stable independent identity whose screen origin does not depend on its former anchor.</summary>
    private static HudLayoutContainer Independent(string indicator, PointF origin, bool movable) => new()
    {
        Id = "independent:" + indicator, Key = HudOverlayPositionKeys.Indicator(indicator),
        Tree = new HudLayoutNode { Indicator = indicator }, X = (int)origin.X, Y = (int)origin.Y, Movable = movable
    };

    /// <summary>Matches registered packing order independently of current enabled or drawable membership.</summary>
    private static HudLayoutNode? DefaultTree(HudOverlayGroup group, IEnumerable<HudOverlayRegistration> members) =>
        HudLayoutNode.FromMembers(members.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Id), group.Packing);

    /// <summary>Publishes accepted authoritative state and derives defaults again after either success or failure.</summary>
    private bool Save(HudLayoutContainer[] records)
    {
        bool result = store.Commit(new HudLayoutSnapshot { Containers = records });
        Resolve();
        return result;
    }
    #endregion
}
