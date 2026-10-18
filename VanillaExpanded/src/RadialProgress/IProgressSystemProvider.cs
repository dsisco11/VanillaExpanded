namespace VanillaExpanded.RadialProgress;

/// <summary>Provides ownership operations for interaction progress displays independently of rendering.</summary>
public interface IProgressSystemProvider
{
    #region Public API
    /// <summary>Creates a display with the caller's layering preference, or returns null when presentation is unavailable.</summary>
    IRadialProgressBar? CreateProgressBar(bool rendersTopmost);

    /// <summary>Releases a display owned by this provider.</summary>
    void RemoveProgressBar(IRadialProgressBar progressBar);
    #endregion
}
