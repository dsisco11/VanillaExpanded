namespace VanillaExpanded.RadialProgress;

/// <summary>
/// Interface for a radial progress bar that can be managed by <see cref="ModSystemRadialProgressBar"/>.
/// </summary>
public interface IRadialProgressBar
{
    /// <summary>
    /// Progress value from 0 (empty) to 1 (full).
    /// </summary>
    float Progress { get; set; }

    /// <summary>
    /// Outer radius of the ring, from 0 to 1 (1 = edge of inscribed circle).
    /// </summary>
    float OuterRadius { get; set; }

    /// <summary>
    /// Inner radius of the ring, from 0 to 1 (0 = solid disc, >0 = ring).
    /// </summary>
    float InnerRadius { get; set; }

    /// <summary>
    /// Whether this progress bar is currently enabled and rendering.
    /// </summary>
    bool Enabled { get; set; }
}
