using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Storage;
using FrameProfilerUtil = Vintagestory.API.Common.FrameProfilerUtil;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Coordinates bounded local reads and graphics-thread publication using current explored coverage.</summary>
internal sealed class HabitatMapPresentation : IDisposable
{
    private readonly GpuHabitatPreparation preparation;
    private readonly IHabitatMapGraphics graphics;
    private readonly List<(GpuHabitatReceipt Receipt, HabitatMapTile Tile, bool Drawable)> ready = new();
    private readonly float[] projectionSnapshot = new float[16];
    private readonly Action<Exception> report;
    private readonly Dictionary<EcosystemTileKey, Task<GpuHabitatReceipt?>> pending = new();
    private readonly BoundedLru<EcosystemTileKey, GpuHabitatReceipt> receipts = new(128);
    private HashSet<EcosystemTileKey> visible = new();
    private bool suspended = true, disposed;
    private readonly HabitatUploadLimits uploadLimits;
    private readonly TimeProvider time;
    private EcosystemTileKey? deferredUpload;

    #region Public API
    /// <summary>Injects input and graphics ownership; persistence stays with the client session.</summary>
    internal HabitatMapPresentation(GpuHabitatPreparation preparation, IHabitatMapGraphics graphics, Action<Exception> report,
        HabitatUploadLimits? uploadLimits = null, TimeProvider? time = null)
    {
        this.preparation = preparation; this.graphics = graphics; this.report = report;
        this.uploadLimits = uploadLimits ?? HabitatUploadLimits.Client; this.time = time ?? TimeProvider.System;
    }

    /// <summary>Polls only completed reads and draws current explored tiles; it never blocks the render thread on storage.</summary>
    internal void Render(IReadOnlyList<HabitatMapTile> tiles, HabitatRanges ranges, float[] projection, FrameProfilerUtil? profiler = null)
    {
        if (disposed) return;
        // Native projection/model-view getters share scratch storage; retain an owned projection before any tile draw.
        Array.Copy(projection, projectionSnapshot, projectionSnapshot.Length);
        var current = tiles.Select(tile => tile.Key).ToHashSet();
        if (suspended || !visible.SetEquals(current))
        {
            // Membership changes do not revoke source provenance or useful work for overlapping tiles.
            var retained = preparation.SetView(current);
            receipts.Resize(retained.Bundles, current.Contains);
            graphics.SetView(current, preparation.World, retained);
            visible = current; suspended = false;
        }
        profiler?.Mark("view-membership");
        // Keep incomplete jobs bounded by preparation admission; observe and forget completed off-view results.
        foreach (var key in pending.Keys.Where(key => !current.Contains(key) && pending[key].IsCompleted).ToArray())
        {
            try { pending[key].GetAwaiter().GetResult(); }
            catch (Exception error) { report(error); }
            pending.Remove(key);
        }
        ready.Clear();
        foreach (var tile in tiles)
        {
            if (!receipts.TryGet(tile.Key, out var receipt) || !preparation.IsCurrent(receipt!))
            {
                receipts.Remove(tile.Key); receipt = null;
                if (pending.TryGetValue(tile.Key, out var job) && job.IsCompleted)
                {
                    pending.Remove(tile.Key);
                    try { receipt = job.GetAwaiter().GetResult(); }
                    catch (Exception error) { report(error); }
                    if (receipt != null && preparation.IsCurrent(receipt)) receipts.Put(tile.Key, receipt);
                    else receipt = null;
                }
                if (receipt == null && !pending.ContainsKey(tile.Key) && preparation.TryPrepare(tile.Key, out var next))
                    pending[tile.Key] = next;
            }
            if (receipt != null && preparation.IsCurrent(receipt)) ready.Add((receipt, tile, false));
        }
        profiler?.Mark("input-preparation");
        // Keep receipt polling separate from texture acquisition so the native profiler can distinguish CPU stages.
        var budget = new HabitatUploadBudget(uploadLimits, time);
        int start = deferredUpload is { } deferredKey ? ready.FindIndex(item => item.Receipt.Bundle.Key == deferredKey) : 0;
        if (start < 0) start = 0;
        deferredUpload = null;
        using (graphics.BeginUploads())
        {
            // The first deferred current tile gets the next frame's first admission, including oversized inputs.
            for (int i = 0; i < ready.Count; i++)
            {
                int index = (start + i) % ready.Count;
                var item = ready[index];
                if (!preparation.IsCurrent(item.Receipt)) continue;
                bool drawable = graphics.TryPrepare(item.Receipt, budget);
                ready[index] = (item.Receipt, item.Tile, drawable);
                if (!drawable && deferredUpload == null) deferredUpload = item.Receipt.Bundle.Key;
            }
        }
        profiler?.Mark("texture-acquisition");
        // Source acquisition finishes before any draw binding is tracked; late invalidation cannot publish stale inputs.
        ready.RemoveAll(item => !item.Drawable || !preparation.IsCurrent(item.Receipt));
        if (ready.Count == 0) return;
        try
        {
            using var pass = graphics.Begin(ready[0].Receipt, ranges, projectionSnapshot);
            foreach (var item in ready)
                if (preparation.IsCurrent(item.Receipt)) graphics.Draw(item.Receipt, item.Tile);
        }
        finally { ready.Clear(); profiler?.Mark("draw-and-restore"); }
    }
    /// <summary>Cancels disposable reads and releases textures while leaving durable acquisition/storage running.</summary>
    internal void Suspend()
    {
        if (disposed) return;
        preparation.SetView(Array.Empty<EcosystemTileKey>()); preparation.CancelPresentation(true); DropPending(); receipts.Clear(); ready.Clear(); visible.Clear(); suspended = true;
        graphics.Clear(); deferredUpload = null;
    }
    /// <summary>Reloads through the game's normal shader event on the graphics thread.</summary>
    internal bool Reload() => !disposed && graphics.Reload();
    /// <summary>Ends presentation ownership without disposing the independently session-owned storage/preparer.</summary>
    public void Dispose()
    {
        if (disposed) return;
        Suspend(); disposed = true; graphics.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Observes late faults without publishing results or touching graphics from a worker continuation.</summary>
    private void DropPending()
    {
        foreach (var task in pending.Values)
            _ = task.ContinueWith(failed => report(failed.Exception!), default,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        pending.Clear();
    }
    #endregion
}
