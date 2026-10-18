using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises accepted-write durability, generation barriers and explicit cache clearing without game-region loading.</summary>
public sealed class EnvironmentalStorageLifecycleTests
{
    #region Public API
    /// <summary>Normal shutdown drains all operations admitted before it closes, while cancelling disposable work and rejecting late producers.</summary>
    [Fact]
    public async Task ShutdownFlushesAcceptedWritesAndCancelsOnlyDisposableWork()
    {
        using var fixture = new StorageFixture();
        var store = fixture.Open(capacity:1);
        await store.WaitUntilReadyAsync();
        StorageEpoch ticket = store.CurrentEpoch;
        var writes = Enumerable.Range(0,32).Select(x =>
            store.WriteRegionAsync(new(new(x,0), Climate:StorageFixture.Map(x)), ticket)).ToArray();
        // Accepted requests must be durable; submissions overtaken by shutdown may be explicitly rejected.
        Task shutdown = store.ShutdownAsync();
        bool[] accepted = await Task.WhenAll(writes);
        await shutdown;
        Assert.Contains(true,accepted);
        Assert.True(ticket.DisposableWork.IsCancellationRequested);
        Assert.False(store.IsCurrent(ticket));
        Assert.False(await store.WriteRegionAsync(new(new(100,0),Climate:StorageFixture.Map()),ticket));
        await store.ShutdownAsync();
        await using var reopened = fixture.Open();
        for (int x=0;x<accepted.Length;x++)
        {
            var region=(await reopened.ReadRegionAsync(new(x,0))).Value;
            Assert.Equal(accepted[x],region != null);
            if(region != null) Assert.Equal(int.MinValue+x,region.Climate!.Values[0]);
        }
    }

    /// <summary>Purge invalidates pending/late producers and completed reads, then permits only new-generation data to persist.</summary>
    [Fact]
    public async Task PurgeRejectsStaleWorkAndCannotResurrectClearedRecords()
    {
        using var fixture = new StorageFixture();
        var store = fixture.Open(capacity:1);
        await store.WaitUntilReadyAsync();
        var original=store.CurrentEpoch;
        var key=new EcosystemRegionKey(9,9);
        await store.WriteRegionAsync(new(key,Climate:StorageFixture.Map()),original);
        var previousRead=await store.ReadRegionAsync(key);
        var pending=Enumerable.Range(0,64).Select(x=>store.WriteRegionAsync(new(new(x,0),Forest:StorageFixture.Map()),original)).ToArray();
        await store.PurgeAsync();
        await Task.WhenAll(pending);
        Assert.True(original.DisposableWork.IsCancellationRequested);
        Assert.False(store.IsCurrent(previousRead.Epoch));
        Assert.False(await store.WriteRegionAsync(new(key,Shrubs:StorageFixture.Map()),original));
        Assert.Null((await store.ReadRegionAsync(key)).Value);
        for(int x=0;x<64;x++) Assert.Null((await store.ReadRegionAsync(new(x,0))).Value);
        await store.WriteRegionAsync(new(key,Shrubs:StorageFixture.Map()),store.CurrentEpoch);
        await store.ShutdownAsync();
        await using var reopened=fixture.Open();
        var current=(await reopened.ReadRegionAsync(key)).Value!;
        Assert.Null(current.Climate);
        Assert.Null(current.Forest);
        Assert.NotNull(current.Shrubs);
    }

    /// <summary>Cancellation observers may reenter storage without deadlocking its admission gate or restoring an old generation.</summary>
    [Fact]
    public async Task DisposableCancellationCallbacksRunOutsideStorageLocks()
    {
        using var fixture=new StorageFixture();
        await using var store=fixture.Open(capacity:1);
        await store.WaitUntilReadyAsync();
        var previous=store.CurrentEpoch;
        bool rejected=false;
        using var registration=previous.DisposableWork.Register(() =>
            rejected=!store.WriteRegionAsync(new(new(1,1),Climate:StorageFixture.Map()),previous).GetAwaiter().GetResult());
        await store.PurgeAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(rejected);
    }

    /// <summary>Shutdown is published before observers request the same shutdown receipt during cancellation.</summary>
    [Fact]
    public async Task ShutdownObserversReceiveThePublishedTask()
    {
        using var fixture = new StorageFixture();
        var store = fixture.Open();
        await store.WaitUntilReadyAsync();
        Task? observed = null;
        using var registration = store.CurrentEpoch.DisposableWork.Register(() => observed = store.ShutdownAsync());
        Task shutdown = store.ShutdownAsync();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Same(shutdown, observed);
    }
    /// <summary>Opening, updating, purging and closing the ecosystem cache do not access a sentinel in vanilla's map directory.</summary>
    [Fact]
    public async Task OwnStorageNeverTouchesVanillaMapFiles()
    {
        using var fixture=new StorageFixture();
        string vanilla=Path.Combine(fixture.Root,"Maps","test-world.db");
        Directory.CreateDirectory(Path.GetDirectoryName(vanilla)!);
        byte[] sentinel=Enumerable.Range(0,57).Select(x=>(byte)x).ToArray();
        File.WriteAllBytes(vanilla,sentinel);
        await using(var store=fixture.Open())
        {
            await store.WriteRegionAsync(new(new(0,0),Climate:StorageFixture.Map()),store.CurrentEpoch);
            await store.PurgeAsync();
            Assert.NotEqual(vanilla,store.FilePath);
        }
        Assert.Equal(sentinel,File.ReadAllBytes(vanilla));
    }
    #endregion
}
