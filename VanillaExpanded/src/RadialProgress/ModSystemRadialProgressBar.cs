using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RadialProgress;

/// <summary>
/// ModSystem that manages radial progress bar renderers.
/// Similar to the base game's ModSystemProgressBar but for circular progress indicators.
/// </summary>
public sealed class ModSystemRadialProgressBar : ModSystem
{
    #region Constants
    private const string RendererNamePrefix = "vanillaexpanded-radialprogress-";
    private const float DefaultStartOffset = 0.25f; // Top (12 o'clock position)
    private const bool DefaultClockwise = true;
    #endregion

    #region Fields
    private ICoreClientAPI? capi;
    private readonly List<RadialProgressBarRenderer> activeRenderers = new();
    private readonly object lockObj = new();
    private int nextRendererId;
    private bool isDisposed;
    #endregion

    #region ModSystem Overrides
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Hook shader reload event
        api.Event.ReloadShader += OnReloadShaders;
    }

    public override void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (capi is not null)
        {
            capi.Event.ReloadShader -= OnReloadShaders;
        }

        // Remove all active renderers
        lock (lockObj)
        {
            foreach (var renderer in activeRenderers)
            {
                UnregisterAndDispose(renderer);
            }
            activeRenderers.Clear();
        }

        // Force cleanup of shared resources
        RadialProgressResources.ForceCleanup();

        base.Dispose();
    }
    #endregion

    #region Public API
    /// <summary>
    /// Creates and registers a new radial progress bar with default settings.
    /// Progress starts at top (12 o'clock) and fills clockwise.
    /// </summary>
    /// <returns>The progress bar interface for controlling the display.</returns>
    public IRadialProgressBar? AddProgressBar()
    {
        return AddProgressBar(DefaultStartOffset, DefaultClockwise);
    }

    /// <summary>
    /// Creates and registers a new radial progress bar with custom start position and direction.
    /// </summary>
    /// <param name="startOffset01">Start angle offset in [0,1] range. 0 = right (3 o'clock), 0.25 = top (12 o'clock), 0.5 = left (9 o'clock), 0.75 = bottom (6 o'clock).</param>
    /// <param name="clockwise">True for clockwise fill direction, false for counter-clockwise.</param>
    /// <returns>The progress bar interface for controlling the display, or null if creation failed.</returns>
    public IRadialProgressBar? AddProgressBar(float startOffset01, bool clockwise)
    {
        if (capi is null || isDisposed)
        {
            return null;
        }

        RadialProgressBarRenderer renderer;
        try
        {
            renderer = new RadialProgressBarRenderer(capi, startOffset01, clockwise);
        }
        catch (InvalidOperationException ex)
        {
            capi.Logger.Error("[VanillaExpanded] Failed to create radial progress bar: {0}", ex.Message);
            return null;
        }

        // Set default screen position (centered on crosshair)
        float size = 64f;
        renderer.SetRect(
            (capi.Render.FrameWidth - size) / 2f,
            (capi.Render.FrameHeight - size) / 2f,
            size,
            size
        );

        // Default ring settings
        renderer.SetRadii(inner: 0.7f, outer: 1.0f);

        lock (lockObj)
        {
            string rendererName = $"{RendererNamePrefix}{nextRendererId++}";
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Ortho, rendererName);
            activeRenderers.Add(renderer);
        }

        return renderer;
    }

    /// <summary>
    /// Removes and disposes a radial progress bar.
    /// </summary>
    /// <param name="progressBar">The progress bar to remove. If null or not managed by this system, does nothing.</param>
    public void RemoveProgressBar(IRadialProgressBar? progressBar)
    {
        if (progressBar is not RadialProgressBarRenderer renderer || capi is null)
        {
            return;
        }

        lock (lockObj)
        {
            if (activeRenderers.Remove(renderer))
            {
                UnregisterAndDispose(renderer);
            }
        }
    }

    /// <summary>
    /// Gets the number of currently active progress bars.
    /// </summary>
    public int ActiveCount
    {
        get
        {
            lock (lockObj)
            {
                return activeRenderers.Count;
            }
        }
    }
    #endregion

    #region Private Methods
    private void UnregisterAndDispose(RadialProgressBarRenderer renderer)
    {
        try
        {
            capi?.Event.UnregisterRenderer(renderer, EnumRenderStage.Ortho);
            renderer.Dispose();
        }
        catch (Exception ex)
        {
            capi?.Logger.Warning("[VanillaExpanded] Error disposing radial progress bar: {0}", ex.Message);
        }
    }

    private bool OnReloadShaders()
    {
        return RadialProgressResources.ReloadAllShaders();
    }
    #endregion
}
