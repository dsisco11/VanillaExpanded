using System.Drawing;
using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Owns one arbitrary bounded passive presentation and its stable sampled state.</summary>
/// <remarks>All calls occur on the client main thread. Engine/API/inventory resources are borrowed.
/// No method handles input. Snapshots retain presentation copies rather than mutable live inventory state.
/// Drawing honors the assigned clip (including oversized content), restores temporary native draw state,
/// reads only sampled/prepared state, and creates no resources for unchanged content.
/// Final removal calls EndSession before Dispose; Dispose releases only owned resources, never borrowed ones.</remarks>
internal interface IHudOverlay : IDisposable
{
    #region Public API
    #region Lifecycle
    /// <summary>Binds a fresh session to an inactive, undisposed instance with no previous sample/resources.</summary>
    void BeginSession(ICoreClientAPI api);
    /// <summary>Idempotently detaches subscriptions and releases session resources, including partial initialization.</summary>
    void EndSession();
    #endregion
    #region Sampling
    /// <summary>Cheaply tests feature applicability without expensive sampling or preparation.</summary>
    bool IsApplicable();
    /// <summary>Publishes stable feature-owned content; reports presentation and measurement invalidation separately.</summary>
    HudOverlayChange Refresh();
    #endregion
    #region Presentation
    /// <summary>Prepares owned presentation resources for sampled content; reuses them when context/content is unchanged.</summary>
    void Prepare(HudOverlayPreparationContext context);
    /// <summary>Measures finite, nonnegative dimensions in unscaled GUI units without another gameplay sample.</summary>
    SizeF Measure();
    /// <summary>Draws prepared state within native bounds and a finite framebuffer-pixel clip with nonnegative dimensions; performs no sampling or input.</summary>
    void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime);
    #endregion
    #endregion
}
