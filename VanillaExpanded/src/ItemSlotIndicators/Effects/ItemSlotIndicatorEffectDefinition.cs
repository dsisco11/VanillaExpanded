using System;
using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Describes an immutable indicator effect independently of GPU resources and provider sampling.</summary>
/// <remarks>Effect declarations validate their shader-specific parameter ranges and unused lanes before construction.</remarks>
internal sealed record ItemSlotIndicatorEffectDefinition
{
    /// <summary>The supported mesh and shader input contract version.</summary>
    internal const int CurrentAbiVersion = 1;
    /// <summary>Prefix reserved for indicator shader registration names and asset basenames.</summary>
    internal const string ShaderNamePrefix = "vanillaexpanded_itemslot_";
    /// <summary>Default number of equal-width segments for a deformable fill strip.</summary>
    internal const int DefaultSegmentCount = 16;
    /// <summary>Maximum number of segments accepted by the shared geometry contract.</summary>
    internal const int MaximumSegmentCount = 64;

    #region Public API
    /// <summary>Creates a validated effect description; shader assets may be unavailable until resource preparation.</summary>
    /// <param name="id">Canonical lower-case namespace:path identifier; path segments use letters, digits, underscores, or hyphens.</param>
    /// <param name="shaderAssetDomain">Canonical lower-case asset domain without path separators.</param>
    /// <param name="shaderName">Shared .vsh/.fsh basename, including the reserved prefix and excluding extensions.</param>
    /// <param name="topology">Reusable indexed triangle geometry required by the effect.</param>
    /// <param name="segmentCount">One for a quad, or two through sixty-four for a fill strip.</param>
    /// <param name="parameters">Finite, copied values whose meaning is defined by the effect's shader contract.</param>
    /// <param name="needsCameraMotion">Whether the effect consumes the shared camera-motion signal.</param>
    /// <param name="abiVersion">Mesh and shader input contract version; only version one is supported.</param>
    internal ItemSlotIndicatorEffectDefinition(string id, string shaderAssetDomain, string shaderName,
        ItemSlotIndicatorTopology topology = ItemSlotIndicatorTopology.FillStrip,
        int segmentCount = DefaultSegmentCount, Vector4 parameters = default, bool needsCameraMotion = false,
        int abiVersion = CurrentAbiVersion)
    {
        ValidateId(id);
        if (!IsCanonicalName(shaderAssetDomain))
            throw new ArgumentException("Shader asset domains must use lower-case letters, digits, underscores, or hyphens.", nameof(shaderAssetDomain));
        if (shaderName is null || !shaderName.StartsWith(ShaderNamePrefix, StringComparison.Ordinal)
            || shaderName.Length == ShaderNamePrefix.Length || !IsCanonicalName(shaderName))
            throw new ArgumentException("Shader basenames must use the reserved indicator prefix followed by a canonical name.", nameof(shaderName));
        if (abiVersion != CurrentAbiVersion)
            throw new ArgumentOutOfRangeException(nameof(abiVersion), "Unsupported indicator shader contract version.");

        // Reject unsupported geometry before it can become part of a provider registration.
        switch (topology)
        {
            case ItemSlotIndicatorTopology.Quad:
                if (segmentCount != 1)
                    throw new ArgumentOutOfRangeException(nameof(segmentCount), "A quad requires exactly one segment.");
                break;
            case ItemSlotIndicatorTopology.FillStrip:
                if (segmentCount is < 2 or > MaximumSegmentCount)
                    throw new ArgumentOutOfRangeException(nameof(segmentCount), "A fill strip requires two through sixty-four segments.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(topology), "Unsupported indicator geometry.");
        }
        if (!float.IsFinite(parameters.X) || !float.IsFinite(parameters.Y)
            || !float.IsFinite(parameters.Z) || !float.IsFinite(parameters.W))
            throw new ArgumentOutOfRangeException(nameof(parameters), "Effect parameters must be finite.");

        Id = id;
        ShaderAssetDomain = shaderAssetDomain;
        ShaderName = shaderName;
        AbiVersion = abiVersion;
        Topology = topology;
        SegmentCount = segmentCount;
        Parameters = parameters;
        NeedsCameraMotion = needsCameraMotion;
    }

    /// <summary>Gets the stable, canonical effect identifier.</summary>
    internal string Id { get; }
    /// <summary>Gets the domain containing the vertex and fragment shader assets.</summary>
    internal string ShaderAssetDomain { get; }
    /// <summary>Gets the shared shader asset basename and engine registration name.</summary>
    internal string ShaderName { get; }
    /// <summary>Gets the supported mesh and shader input contract version.</summary>
    internal int AbiVersion { get; }
    /// <summary>Gets the reusable geometry layout.</summary>
    internal ItemSlotIndicatorTopology Topology { get; }
    /// <summary>Gets the fixed number of equal-width mesh segments.</summary>
    internal int SegmentCount { get; }
    /// <summary>Gets the copied effect-specific parameter block.</summary>
    internal Vector4 Parameters { get; }
    /// <summary>Gets whether shared camera-motion input is needed.</summary>
    internal bool NeedsCameraMotion { get; }

    /// <summary>Rejects incompatible effect identities or engine shader-name reuse before registry mutation.</summary>
    internal void ValidateCompatibility(ItemSlotIndicatorEffectDefinition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        // An effect ID describes the complete appearance, while program sharing excludes per-draw inputs.
        if (Id == other.Id && this != other)
            throw new ArgumentException($"Indicator effect '{Id}' already has a different definition.", nameof(other));
        if (ShaderName == other.ShaderName
            && (ShaderAssetDomain != other.ShaderAssetDomain || AbiVersion != other.AbiVersion))
            throw new ArgumentException($"Indicator shader '{ShaderName}' already refers to a different program.", nameof(other));
    }
    #endregion

    #region Private
    /// <summary>Validates one explicit namespace and nonempty, canonical path segments without normalizing identity.</summary>
    private static void ValidateId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        int separator = id.IndexOf(':');
        if (separator <= 0 || separator != id.LastIndexOf(':') || !IsCanonicalName(id.AsSpan(0, separator)))
            throw new ArgumentException("Effect IDs require one canonical namespace and a path.", nameof(id));

        // Validate each path segment so slashes cannot disguise empty or traversal components.
        ReadOnlySpan<char> path = id.AsSpan(separator + 1);
        while (true)
        {
            int slash = path.IndexOf('/');
            ReadOnlySpan<char> segment = slash < 0 ? path : path[..slash];
            if (!IsCanonicalName(segment))
                throw new ArgumentException("Effect paths require nonempty canonical name segments.", nameof(id));
            if (slash < 0) return;
            path = path[(slash + 1)..];
        }
    }

    /// <summary>Accepts an ASCII lower-case name suitable for stable identity and shader file lookup.</summary>
    private static bool IsCanonicalName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty) return false;
        foreach (char character in name)
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-')) return false;
        return true;
    }
    #endregion
}
