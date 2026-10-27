using System;
using System.Collections.Generic;
using System.Linq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Retains immutable starting geometry for a drag without owning feature resources or layout membership.</summary>
internal sealed class HudOverlayDragPreview
{
    /// <summary>Shared translucency for the original and moving copies.</summary>
    public const float Opacity = .55f;
    private HudOverlayGroupMovement? movement;
    private long revision;
    private bool groupingPreview;
    private HudOverlayMemberLayout[] originals = Array.Empty<HudOverlayMemberLayout>();

    #region Public API
    /// <summary>Gets source copies during free dragging, suppressing them while a destination group is previewed.</summary>
    public IReadOnlyList<HudOverlayMemberLayout> Originals => Active && !groupingPreview ? originals : Array.Empty<HudOverlayMemberLayout>();
    /// <summary>Retains source-view ownership while a reversible grouping preview suppresses its drawing.</summary>
    public IReadOnlyList<HudOverlayMemberLayout> Captured => Active ? originals : Array.Empty<HudOverlayMemberLayout>();

    /// <summary>Copies current bounds before input can detach, reflow, or merge the native layouts.</summary>
    public static HudOverlayMemberLayout[] Capture(IEnumerable<HudOverlayGroupLayout> groups) => groups
        .Where(group => group.Available).SelectMany(group => group.Members)
        .Select(member => new HudOverlayMemberLayout(member.Registration, new FrozenBounds(member.Bounds)) { Clip = member.Clip }).ToArray();

    /// <summary>Restricts source copies to the dragged member or complete dragged container.</summary>
    public void Begin(HudOverlayGroupMovement capture, IEnumerable<HudOverlayMemberLayout> snapshot)
    {
        movement = capture;
        revision = capture.DragRevision;
        groupingPreview = false;
        originals = snapshot.Where(member => capture.IndicatorId is { } id ? member.Registration.Id == id
            : capture.StartingMembers.Contains(member.Registration.Id)).ToArray();
    }

    /// <summary>Identifies moving content by registration identity, including during a grouping preview.</summary>
    public bool Contains(HudOverlayRegistration registration) => Active && originals.Any(member => ReferenceEquals(member.Registration, registration));

    /// <summary>Releases borrowed registrations as soon as cancellation, commit, or teardown ends the capture.</summary>
    public void Clear()
    {
        movement = null;
        groupingPreview = false;
        originals = Array.Empty<HudOverlayMemberLayout>();
    }

    /// <summary>Retains the source snapshot while suppressing its drawing during a reversible grouping preview.</summary>
    public void Refresh(bool groupingPreview = false)
    {
        if (!Active) { Clear(); return; }
        this.groupingPreview = groupingPreview;
    }
    #endregion

    #region Private
    /// <summary>Rejects completed or replaced movement transactions before exposing borrowed source content.</summary>
    private bool Active => movement?.Dragging == true && movement.DragRevision == revision;

    /// <summary>Preserves measured dimensions and absolute position independently of mutable layout parents.</summary>
    private sealed class FrozenBounds : ElementBounds
    {
        private readonly double x, y;
        #region Public API
        /// <summary>Gets the captured physical horizontal position.</summary>
        public override double renderX => x;
        /// <summary>Gets the captured physical vertical position.</summary>
        public override double renderY => y;

        /// <summary>Copies only the geometry consumed by prepared HUD presentations.</summary>
        public FrozenBounds(ElementBounds bounds)
        {
            x = bounds.renderX; y = bounds.renderY;
            ParentBounds = Empty;
            absFixedX = x; absFixedY = y;
            fixedWidth = bounds.fixedWidth; fixedHeight = bounds.fixedHeight;
            fixedPaddingX = bounds.fixedPaddingX; fixedPaddingY = bounds.fixedPaddingY;
            absInnerWidth = bounds.InnerWidth; absInnerHeight = bounds.InnerHeight;
            absPaddingX = (bounds.OuterWidth - bounds.InnerWidth) / 2;
            absPaddingY = (bounds.OuterHeight - bounds.InnerHeight) / 2;
            Initialized = true; requiresrelculation = false;
        }
        #endregion
    }
    #endregion
}
