using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Moq;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises startup and normal packet/event acquisition through the installed receipt adapter with no map window.</summary>
[Collection("Ecosystem receipt adapters")]
public sealed class OceanCaptureSourceTests
{
    #region Public API
    /// <summary>Already-loaded tiles and ordinary late arrivals acquire on a worker, persist on shutdown and reject old purge work.</summary>
    [Fact]
    public async Task CapturesWhileMapClosedAndJoinsBeforeStorage()
    {
        using var files = new StorageFixture();
        var storage = files.Open();
        var errors = new ConcurrentQueue<Exception>();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        var native = new ClientReceiptFixture();
        using var receipt = new ClientReceiptAdapter(native.Game, errors.Enqueue);
        native.Receive(0, 0, 5);
        receipt.Start(acquisition, 32);
        var surfaces = new OceanClassificationFixture();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using (var source = new OceanCaptureSource(api.Object, receipt, acquisition, surfaces.Classifier, errors.Enqueue))
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await WaitForOcean(storage, new(0, 0), timeout.Token);
            native.Receive(1, 0, 5);
            await WaitForOcean(storage, new(1, 0), timeout.Token);
            await storage.PurgeAsync();
            source.RefreshLoaded();
            await WaitForOcean(storage, new(0, 0), timeout.Token);
            // A normal dirty event also works with the tab/map absent, and never performs voxel reads on the notifying thread.
            int notifyingThread = Environment.CurrentManagedThreadId;
            int notifying = 1;
            var reads = new ConcurrentQueue<bool>();
            surfaces.Surface = (x, y, z) => { reads.Enqueue(Environment.CurrentManagedThreadId == notifyingThread && Volatile.Read(ref notifying) == 1); return 6; };
            Mock.Get(api.Object.Event).Raise(value => value.ChunkDirty += null, new Vec3i(2, 0, 0), surfaces.Chunks[0].Object, EnumChunkDirtyReason.NewlyLoaded);
            Volatile.Write(ref notifying, 0);
            native.Receive(2, 0, 5);
            await WaitForOcean(storage, new(2, 0), timeout.Token);
            Assert.NotEmpty(reads);
            Assert.DoesNotContain(true, reads);
            surfaces.Surface = (x, y, z) => 1;
            Mock.Get(api.Object.Event).Raise(value => value.ChunkDirty += null, new Vec3i(2, 0, 0), surfaces.Chunks[0].Object, EnumChunkDirtyReason.MarkedDirty);
            while ((await storage.ReadTileAsync(new(2, 0))).Value?.Ocean?.Columns[0] != 1) await Task.Delay(1, timeout.Token);
        }
        receipt.Stop();
        await acquisition.ShutdownAsync();
        Assert.Empty(errors);
        await using var reopened = files.Open();
        Assert.NotNull((await reopened.ReadTileAsync(new(2, 0))).Value!.Ocean);
    }
    /// <summary>Delayed callbacks cannot replace newer queued work or revive an old epoch after purge, including overflow scans.</summary>
    [Fact]
    public async Task CoalescingPreservesNewestEpochAndOrdinal()
    {
        using var files = new StorageFixture();
        var storage = files.Open();
        var errors = new ConcurrentQueue<Exception>();
        var acquisition = new EnvironmentalAcquisition(storage, 1, errors.Enqueue);
        var native = new ClientReceiptFixture();
        using var receipt = new ClientReceiptAdapter(native.Game, errors.Enqueue);
        receipt.Start(acquisition, 32);
        var surfaces = new OceanClassificationFixture();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using (var source = new OceanCaptureSource(api.Object, receipt, acquisition, surfaces.Classifier, errors.Enqueue))
        {
            var type = typeof(OceanCaptureSource);
            var gate = type.GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            var pending = (IDictionary)type.GetField("pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            var rescan = type.GetField("rescan", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var callback = type.GetMethod("OnTile", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var epoch = acquisition.CurrentEpoch;
            var key = new EcosystemTileKey(0, 0);
            // Holding the real queue gate reproduces delayed/reordered callbacks without timing sleeps or native reads.
            lock (gate)
            {
                callback.Invoke(source, new object[] { key, epoch, 20L });
                callback.Invoke(source, new object[] { key, epoch, 10L });
                Assert.Equal(20L, pending[key]!.GetType().GetProperty("Sequence")!.GetValue(pending[key]));
                for (int i = 1; i < 128; i++) callback.Invoke(source, new object[] { new EcosystemTileKey(i, 0), epoch, 20L });
                callback.Invoke(source, new object[] { new EcosystemTileKey(999, 0), epoch, 40L });
                callback.Invoke(source, new object[] { new EcosystemTileKey(998, 0), epoch, 30L });
                var scan = rescan.GetValue(source)!;
                Assert.Equal(40L, scan.GetType().GetProperty("Sequence")!.GetValue(scan));
                Assert.Equal(128, pending.Count);
            }
            await storage.PurgeAsync();
            var current = acquisition.CurrentEpoch;
            lock (gate)
            {
                callback.Invoke(source, new object[] { key, current, 100L });
                callback.Invoke(source, new object[] { key, epoch, 10000L });
                var request = pending[key]!;
                Assert.Equal(100L, request.GetType().GetProperty("Sequence")!.GetValue(request));
                Assert.Same(current, request.GetType().GetProperty("Epoch")!.GetValue(request));
            }
        }
        receipt.Stop();
        await acquisition.ShutdownAsync();
        Assert.Empty(errors);
    }
    #endregion

    #region Private
    /// <summary>Awaits only feature-owned local storage; no world loading or render loop is involved.</summary>
    private static async Task WaitForOcean(EnvironmentalStorage storage, EcosystemTileKey key, CancellationToken cancellation)
    {
        while ((await storage.ReadTileAsync(key)).Value?.Ocean == null) await Task.Delay(1, cancellation);
    }
    #endregion
}
