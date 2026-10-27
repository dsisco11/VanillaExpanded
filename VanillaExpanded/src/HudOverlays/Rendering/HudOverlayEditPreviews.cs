using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Prepares labeled edit-only stand-ins for registered indicators without drawable live content.</summary>
internal sealed class HudOverlayEditPreviews : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly HudOverlayRegistry registry;
    private readonly Func<HudOverlayRegistration, bool> liveDrawable;
    private readonly Func<bool> enabled;
    private readonly Action<string, Exception> diagnostic;
    private readonly Dictionary<string, (HudOverlayRegistration Registration, SizeF Size, string Label)> entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private bool disposed;

    #region Public API
    #region Preparation
    /// <summary>Borrows normal visibility without changing feature state; prepares only placeholder metadata.</summary>
    public HudOverlayEditPreviews(ICoreClientAPI api, HudOverlayRegistry registry, Func<HudOverlayRegistration, bool> liveDrawable,
        Func<bool> enabled, Action<string, Exception> diagnostic, Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? createText = null)
    {
        this.api = api;
        this.registry = registry;
        this.liveDrawable = liveDrawable;
        this.enabled = enabled;
        this.diagnostic = diagnostic;
        CreateText = createText;
    }

    /// <summary>Gates edit-only drawing immediately when pointer capture, global HUD visibility, or session availability changes.</summary>
    public bool Active => !disposed && enabled() && HudOverlayEditing.IsActive(api);

    /// <summary>Prepares missing live members on the heartbeat, including disabled or currently inapplicable registrations.</summary>
    public void Update(HudOverlayPreparationContext context)
    {
        if (!Active) { Clear(); return; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        registry.RunPass(registration =>
        {
            seen.Add(registration.Id);
            if (liveDrawable(registration)) { Remove(registration.Id); return; }
            try
            {
                string label = registration.HoverLabelKey == null ? registration.Id : Lang.GetL(context.Locale, registration.HoverLabelKey);
                // Compact proxies do not stretch a status row into unreadable clipped labels. Hover retains the full identity.
                var size = registration.ReservedSize ?? new SizeF(HudOverlaySizes.Standard, HudOverlaySizes.Standard);
                entries[registration.Id] = (registration, size, Lang.GetL(context.Locale, "vanillaexpanded:hud-edit-hidden", label));
                reported.Remove(registration.Id);
            }
            catch (Exception failure)
            {
                entries.Remove(registration.Id);
                if (reported.Add(registration.Id)) Report(registration.Id, failure);
            }
        }, failure => Report("edit preview binding", failure));
        foreach (string id in entries.Keys.Where(id => !seen.Contains(id)).ToArray()) Remove(id);
        reported.IntersectWith(seen);
    }

    #endregion
    #region Membership and identity
    /// <summary>Returns only current registration identities, preventing removed integrations from remaining edit targets.</summary>
    public IReadOnlyList<KeyValuePair<HudOverlayRegistration, SizeF>> GetMembers()
    {
        var result = new List<KeyValuePair<HudOverlayRegistration, SizeF>>();
        if (!Active) return result;
        registry.RunPass(registration =>
        {
            if (IsDrawable(registration)) result.Add(new(registration, entries[registration.Id].Size));
        }, failure => Report("edit preview membership", failure));
        return result;
    }

    /// <summary>Offers a prepared placeholder only while normal live content is absent.</summary>
    public bool IsDrawable(HudOverlayRegistration registration) => Active && !liveDrawable(registration)
        && entries.TryGetValue(registration.Id, out var entry) && ReferenceEquals(entry.Registration, registration);

    /// <summary>Provides the full hidden-item identity for hover text while the compact proxy uses initials.</summary>
    public string Label(HudOverlayRegistration registration) => entries[registration.Id].Label;

    /// <summary>Gets the optional per-element proxy glyph factory; mounted elements own every generated texture.</summary>
    public Func<HudOverlayPreparationContext, string, CairoFont, LoadedTexture>? CreateText { get; }

    /// <summary>Prepares compact lettering in the view's locale, including context changes between metadata heartbeats.</summary>
    public string Caption(HudOverlayRegistration registration, string locale) => Abbreviate(registration.HoverLabelKey == null
        ? registration.Id : Lang.GetL(locale, registration.HoverLabelKey));
    #endregion

    #region Lifetime
    /// <summary>Clears prepared edit metadata on session exit without disposing feature presentations.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Clear();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Produces a compact Unicode-safe name without requiring feature-specific preview metadata.</summary>
    private static string Abbreviate(string label)
    {
        var words = label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        if (words.Length > 1) return string.Concat(words.Take(3).Select(word => StringInfo.GetNextTextElement(word))).ToUpperInvariant();
        var name = new StringInfo(label);
        return name.SubstringByTextElements(0, Math.Min(3, name.LengthInTextElements)).ToUpperInvariant();
    }

    /// <summary>Retires one placeholder when live content returns or its registration changes.</summary>
    private void Remove(string id)
    {
        entries.Remove(id); reported.Remove(id);
    }

    /// <summary>Contains diagnostic failures so one edit placeholder cannot interrupt other registered items.</summary>
    private void Report(string id, Exception failure)
    {
        try { diagnostic(id, failure); } catch { }
    }

    /// <summary>Clears edit metadata after mouse capture without touching saved membership or normal feature state.</summary>
    private void Clear()
    {
        foreach (string id in entries.Keys.ToArray()) Remove(id);
        reported.Clear();
    }
    #endregion
}
