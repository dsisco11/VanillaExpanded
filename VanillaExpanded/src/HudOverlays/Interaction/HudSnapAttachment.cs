using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Anchoring;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Translates snap intent into external placement metadata without owning layout resolution.</summary>
internal static class HudSnapAttachment
{
    #region Public API
    /// <summary>Captures the resolved snap without a position jump; future measurements keep its attachment point fixed.</summary>
    public static HudOverlayPlacement? FromSnap(HudSnapCandidate candidate, RectangleF incoming, RectangleF viewport, double scale)
    {
        if (candidate.Grouping || candidate.Boundary == null && !candidate.Identity.StartsWith("align:screen:", StringComparison.Ordinal)) return null;
        var target = candidate.Boundary?.Bounds ?? viewport;
        int x = Nearest(target.Left, target.Width, incoming.Left, incoming.Width);
        int y = Nearest(target.Top, target.Height, incoming.Top, incoming.Height);
        // Equal-sized surfaces make all current coordinates identical; retain the selected guide's intent.
        if (!candidate.Exterior)
        {
            if (candidate.Axis == HudSnapAxis.X) x = (int)candidate.GuideAlignment;
            else y = (int)candidate.GuideAlignment;
        }
        int px = x, py = y;
        if (candidate.Exterior)
        {
            switch (candidate.Side)
            {
                case HudSnapSide.Left: x = 0; px = 2; break;
                case HudSnapSide.Right: x = 2; px = 0; break;
                case HudSnapSide.Above: y = 0; py = 2; break;
                case HudSnapSide.Below: y = 2; py = 0; break;
            }
        }
        return new HudOverlayPlacement(candidate.Boundary == null ? HudOverlayTargets.Screen : HudOverlayTargets.Target(candidate.Boundary.Id),
            (HudOverlayPoint)(y * 3 + x), (HudOverlayPoint)(py * 3 + px),
            (incoming.Left + px * .5 * incoming.Width - target.Left - x * .5 * target.Width) / scale,
            (incoming.Top + py * .5 * incoming.Height - target.Top - y * .5 * target.Height) / scale, 0);
    }
    #endregion

    #region Private
    /// <summary>Chooses the closest common start, center, or end alignment while preserving residual offset.</summary>
    private static int Nearest(float targetStart, float targetSize, float start, float size)
    {
        int best = 0;
        double distance = double.PositiveInfinity;
        for (int index = 0; index < 3; index++)
        {
            double current = Math.Abs(start + index * .5 * size - targetStart - index * .5 * targetSize);
            if (current < distance) { best = index; distance = current; }
        }
        return best;
    }
    #endregion
}
