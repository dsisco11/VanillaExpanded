using System;
using System.Drawing;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Mounts unique component elements into one composer without owning gameplay state.</summary>
/// <remarks>The host prepares and resolves layout before either render pass. Mounted elements belong
/// to the composer; Dispose releases only remaining view bookkeeping or unmounted resources.
/// Elements isolate rendering failures, restore native state and never consume input.</remarks>
public interface INativeView : IDisposable
{
    #region Public API
    #region Composition and preparation
    /// <summary>Mounts once under native member bounds with unique prefixed component keys.</summary>
    void AddTo(GuiComposer composer, ElementBounds memberBounds, string keyPrefix);
    /// <summary>Updates changed resources/local bounds from already-prepared feature state.</summary>
    void Prepare(HudOverlayPreparationContext context);
    /// <summary>Caches fitting, effective framebuffer clipping and backdrop geometry after layout.</summary>
    void LayoutCompleted(RectangleF clip);
    #endregion
    #region Rendering state
    /// <summary>Sets the prepared host visibility times drag multiplier without regenerating resources.</summary>
    void SetOpacity(float opacity);
    /// <summary>Submits cached backdrop targets without measurement, fitting or bounds calculation.</summary>
    void CollectBackdrops(IRenderAPI renderer);
    #endregion
    #endregion
}
