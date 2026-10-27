using System.Drawing;
using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Owns stable sampled state and supplies independent bounded native presentations.</summary>
/// <remarks>All calls occur on the client main thread. Engine/API/inventory resources are borrowed.
/// No method handles input. Snapshots retain presentation copies rather than mutable live inventory state.
/// Native elements honor the assigned clip (including oversized content), restore temporary native draw state,
/// read only sampled/prepared state, and create no resources for unchanged content.
/// Final removal calls EndSession before Dispose; Dispose releases only owned resources, never borrowed ones.</remarks>
public interface IHudOverlay : IDisposable, INativePresentation
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
    /// <summary>Prepares stable presentation data and canonical measurements without allocating element-owned GUI resources.</summary>
    void Prepare(HudOverlayPreparationContext context);
    /// <summary>Measures finite, nonnegative dimensions in unscaled GUI units without another gameplay sample.</summary>
    SizeF Measure();
    #endregion
    #endregion
}
