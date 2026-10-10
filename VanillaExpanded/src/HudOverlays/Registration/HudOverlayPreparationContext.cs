using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Borrowed native preparation access plus values used to invalidate presentation resources.</summary>
internal sealed record HudOverlayPreparationContext
{
    public ICoreClientAPI Api { get; }
    public double GuiScale { get; }
    public string Locale { get; }
    public long FontRevision { get; }
    #region Public API
    /// <summary>Creates a preparation context; overlays own only resources they allocate through the borrowed API.</summary>
    public HudOverlayPreparationContext(ICoreClientAPI api, double guiScale, string locale, long fontRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!double.IsFinite(guiScale) || guiScale <= 0) throw new ArgumentOutOfRangeException(nameof(guiScale));
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        Api = api;
        GuiScale = guiScale;
        Locale = locale;
        FontRevision = fontRevision;
    }
    #endregion
}
