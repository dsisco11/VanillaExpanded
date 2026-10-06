using Vintagestory.API.Common;

namespace VanillaExpanded.ItemRendering;

/// <summary>Owns validated radial presentation values independently of collectible GUI transforms.</summary>
internal sealed class ToolHeadPresentationProperties
{
    private readonly ModelTransform transform;

    #region Public API
    /// <summary>Copies a validated transform and records whether presentation follows the wedge direction.</summary>
    internal ToolHeadPresentationProperties(ModelTransform transform, float? wedgeRotationDegrees)
    {
        this.transform = transform.Clone();
        WedgeRotationDegrees = wedgeRotationDegrees;
    }

    /// <summary>Gets the clockwise wedge-relative angle, or null for screen-fixed presentation.</summary>
    public float? WedgeRotationDegrees { get; }

    /// <summary>Creates an owned engine transform that a draw can modify without changing resolved settings.</summary>
    public ModelTransform CreateModelTransform() => transform.Clone();
    #endregion
}
