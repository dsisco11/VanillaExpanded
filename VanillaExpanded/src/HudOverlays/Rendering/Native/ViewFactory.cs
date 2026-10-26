using System;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Selects optional native capability or the explicit external compatibility boundary.</summary>
internal static class ViewFactory
{
    #region Public API
    /// <summary>Creates an independent visual instance; built-ins can never silently fall back to legacy dispatch.</summary>
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
        if (overlay.GetType().Assembly == typeof(ViewFactory).Assembly)
            throw new InvalidOperationException("Built-in HUD indicators require native presentation capability.");
        return new LegacyView(api, overlay, role, failure);
    }
    #endregion
}
