using System;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Creates independent native visuals and binds their feature failure boundary.</summary>
internal static class ViewFactory
{
    #region Public API
    /// <summary>Creates an independent visual instance from the native presentation capability.</summary>
    public static INativeView Create(IHudOverlay overlay, ICoreClientAPI api,
        NativeViewRole role, Action<Exception> failure)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(failure);
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (overlay is INativePresentation native)
        {
            var view = native.CreateView(api, role) ?? throw new InvalidOperationException("Native capability returned no view.");
            try
            {
                if (view is NativeView owned) owned.SetFailureHandler(failure);
                return view;
            }
            catch { view.Dispose(); throw; }
        }
        throw new InvalidOperationException("HUD indicators require native presentation capability.");
    }
    #endregion
}
