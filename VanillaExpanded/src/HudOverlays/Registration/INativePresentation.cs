using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Optionally supplies independent native visuals over one overlay's prepared data.</summary>
/// <remarks>Implement alongside IHudOverlay. Creating views never samples gameplay or subscribes;
/// each composer receives fresh elements and owned resources. The host calls BeginFrame once per
/// feature, then all its views read the same visual effect state.</remarks>
public interface INativePresentation
{
    #region Public API
    /// <summary>Creates one unmounted visual instance borrowing this controller's stable prepared state.</summary>
    INativeView CreateView(ICoreClientAPI api, NativeViewRole role);
    /// <summary>Advances shared visual effects without sampling, preparation, measurement or layout.</summary>
    void BeginFrame(float deltaTime);
    #endregion
}
