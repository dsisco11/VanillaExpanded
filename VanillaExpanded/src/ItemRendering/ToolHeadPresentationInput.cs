using Vintagestory.API.Common;

namespace VanillaExpanded.ItemRendering;

/// <summary>Provides typed asset fields for engine JSON population of radial presentation settings.</summary>
internal sealed class ToolHeadPresentationInput
{
    #region Public API
    /// <summary>Gets or sets the engine transform seeded by the resolver before population.</summary>
    public ModelTransformNoDefaults? Transform { get; set; }

    /// <summary>Gets or sets the optional clockwise wedge-relative angle.</summary>
    public float? WedgeRotationDegrees { get; set; }
    #endregion
}
