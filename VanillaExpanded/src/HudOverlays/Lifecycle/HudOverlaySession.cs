using System;
using System.Collections.Generic;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Layout;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using VanillaExpanded.HudOverlays.Updating;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.HudOverlays.Lifecycle;

/// <summary>Owns ready-world heartbeat registration and fresh native host/scheduler bindings for each player session.</summary>
internal sealed class HudOverlaySession : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly HudOverlayRegistry registry;
    private readonly Func<HudOverlayScheduler, HudOverlayAnchorContext, IHudOverlayHost> createHost;
    private readonly Func<long> clock;
    private readonly Func<bool> enabled;
    private readonly Dictionary<string, HudOverlayGroupLayout> layouts = new(StringComparer.Ordinal);
    private long? tickId;
    private IClientWorldAccessor? world;
    private IClientPlayer? player;
    private object? playerEntity;
    private IHudOverlayHost? host;
    private HudOverlayAnchorContext? anchors;
    private bool disposed;
    public HudOverlayScheduler? Scheduler { get; private set; }

    #region Public API
    /// <summary>Stores borrowed services and a native-host creation boundary without allocating world resources.</summary>
    public HudOverlaySession(ICoreClientAPI api, HudOverlayRegistry registry,
        Func<HudOverlayScheduler, HudOverlayAnchorContext, IHudOverlayHost>? createHost = null,
        Func<long>? clock = null, Func<bool>? enabled = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(registry);
        this.api = api;
        this.registry = registry;
        this.clock = clock ?? (() => Environment.TickCount64);
        this.enabled = enabled ?? (() => true);
        this.createHost = createHost ?? ((scheduler, _) => new HudOverlayHost(api, registry,
            scheduler.IsDrawable, scheduler.ReportDrawFailure, scheduler.ReportBoundaryFailure));
    }

    /// <summary>Starts one ready-world heartbeat, including a cheap wait when the player is not available yet.</summary>
    public void EnterWorld()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LeaveWorld();
        tickId = api.Event.RegisterGameTickListener(OnTick, HudOverlayScheduler.HeartbeatMs);
        OnTick(0);
    }

    /// <summary>Stops heartbeat work and releases all session bindings; repeated world exit is safe.</summary>
    public void LeaveWorld()
    {
        try
        {
            if (tickId is { } listener)
            {
                tickId = null;
                api.Event.UnregisterGameTickListener(listener);
            }
        }
        finally { EndBindings(); }
    }

    /// <summary>Observes live selectors immediately while leaving expensive refreshes on the heartbeat.</summary>
    public void ConfigurationChanged()
    {
        if (disposed || anchors == null || Scheduler == null) return;
        bool visible = IsCurrentSession() && !api.HideGuis;
        anchors.BeginFrame(visible);
        Scheduler.ObserveVisibility(anchors, visible, enabled());
        SynchronizeLayouts();
    }

    /// <summary>Stops listeners and session resources without disposing registry-owned overlay instances.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        LeaveWorld();
    }
    #endregion

    #region Private
    #region Binding and teardown
    /// <summary>Rebinds replaced worlds/players before sampling; render callbacks never perform this work.</summary>
    private void OnTick(float deltaTime)
    {
        if (disposed || tickId == null) return;
        var currentWorld = api.World;
        var currentPlayer = currentWorld?.Player;
        if (currentWorld == null || currentPlayer?.Entity == null) { EndBindings(); return; }
        if (!ReferenceEquals(world, currentWorld) || !ReferenceEquals(player, currentPlayer)
            || !ReferenceEquals(playerEntity, currentPlayer.Entity))
        {
            EndBindings();
            world = currentWorld;
            player = currentPlayer;
            playerEntity = currentPlayer.Entity;
            // Failed individual bindings remain owned and hidden; unaffected registrations still run.
            try { registry.BeginSession(api); }
            catch (Exception failure) { Report("session binding", failure); }
            Scheduler = new HudOverlayScheduler(registry, clock, Report);
            try
            {
                anchors = new HudOverlayAnchorContext(api);
                host = createHost(Scheduler, anchors);
                host.BeforeRender = BeforeRender;
            }
            catch
            {
                EndBindings();
                throw;
            }
        }
        bool visible = !api.HideGuis;
        anchors!.BeginFrame(visible);
        var preparation = new HudOverlayPreparationContext(api, anchors.GuiScale,
            Lang.CurrentLocale ?? "en", FontRevision());
        Scheduler!.Update(anchors, preparation, visible, enabled());
        SynchronizeLayouts();
    }

    /// <summary>Closes the native host before disposing compositions and clearing borrowed player/world references.</summary>
    private void EndBindings()
    {
        var oldHost = host;
        host = null;
        Scheduler?.Reset();
        Scheduler = null;
        anchors = null;
        world = null;
        player = null;
        playerEntity = null;
        List<Exception>? failures = null;
        // Attempt every cleanup boundary even when a consumer or native close callback fails.
        if (oldHost != null)
        {
            oldHost.BeforeRender = null;
            TryCleanup(() => oldHost.TryClose(), ref failures);
            TryCleanup(oldHost.Dispose, ref failures);
        }
        foreach (var layout in layouts.Values) TryCleanup(layout.Dispose, ref failures);
        layouts.Clear();
        TryCleanup(registry.EndSession, ref failures);
        if (failures != null) Report("session cleanup", new AggregateException(failures));
    }
    #endregion

    #region Prepared frame layout
    /// <summary>Tracks visibility and native anchors during drawing without gameplay sampling or preparation.</summary>
    private void BeforeRender()
    {
        if (disposed || anchors == null || Scheduler == null) return;
        bool visible = IsCurrentSession() && !api.HideGuis;
        anchors.BeginFrame(visible);
        Scheduler.ObserveVisibility(anchors, visible, enabled());
        SynchronizeLayouts();
    }

    /// <summary>Applies cached measurements and native layout, then opens/closes without taking focus.</summary>
    private void SynchronizeLayouts()
    {
        if (host == null || anchors == null || Scheduler == null) return;
        foreach (var group in registry.GetGroups())
        {
            if (!layouts.TryGetValue(group.Id, out var layout)) layouts[group.Id] = layout = new HudOverlayGroupLayout();
            layout.Apply(anchors, group, Scheduler.GetMembers(group.Id));
        }
        host.Synchronize(layouts);
        bool drawable = false;
        foreach (var layout in layouts.Values) drawable |= layout.Available && layout.Members.Count > 0;
        if (drawable) host.TryOpen(false);
        else host.TryClose();
    }

    /// <summary>Checks only global borrowed session identity; feature applicability remains on the heartbeat.</summary>
    private bool IsCurrentSession() => world != null && player != null && ReferenceEquals(api.World, world)
        && ReferenceEquals(api.World.Player, player) && ReferenceEquals(player.Entity, playerEntity);

    /// <summary>Includes the native reusable text font and contrast color in preparation invalidation.</summary>
    private static long FontRevision()
    {
        var hash = new HashCode();
        hash.Add(GuiStyle.StandardFontName);
        hash.Add(GuiStyle.SmallFontSize);
        foreach (double component in GuiStyle.DialogDefaultTextColor) hash.Add(component);
        return hash.ToHashCode();
    }
    #endregion

    #region Diagnostics and cleanup
    /// <summary>Bounds consumer diagnostics through the scheduler and keeps logger failures out of lifecycle work.</summary>
    private void Report(string id, Exception failure)
    {
        try { api.Logger.Warning("[VanillaExpanded] HUD overlay {0}: {1}", id, failure); } catch { }
    }

    /// <summary>Collects teardown failures while continuing all remaining owned cleanup.</summary>
    private static void TryCleanup(Action cleanup, ref List<Exception>? failures)
    {
        try { cleanup(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
    }
    #endregion
    #endregion
}
