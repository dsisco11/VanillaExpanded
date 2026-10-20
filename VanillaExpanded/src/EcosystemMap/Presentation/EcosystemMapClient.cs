using System;
using System.Linq;
using System.Threading;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Catalogue;
using Vintagestory.API.Client;
using FrameProfilerUtil = Vintagestory.API.Common.FrameProfilerUtil;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Composes client map controls, current explored coverage and graphics lifetime for one world.</summary>
internal sealed class EcosystemMapClient : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly EcosystemClientSession session;
    private readonly Func<EcosystemSpecies, HabitatRule> resolve;
    private readonly EcosystemMapSelection selection = new();
    private readonly EcosystemMapControls controls;
    private HabitatMapPresentation? presentation;
    private bool catalogueLoaded, suspended = true, failed, disposed;
    private int purged;

    #region Public API
    /// <summary>Attaches shader/purge events without allocating graphics resources until the full map is active.</summary>
    internal EcosystemMapClient(ICoreClientAPI api, EcosystemClientSession session, string group,
        Func<EcosystemSpecies, HabitatRule> resolve)
    {
        this.api = api; this.session = session; this.resolve = resolve;
        controls = new(api, selection, group);
        session.Purged += OnPurge; api.Event.ReloadShader += Reload;
    }
    /// <summary>Loads the local catalogue once and replaces only the feature's extra composer.</summary>
    internal void Compose(GuiDialogWorldMap dialog, GuiComposer composer)
    {
        if (disposed) return;
        Close();
        if (!catalogueLoaded)
        {
            selection.SetSpecies(EcosystemSpeciesCatalogue.Load(api)); catalogueLoaded = true;
        }
        controls.Compose(dialog, composer);
    }
    /// <summary>Draws only the exact full-map element inside vanilla's existing scissor, without environmental world queries.</summary>
    internal void Render(GuiElementMap map, bool active)
    {
        if (disposed || failed) return;
        if (!controls.Refresh(active)) { Close(); return; }
        if (!controls.IsSurface(map)) return;
        FrameProfilerUtil? profiler = null;
        try
        {
            if (Interlocked.Exchange(ref purged, 0) != 0) Close();
            // Join the native opt-in frame profile; these marks do not enable logging or read GPU results.
            profiler = api.World.FrameProfiler;
            profiler?.Enter("ecosystem-map");
            var ranges = selection.ActiveRanges(resolve);
            if (ranges == null) return;
            presentation ??= new(session.Preparation, new HabitatMapGraphics(api), Report);
            suspended = false;
            profiler?.Mark("selection-and-startup");
            var coverage = ExploredMapCoverage.Capture(map.mapLayers, map.CurrentBlockViewBounds);
            profiler?.Mark("explored-coverage");
            // Convert current explored chunk placement only; habitat values remain original GPU inputs.
            var tiles = coverage.OrderBy(key => key.Z).ThenBy(key => key.X).Select(key =>
            {
                var point = new Vec2f();
                map.TranslateWorldPosToViewPos(new Vec3d((double)key.X * 32, 0, (double)key.Z * 32), ref point);
                return new HabitatMapTile(key, (float)map.Bounds.renderX + point.X,
                    (float)map.Bounds.renderY + point.Y, 32 * map.ZoomLevel, 32 * map.ZoomLevel);
            }).ToArray();
            profiler?.Mark("tile-placement");
            presentation.Render(tiles, ranges, api.Render.CurrentProjectionMatrix, profiler);
        }
        catch (Exception error) { failed = true; Close(); Report(error); }
        finally { profiler?.Leave(); }
    }
    /// <summary>Closes disposable presentation without stopping acquisition or dropping pending persistent writes.</summary>
    internal void Close()
    {
        controls.Refresh(false);
        if (suspended) return;
        presentation?.Suspend(); suspended = true;
    }
    /// <summary>Detaches event ownership before releasing the graphics and composer resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; session.Purged -= OnPurge; api.Event.ReloadShader -= Reload;
        try { presentation?.Dispose(); }
        finally { controls.Dispose(); }
    }
    #endregion

    #region Private
    /// <summary>Defers graphics invalidation until the render thread observes a successful purge.</summary>
    private void OnPurge() => Interlocked.Exchange(ref purged, 1);
    /// <summary>Reloads an existing shader owner on the normal shader event; absent graphics need no reload.</summary>
    private bool Reload()
    {
        try
        {
            bool success = presentation?.Reload() ?? true;
            if (success) failed = false;
            return success;
        }
        catch (Exception error) { Report(error); return false; }
    }
    /// <summary>Reports renderer or asynchronous input faults at the feature boundary.</summary>
    private void Report(Exception error) => api.Logger.Error("Ecosystem map presentation failed: {0}", error);
    #endregion
}
