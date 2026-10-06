using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.ItemRendering;

/// <summary>Checks the live engine GUI registry and projection without assuming custom delegates honor matrices.</summary>
internal sealed class ToolHeadRendererEligibility
{
    private readonly ICoreClientAPI api;
    private readonly FieldInfo? registry;

    #region Public API
    /// <summary>Caches only registry metadata; renderer registrations remain live.</summary>
    internal ToolHeadRendererEligibility(ICoreClientAPI api)
    {
        this.api = api;
        registry = AccessTools.Field(api.Event.GetType(), "itemStackRenderersByTarget");
    }

    /// <summary>Allows only the ordinary mesh path under a finite Y-down orthographic projection.</summary>
    internal bool Supports(CollectibleObject collectible)
    {
        try
        {
            if (!IsGuiProjection(api.Render.CurrentProjectionMatrix)) return false;
            if (registry?.GetValue(api.Event) is not Dictionary<int, ItemRenderDelegate>[][] registrations) return false;
            int itemClass = (int)collectible.ItemClass;
            int target = (int)EnumItemRenderTarget.Gui;
            if ((uint)itemClass >= (uint)registrations.Length || registrations[itemClass] is not { } targets
                || (uint)target >= (uint)targets.Length || targets[target] is not { } renderers) return false;
            return !renderers.ContainsKey(collectible.Id);
        }
        catch (Exception error) when (error is MemberAccessException or TargetException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Rejects perspective, inverted, and skewed projections incompatible with screen-space composition.</summary>
    internal static bool IsGuiProjection(float[] projection)
    {
        if (projection.Length != 16) return false;
        foreach (float component in projection)
            if (!float.IsFinite(component)) return false;
        // The ordinary GUI projection is diagonal plus translation; its homogeneous row is affine.
        foreach (int index in new[] { 1, 2, 3, 4, 6, 7, 8, 9, 11 })
            if (projection[index] != 0) return false;
        return projection[0] > 0 && projection[5] < 0 && projection[10] != 0 && projection[15] == 1;
    }
    #endregion
}
