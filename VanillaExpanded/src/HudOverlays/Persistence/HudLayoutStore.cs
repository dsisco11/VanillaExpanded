using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.HudOverlays.Persistence;

/// <summary>Commits one authoritative native string setting before repairing disposable coordinate mirrors.</summary>
internal sealed class HudLayoutStore
{
    public const string SettingKey = "vanillaexpanded:hud:layout";
    private readonly ISettingsClass<string> settings;
    private readonly IGuiAPI gui;
    private readonly Action<Exception>? diagnostic;
    private readonly HashSet<string> owned = new(StringComparer.Ordinal);
    private string? serialized;
    private bool reported;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 512 };
    public HudLayoutSnapshot Current { get; private set; } = new();
    public bool HasSnapshot { get; private set; }
    public bool ReadOnly { get; private set; }
    public bool Uncertain { get; private set; }

    #region Public API
    #region Binding and ownership
    /// <summary>Reads without rewriting malformed or newer data; defaults remain available read-only.</summary>
    public HudLayoutStore(ISettingsClass<string> settings, IGuiAPI gui, Action<Exception>? diagnostic = null)
    {
        this.settings = settings;
        this.gui = gui;
        this.diagnostic = diagnostic;
        try
        {
            serialized = settings.Get(SettingKey);
            if (serialized == null) return;
            Current = JsonSerializer.Deserialize<HudLayoutSnapshot>(serialized, Json) ?? throw new InvalidOperationException("Empty HUD snapshot.");
            Current.Validate();
            HasSnapshot = true;
            foreach (var container in Current.Containers) owned.Add(container.Key);
            owned.UnionWith(Current.RetiredKeys);
            RepairMirrors();
        }
        catch (Exception failure) { Current = new(); ReadOnly = true; Report(failure); }
    }

    /// <summary>Tracks only namespace-validated keys derived from registrations or accepted state.</summary>
    public void Own(string key)
    {
        if (!HudLayoutSnapshot.OwnedKey(key)) throw new ArgumentException("Not a VE layout key.", nameof(key));
        owned.Add(key);
    }

    #endregion
    #region Snapshot transactions
    /// <summary>Validates and freezes a candidate, committing the snapshot before any mirror can change.</summary>
    public bool Commit(HudLayoutSnapshot candidate, bool reset = false)
    {
        if (Uncertain || ReadOnly && !reset) return false;
        string value;
        try
        {
            candidate = candidate with { Revision = reset ? 0 : checked(Current.Revision + 1),
                RetiredKeys = owned.Union(candidate.RetiredKeys).Distinct(StringComparer.Ordinal).ToArray() };
            candidate.Validate();
            value = JsonSerializer.Serialize(candidate, Json);
            // Freeze public input arrays before publishing them as the committed state.
            candidate = JsonSerializer.Deserialize<HudLayoutSnapshot>(value, Json)!;
        }
        catch (Exception failure) { Report(failure); return false; }
        try { settings[SettingKey] = value; }
        catch (Exception failure)
        {
            Report(failure);
            try
            {
                string? actual = settings.Get(SettingKey);
                if (actual != value)
                {
                    if (actual != serialized) { Uncertain = true; ReadOnly = true; }
                    return false;
                }
            }
            catch (Exception readFailure) { Uncertain = true; ReadOnly = true; Report(readFailure); return false; }
        }
        serialized = value;
        Current = candidate;
        HasSnapshot = true;
        ReadOnly = false;
        foreach (var container in Current.Containers) owned.Add(container.Key);
        RepairMirrors();
        return true;
    }

    /// <summary>Commits an initialized empty layout before clearing known mirrors, including unavailable members.</summary>
    public bool Reset() => Commit(new HudLayoutSnapshot(), reset: true);

    #endregion
    #region Recovery
    /// <summary>Resolves an ambiguous settings assignment before permitting another interaction or write.</summary>
    public void Recover()
    {
        if (!Uncertain) return;
        string? actual;
        try { actual = settings.Get(SettingKey); }
        catch (Exception failure) { Report(failure); return; }
        try
        {
            var snapshot = actual == null ? new HudLayoutSnapshot() : JsonSerializer.Deserialize<HudLayoutSnapshot>(actual, Json)!;
            snapshot.Validate();
            Current = snapshot;
            serialized = actual;
            HasSnapshot = actual != null;
            ReadOnly = false;
            foreach (var container in Current.Containers) owned.Add(container.Key);
            owned.UnionWith(Current.RetiredKeys);
        }
        catch (Exception failure)
        {
            Current = new(); HasSnapshot = false; serialized = actual;
            ReadOnly = true; Report(failure);
        }
        Uncertain = false;
    }

    /// <summary>Retries idempotent mirrors at a safe boundary; accepted snapshot state is never rolled back.</summary>
    public void RepairMirrors()
    {
        if (!HasSnapshot || ReadOnly || Uncertain) return;
        var positions = Current.Containers.ToDictionary(container => container.Key, StringComparer.Ordinal);
        foreach (string key in owned.ToArray())
        {
            try
            {
                Vec2i? expected = positions.TryGetValue(key, out var container) && container.X.HasValue
                    ? new Vec2i(container.X.Value, container.Y!.Value) : null;
                var actual = gui.GetDialogPosition(key);
                if (actual?.X != expected?.X || actual?.Y != expected?.Y) gui.SetDialogPosition(key, expected!);
            }
            catch (Exception failure) { Report(failure); }
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Bounds diagnostics independently of interaction retries and native settings failure behavior.</summary>
    private void Report(Exception failure)
    {
        if (reported) return;
        reported = true;
        try { diagnostic?.Invoke(failure); } catch { }
    }
    #endregion
}
