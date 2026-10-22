using Moq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Presentation;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises deferred upload fairness and lifecycle through real source preparation and presentation.</summary>
public sealed class BudgetedPresentationTests
{
    #region Public API
    /// <summary>Deferred current tiles receive admission before newly entering tiles, and resident overlaps never disappear.</summary>
    [Fact]
    public async Task DeferredTilesProgressWhileResidentsDrawAndNewTilesEnter()
    {
        using var fixture = new StorageFixture(); await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(4, 16, 16, 4));
        await Warm(preparation, 0, 1, 2, 9);
        var graphics = new BudgetGraphics(preparation);
        using var presentation = new HabitatMapPresentation(preparation, graphics, error => throw error, OneTile());
        Frame(presentation, graphics, 0, 1, 2); Frame(presentation, graphics, 0, 1, 2);
        Assert.Equal(new[] { 0 }, graphics.History.Select(key => key.X));
        Assert.Equal(new[] { 0 }, graphics.Draws.Select(receipt => receipt.Bundle.Key.X));
        Frame(presentation, graphics, 9, 0, 1, 2);
        Assert.Equal(new[] { 0, 1 }, graphics.History.Select(key => key.X));
        Assert.Contains(graphics.Draws, receipt => receipt.Bundle.Key.X == 0);
        Frame(presentation, graphics, 9, 0, 1, 2);
        Assert.Equal(new[] { 0, 1, 2 }, graphics.History.Select(key => key.X));
        Frame(presentation, graphics, 9, 0, 1, 2);
        Assert.Equal(new[] { 0, 1, 2, 9 }, graphics.History.Select(key => key.X));
        Assert.Equal(4, graphics.Draws.Count);
    }
    /// <summary>Departed backlog is not admitted, and close/reopen discards its old scheduling and resource ownership.</summary>
    [Fact]
    public async Task DepartedTilesAndClosedBacklogNeverUploadLater()
    {
        using var fixture = new StorageFixture(); await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(4, 16, 16, 4));
        await Warm(preparation, 0, 1, 2, 9);
        var graphics = new BudgetGraphics(preparation);
        using var presentation = new HabitatMapPresentation(preparation, graphics, error => throw error, OneTile());
        Frame(presentation, graphics, 0, 1, 2); Frame(presentation, graphics, 0, 1, 2);
        Frame(presentation, graphics, 0, 2);
        Assert.Equal(new[] { 0, 2 }, graphics.History.Select(key => key.X));
        Assert.Equal(2, graphics.Draws.Count);
        presentation.Suspend();
        await Warm(preparation, 9);
        Frame(presentation, graphics, 9); Frame(presentation, graphics, 9);
        Assert.Equal(new[] { 0, 2, 9 }, graphics.History.Select(key => key.X));
        Assert.Equal(9, Assert.Single(graphics.Draws).Bundle.Key.X);
        // Stopping presentation never disables durable acquisition/storage.
        Assert.True(await storage.WriteTileAsync(StorageFixture.Tile(new(9, 0)), storage.CurrentEpoch));
        Assert.NotNull((await storage.ReadTileAsync(new(9, 0))).Value);
    }
    /// <summary>Purge revokes resident and deferred receipts; later publication uses only new source/session provenance.</summary>
    [Fact]
    public async Task PurgeRevokesDeferredAndResidentInputs()
    {
        using var fixture = new StorageFixture(); await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(4, 16, 16, 4));
        await Warm(preparation, 0, 1, 2);
        var graphics = new BudgetGraphics(preparation);
        using var presentation = new HabitatMapPresentation(preparation, graphics, error => throw error, OneTile());
        Frame(presentation, graphics, 0, 1, 2); Frame(presentation, graphics, 0, 1, 2);
        var old = Assert.Single(graphics.Draws);
        await storage.PurgeAsync();
        Assert.False(preparation.IsCurrent(old));
        await Warm(preparation, 0, 1, 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        do
        {
            Frame(presentation, graphics, 0, 1, 2);
            Assert.DoesNotContain(old, graphics.Draws);
            Assert.All(graphics.Draws, receipt => Assert.True(preparation.IsCurrent(receipt)));
            if (graphics.Draws.Count == 3) break;
            await Task.Delay(1, timeout.Token);
        } while (true);
    }
    #endregion

    #region Private
    /// <summary>Uses a one-transfer test budget so multi-frame behavior is visible without real sleeps or GL execution.</summary>
    private static HabitatUploadLimits OneTile() => new(4, 1, TimeSpan.FromHours(1));
    /// <summary>Populates only test source bundles, leaving graphics uploads for the real presentation admission loop.</summary>
    private static async Task Warm(GpuHabitatPreparation preparation, params int[] x)
    {
        var keys = x.Select(value => new EcosystemTileKey(value, 0)).ToArray();
        preparation.SetView(keys);
        foreach (var key in keys)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<GpuHabitatReceipt?> task;
            while (!preparation.TryPrepare(key, out task)) await Task.Delay(1, timeout.Token);
            Assert.NotNull(await task);
        }
    }
    /// <summary>Submits a changed or stable view while retaining only the latest frame's draw evidence.</summary>
    private static void Frame(HabitatMapPresentation presentation, BudgetGraphics graphics, params int[] x)
    {
        graphics.Draws.Clear();
        var view = x.Select(value => new HabitatMapTile(new(value, 0), value, 0, 32, 32)).ToArray();
        var ranges = HabitatRanges.From(new HabitatRule("game:test", HabitatSpawnPath.Runtime, -20, 30, 0, 1, 0, 1, 0, 1, 0, 0, 2));
        presentation.Render(view, ranges, new float[16]);
    }

    /// <summary>Records budgeted resource availability without replacing real preparation, view gates or presentation scheduling.</summary>
    private sealed class BudgetGraphics : IHabitatMapGraphics
    {
        private readonly GpuHabitatPreparation preparation;
        private readonly Dictionary<EcosystemTileKey, GpuHabitatReceipt> resident = new();
        internal List<EcosystemTileKey> History { get; } = new();
        internal List<GpuHabitatReceipt> Draws { get; } = new();
        #region Public API
        /// <summary>Retains the actual source validator for every publication.</summary>
        internal BudgetGraphics(GpuHabitatPreparation preparation) { this.preparation = preparation; }
        /// <summary>Accepts shader reload without changing input ownership.</summary>
        public bool Reload() => true;
        /// <summary>Leaves eviction to the production-owner tests; all retained fixture resources stay small and explicit.</summary>
        public void SetView(IReadOnlyCollection<EcosystemTileKey> visible, EcosystemStorageIdentity world, GpuHabitatLimits limits) { }
        /// <summary>Provides the ordinary upload scope boundary without GL calls.</summary>
        public IDisposable BeginUploads() => Mock.Of<IDisposable>();
        /// <summary>Charges one synthetic transfer only for a changed source lease and publishes it after successful admission.</summary>
        public bool TryPrepare(GpuHabitatReceipt receipt, HabitatUploadBudget budget)
        {
            if (resident.TryGetValue(receipt.Bundle.Key, out var old)
                && ReferenceEquals(old.Bundle.Region, receipt.Bundle.Region) && ReferenceEquals(old.Bundle.Elevation, receipt.Bundle.Elevation))
                return budget.TryAdmit(default);
            if (!budget.TryAdmit(new(4, 0, 1, 0))) return false;
            Assert.True(preparation.IsCurrent(receipt));
            resident[receipt.Bundle.Key] = receipt; History.Add(receipt.Bundle.Key); return true;
        }
        /// <summary>Opens a draw boundary only after this frame's upload admission has finished.</summary>
        public IDisposable Begin(GpuHabitatReceipt first, HabitatRanges ranges, float[] projection) => Mock.Of<IDisposable>();
        /// <summary>Rejects stale publication and records valid resident draw receipts.</summary>
        public void Draw(GpuHabitatReceipt receipt, HabitatMapTile tile)
        { Assert.True(preparation.IsCurrent(receipt)); Assert.Same(resident[tile.Key], receipt); Draws.Add(receipt); }
        /// <summary>Revokes all fixture resource ownership on close or purge.</summary>
        public void Clear() { resident.Clear(); Draws.Clear(); }
        /// <summary>Ends the fixture graphics lifetime without touching independent durable storage.</summary>
        public void Dispose() { Clear(); }
        #endregion
    }
    #endregion
}
