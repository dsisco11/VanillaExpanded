using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Registration;

/// <summary>Borrowed native preparation access plus values used to invalidate presentation resources.</summary>
public sealed record HudOverlayPreparationContext
{
    public ICoreClientAPI Api { get; }
    public double GuiScale { get; }
    /// <summary>Gets the content multiplier applied in addition to the game's GUI scale.</summary>
    public double ContentScale { get; }
    public string Locale { get; }
    public long FontRevision { get; }
    #region Public API
    /// <summary>Creates a preparation context; overlays own only resources they allocate through the borrowed API.</summary>
    public HudOverlayPreparationContext(ICoreClientAPI api, double guiScale, string locale, long fontRevision = 0,
        double contentScale = 1)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!double.IsFinite(guiScale) || guiScale <= 0) throw new ArgumentOutOfRangeException(nameof(guiScale));
        if (!double.IsFinite(contentScale) || contentScale <= 0 || !double.IsFinite(guiScale * contentScale))
            throw new ArgumentOutOfRangeException(nameof(contentScale));
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        Api = api;
        ContentScale = contentScale;
        GuiScale = guiScale * contentScale;
        Locale = locale;
        FontRevision = fontRevision;
    }
    #endregion
}
