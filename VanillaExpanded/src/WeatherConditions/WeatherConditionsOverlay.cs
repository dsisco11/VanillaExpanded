using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Samples local native weather and owns a passive icon presentation.</summary>
internal sealed class WeatherConditionsOverlay : IHudOverlay
{
    private readonly WeatherConditionsPresentation presentation;
    private ICoreClientAPI? api;
    private Vintagestory.GameContent.WeatherSystemClient? weather;
    private Vintagestory.API.Common.Entities.Entity? sampledEntity;
    private bool enabled = true;
    private bool disposed;
    internal WeatherConditionsSample? Sample { get; private set; }
    #region Public API
    #region Lifecycle
    /// <summary>Creates an icon-only presentation with optional headless asset loading.</summary>
    public WeatherConditionsOverlay(System.Func<HudOverlayPreparationContext, LoadedTexture[]>? load = null)
    {
        presentation = new WeatherConditionsPresentation(load);
    }
    /// <summary>Binds a borrowed weather provider for one client world.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A weather overlay session is already bound.");
        this.api = api;
        weather = api.ModLoader.GetModSystem<Vintagestory.GameContent.WeatherSystemClient>();
    }
    /// <summary>Immediately clears cached conditions when the live feature setting is disabled.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Releases borrowed weather and texture bindings at world exit.</summary>
    public void EndSession()
    {
        api = null;
        weather = null;
        Clear();
    }
    /// <summary>Removes presentation bindings exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        presentation.Dispose();
    }
    #endregion
    #region Sampling
    /// <summary>Requires a living local player and available client weather provider.</summary>
    public bool IsApplicable()
    {
        bool applicable = enabled && weather != null && api?.World.Player?.Entity?.Alive == true;
        if (!applicable) Clear();
        return applicable;
    }
    /// <summary>Copies native local climate and weather-only fog outside the drawing path.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var entity = api!.World.Player.Entity;
        var climate = api.World.BlockAccessor.GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.NowValues);
        var snapshot = weather!.BlendedWeatherData;
        if (climate == null || snapshot == null)
        {
            bool changed = Sample != null;
            Clear();
            return changed ? HudOverlayChange.Presentation | HudOverlayChange.Measurement : HudOverlayChange.None;
        }
        // The native weather modifier excludes unrelated underwater and temporal scene effects.
        var density = snapshot.Ambient?.FogDensity;
        float fog = (density?.Value ?? 0) * (density?.Weight ?? 0);
        var next = WeatherConditionsSample.FromWeather(climate.Rainfall, snapshot.BlendedPrecType,
            climate.Temperature, snapshot.snowThresholdTemp, fog, ReferenceEquals(sampledEntity, entity) ? Sample : null);
        sampledEntity = entity;
        if (next == Sample) return HudOverlayChange.None;
        Sample = next;
        if (next.Precipitation.HasValue || next.Fog) presentation.SetContent(next);
        else presentation.Reset();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Prepares cached artwork only while conditions are active.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample?.Precipitation != null || Sample?.Fog == true) presentation.Prepare(context);
    }
    /// <summary>Measures active icon slots or zero dimensions for fair weather.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Draws prepared condition icons under the host clip without native sampling.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        presentation.Draw(renderer, bounds);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears entity-specific transition history and borrowed presentation bindings.</summary>
    private void Clear()
    {
        sampledEntity = null;
        Sample = null;
        presentation.Reset();
    }
    #endregion
}
