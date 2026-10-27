using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.WeatherConditions;

/// <summary>Samples local native weather and owns a passive icon presentation.</summary>
internal sealed class WeatherConditionsOverlay : IHudOverlay, INativePresentation, IHudOverlayVisibilityTransition
{
    private readonly WeatherConditionsPresentation presentation;
    private ICoreClientAPI? api;
    private Vintagestory.GameContent.WeatherSystemClient? weather;
    private Vintagestory.API.Common.Entities.Entity? sampledEntity;
    private bool enabled = true;
    private bool disposed;
    internal WeatherConditionsSample? Sample { get; private set; }
    /// <summary>Controls retained artwork during visibility changes.</summary>
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;

    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => Clear();
    private bool styleChanged;

    #region Public API
    /// <summary>Retains style invalidation until the next eligible content and layout refresh.</summary>
    public void SetStyle(HudIndicatorStyle style) => styleChanged |= presentation.SetStyle(style);

    #region Lifecycle
    /// <summary>Accepts prepared native weather state without transferring element ownership to the controller.</summary>
    public WeatherConditionsOverlay(WeatherConditionsPresentation? presentation = null)
    {
        this.presentation = presentation ?? new();
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
    /// <summary>Clears sampled conditions and starts the artwork exit when the feature is disabled.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Clears borrowed weather state and prepared presentation data at world exit.</summary>
    public void EndSession()
    {
        api = null;
        weather = null;
        Clear();
        presentation.Reset();
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
    /// <summary>Copies and classifies independent local weather channels outside the drawing path.</summary>
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
        var next = WeatherConditionsSampler.Sample(api, weather, snapshot, climate,
            ReferenceEquals(sampledEntity, entity) ? Sample : null);
        sampledEntity = entity;
        if (next == Sample && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        Sample = next;
        presentation.SetContent(next);
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Creates independent native visuals without sampling or subscribing.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);
    /// <summary>This indicator has no shared per-frame visual effect.</summary>
    public void BeginFrame(float deltaTime) { }

    /// <summary>Prepares layer identities and metrics for the latest valid weather sample.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample != null) presentation.Prepare(context);
    }
    /// <summary>Measures the composite slot, or zero when no supported artwork is visible.</summary>
    public SizeF Measure() => presentation.Size;
    /// <summary>Requires foreground rendering through the registered native components.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        throw new InvalidOperationException("Native indicator components render through their composer.");
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Clears entity-specific transition history while retaining prepared data for an exit.</summary>
    private void Clear()
    {
        sampledEntity = null;
        Sample = null;
        presentation.Hide();
    }
    #endregion
}
