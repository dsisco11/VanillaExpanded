using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.BodyTemperature;

/// <summary>Samples synchronized temperature warnings and owns its cached passive temperature presentation.</summary>
internal sealed class BodyTemperatureOverlay : IHudOverlay
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

    #region Public API
    #region Lifecycle
    /// <summary>Creates a dynamic thermometer and compact whole-degree text with preparation outside the render path.</summary>
    public BodyTemperatureOverlay(HudOverlayIconTextPresentation? presentation = null,
        System.Func<HudOverlayPreparationContext, LoadedTexture[]>? createIconTextures = null)
    {
        thermometer = new BodyTemperatureThermometerPresentation(presentation ?? new(fontSize: 14), createIconTextures);
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
    /// <summary>Suspends owned warning resources immediately while retaining the client session.</summary>
    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) Clear();
        return value;
    }
    /// <summary>Releases sampled state and native textures on world exit.</summary>
    public void EndSession()
    {
        api = null;
        Clear();
        sampledEntity = null;
    }
    /// <summary>Finally releases owned presentation resources exactly once.</summary>
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
        if (Sample == next && !cleared) return HudOverlayChange.None;
        cleared = false;
        Sample = next;
        if (next == null) thermometer.Reset();
        else thermometer.SetContent(next, showReading);
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }
    #endregion
    #region Presentation
    /// <summary>Prepares thermometer artwork and optional localized text for a visible warning.</summary>
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sample != null) thermometer.Prepare(context);
    }
    /// <summary>Returns zero dimensions while healthy or cached thermometer dimensions during a warning.</summary>
    public SizeF Measure() => Sample == null ? SizeF.Empty : thermometer.Size;
    /// <summary>Draws cached thermometer layers and optional text without sampling or generating textures.</summary>
    public void Draw(IRenderAPI renderer, ElementBounds bounds, RectangleF clip, float deltaTime)
    {
        if (Sample != null) thermometer.Draw(renderer, bounds, clip, deltaTime);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Clears stale readings and their owned texture when the player becomes ineligible.</summary>
    private void Clear()
    {
        sampledEntity = null;
        trend.Reset();
        if (Sample == null) return;
        Sample = null;
        cleared = true;
        thermometer.Reset();
    }
    #endregion
}
