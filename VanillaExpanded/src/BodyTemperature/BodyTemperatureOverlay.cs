using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Samples synchronized temperature warnings and owns its cached passive temperature presentation.</summary>
internal sealed class BodyTemperatureOverlay : IHudOverlay, INativePresentation, IHudOverlayVisibilityTransition
{
    private readonly BodyTemperatureThermometerPresentation thermometer;
    private bool showReading = true;
    private readonly BodyTemperatureTrend trend = new();
    private ICoreClientAPI? api;
    private Vintagestory.API.Common.Entities.Entity? sampledEntity;
    private bool disposed;
    private bool enabled = true;
    private bool cleared;
    internal BodyTemperatureSample? Sample { get; private set; }

    /// <summary>Controls retained artwork during visibility changes.</summary>
    public HudOverlayVisibilityTransition Visibility => thermometer.Visibility;

    /// <summary>Retains the last preparation for the shared host's exit transition.</summary>
    public void HidePresentation() => Clear();
    private bool styleChanged;

    #region Public API
    /// <summary>Retains style invalidation until the next eligible content and layout refresh.</summary>
    public void SetStyle(HudIndicatorStyle style) => styleChanged |= thermometer.SetStyle(style);

    #region Lifecycle
    /// <summary>Retains prepared native temperature data independently of composer-owned resources.</summary>
    public BodyTemperatureOverlay(BodyTemperatureThermometerPresentation? presentation = null)
    {
        thermometer = presentation ?? new();
    }
    /// <summary>Changes the reading preference and requests a fresh measurement on the next scheduled sample.</summary>
    public void SetShowReading(bool value)
    {
        if (showReading == value) return;
        showReading = value;
        cleared = true;
    }
    /// <summary>Binds a fresh client session without retaining a previous entity.</summary>
    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A temperature overlay session is already bound.");
        this.api = api;
    }
    /// <summary>Starts the warning exit transition while retaining the client session.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Clears sampled and prepared state on world exit.</summary>
    public void EndSession()
    {
        api = null;
        Clear();
        sampledEntity = null;
        thermometer.Reset();
    }
    /// <summary>Finally releases controller and prepared presentation state exactly once.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        thermometer.Dispose();
    }
    #endregion
    #region Sampling
    /// <summary>Rejects dead players and game modes whose native temperature behavior disables cold risk.</summary>
    public bool IsApplicable()
    {
        var player = api?.World.Player;
        bool applicable = enabled && player?.Entity?.Alive == true && player.WorldData.CurrentGameMode
            is not (EnumGameMode.Creative or EnumGameMode.Spectator);
        if (!applicable) Clear();
        return applicable;
    }
    /// <summary>Updates only changed temperature readings or risk bands, clearing recovered or unavailable readings.</summary>
    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var entity = api!.World.Player.Entity;
        // Recovery hysteresis belongs to one entity and must not survive player replacement.
        bool sameEntity = ReferenceEquals(sampledEntity, entity);
        if (!sameEntity) trend.Reset();
        var next = BodyTemperatureSample.Read(entity, sameEntity ? Sample : null);
        var tree = entity.WatchedAttributes.GetTreeAttribute("bodyTemp");
        int direction = 0;
        // Track healthy readings too, so entering a warning already has a valid rate baseline.
        if (tree?.HasAttribute("bodytemp") == true && tree.HasAttribute("bodyTempUpdateTotalHours"))
            direction = trend.Update(tree.GetFloat("bodytemp"), tree.GetDouble("bodyTempUpdateTotalHours"), api.World.ElapsedMilliseconds);
        else trend.Reset();
        if (next != null) next = next with { Trend = direction };
        sampledEntity = entity;
        if (Sample == next && !cleared && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        cleared = false;
        Sample = next;
        if (next == null) thermometer.Hide();
        else thermometer.SetContent(next, showReading);
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Creates independent native visuals without sampling or subscribing.</summary>
    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => thermometer.CreateView(api);
    /// <summary>This indicator has no shared per-frame visual effect.</summary>
    public void BeginFrame(float deltaTime) { }

    /// <summary>Prepares localized temperature data and metrics for a visible warning.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample != null) thermometer.Prepare(context);
    }
    /// <summary>Returns zero dimensions while healthy or cached thermometer dimensions during a warning.</summary>
    public SizeF Measure() => Sample == null ? SizeF.Empty : thermometer.Size;
    /// <summary>Requires foreground rendering through the registered native components.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        throw new InvalidOperationException("Native indicator components render through their composer.");
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Clears stale samples while retaining prepared data for a visible exit.</summary>
    private void Clear()
    {
        sampledEntity = null;
        trend.Reset();
        if (Sample == null) return;
        Sample = null;
        cleared = true;
        thermometer.Hide();
    }
    #endregion
}
