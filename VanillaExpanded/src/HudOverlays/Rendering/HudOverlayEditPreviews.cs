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

/// <summary>Owns labeled edit-only stand-ins for registered indicators without drawable live content.</summary>
internal sealed class HudOverlayEditPreviews : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly HudOverlayRegistry registry;
    private readonly Func<HudOverlayRegistration, bool> liveDrawable;
    private readonly Func<bool> enabled;
    private readonly Action<string, Exception> diagnostic;
    private readonly Func<WorkshopGaugeText> createText;
    private readonly HudOverlayGroupHighlight highlight;
    private readonly Dictionary<string, (HudOverlayRegistration Registration, WorkshopGaugeText Text, SizeF Size, string Label)> entries = new(StringComparer.Ordinal);
    private readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private bool disposed;

    #region Public API
    #region Preparation and membership
    /// <summary>Borrows normal visibility without changing feature state; owns only placeholder lettering and decoration.</summary>
    public HudOverlayEditPreviews(ICoreClientAPI api, HudOverlayRegistry registry, Func<HudOverlayRegistration, bool> liveDrawable,
        Func<bool> enabled, Action<string, Exception> diagnostic, Func<WorkshopGaugeText>? createText = null)
    {
        this.api = api;
        this.registry = registry;
        this.liveDrawable = liveDrawable;
        this.enabled = enabled;
        this.diagnostic = diagnostic;
        this.createText = createText ?? (() => new WorkshopGaugeText());
        highlight = new HudOverlayGroupHighlight(api);
    }

    /// <summary>Gates edit-only drawing immediately when pointer capture, global HUD visibility, or session availability changes.</summary>
    public bool Active => !disposed && enabled() && !api.HideGuis && api.Input?.MouseGrabbed == false;

    /// <summary>Prepares missing live members on the heartbeat, including disabled or currently inapplicable registrations.</summary>
    public void Update(HudOverlayPreparationContext context)
    {
        if (!Active) { Clear(); return; }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        registry.RunPass(registration =>
        {
            seen.Add(registration.Id);
            if (liveDrawable(registration)) { Remove(registration.Id); return; }
            WorkshopGaugeText? text = null;
            try
            {
                if (entries.TryGetValue(registration.Id, out var previous) && ReferenceEquals(previous.Registration, registration)) text = previous.Text;
                else { Remove(registration.Id); text = createText(); }
                string label = registration.HoverLabelKey == null ? registration.Id : Lang.GetL(context.Locale, registration.HoverLabelKey);
                text.SetContent(Abbreviate(label));
                text.Prepare(context);
                // Compact proxies do not stretch a status row into unreadable clipped labels. Hover retains the full identity.
                var size = registration.ReservedSize ?? new SizeF(HudOverlaySizes.Standard, HudOverlaySizes.Standard);
                entries[registration.Id] = (registration, text, size, Lang.GetL(context.Locale, "vanillaexpanded:hud-edit-hidden", label));
                reported.Remove(registration.Id);
            }
            catch (Exception failure)
            {
                entries.Remove(registration.Id);
                try { text?.Dispose(); }
                catch (Exception cleanup) { Report(registration.Id, cleanup); }
                if (reported.Add(registration.Id)) Report(registration.Id, failure);
            }
        }, failure => Report("edit preview binding", failure));
        foreach (string id in entries.Keys.Where(id => !seen.Contains(id)).ToArray()) Remove(id);
        reported.IntersectWith(seen);
    }

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
    #endregion

    #region Drawing and lifetime
    /// <summary>Draws cached edit lettering and an outline without feature calls, visibility animations, or fabricated readings.</summary>
    public void Draw(HudOverlayRegistration registration, IRenderAPI renderer, ElementBounds bounds)
    {
        if (!IsDrawable(registration)) return;
        var rectangle = new RectangleF((float)bounds.renderX, (float)bounds.renderY, (float)bounds.OuterWidth, (float)bounds.OuterHeight);
        highlight.Draw(rectangle, false, true);
        entries[registration.Id].Text.Draw(renderer, rectangle, 50);
    }

    /// <summary>Releases all edit-owned resources on session exit without disposing feature presentations.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Clear();
        highlight.Dispose();
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
        if (entries.Remove(id, out var entry))
        {
            try { entry.Text.Dispose(); }
            catch (Exception failure) { Report(id, failure); }
        }
        reported.Remove(id);
    }

    /// <summary>Contains diagnostic failures so one edit placeholder cannot interrupt other registered items.</summary>
    private void Report(string id, Exception failure)
    {
        try { diagnostic(id, failure); } catch { }
    }

    /// <summary>Retires edit resources after mouse capture without touching saved membership or normal feature state.</summary>
    private void Clear()
    {
        foreach (string id in entries.Keys.ToArray()) Remove(id);
        reported.Clear();
    }
    #endregion
}
