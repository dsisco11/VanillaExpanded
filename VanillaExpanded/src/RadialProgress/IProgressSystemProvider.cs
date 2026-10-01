namespace VanillaExpanded.RadialProgress;

/// <summary>Provides ownership operations for interaction progress displays independently of rendering.</summary>
public interface IProgressSystemProvider
{
    #region Public API
    /// <summary>Creates a display, or returns null when progress presentation is unavailable.</summary>
    IRadialProgressBar? CreateProgressBar();

    /// <summary>Releases a display owned by this provider.</summary>
    void RemoveProgressBar(IRadialProgressBar progressBar);
    #endregion
}
