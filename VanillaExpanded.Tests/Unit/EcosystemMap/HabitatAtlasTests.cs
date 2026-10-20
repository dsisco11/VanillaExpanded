using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks native row allocation, immutable pages, partial transfers and recycled-address rejection.</summary>
public sealed class HabitatAtlasTests
{
    #region Public API
    /// <summary>Native logical rows cover exact nonoverlapping pixels, including rectangular pages and hardware spill.</summary>
    [Theory]
    [InlineData(33, 86, 4096)]
    [InlineData(32, 7511, 4096)]
    [InlineData(32, 9, 64)]
    [InlineData(1, 1, 16)]
    public void NativeRowsPreserveExactRectangles(int size, int demand, int maximum)
    {
        var layout = new NativeAtlasLayout(size, demand, maximum);
        var addresses = new List<AtlasAddress>();
        while (layout.TryAllocate(out var address)) addresses.Add(address);
        Assert.Equal(layout.Capacity, addresses.Count);
        Assert.Equal(addresses.Count, addresses.Select(a => (a.X, a.Y)).Distinct().Count());
        Assert.InRange(layout.Width, size, maximum);
        Assert.InRange(layout.Height, size, maximum);
        Assert.All(addresses, a =>
        {
            Assert.InRange(a.X, 0, layout.Width - size); Assert.InRange(a.Y, 0, layout.Height - size);
            Assert.Equal(0, a.X % size); Assert.Equal(0, a.Y % size);
            Assert.True(layout.IsCurrent(a));
        });
        var old = addresses[addresses.Count / 2];
        layout.Release(old);
        Assert.False(layout.IsCurrent(old));
        Assert.True(layout.TryAllocate(out var replacement));
        Assert.Equal((old.X, old.Y), (replacement.X, replacement.Y));
        Assert.NotEqual(old.Generation, replacement.Generation);
        layout.Release(old);
        Assert.True(layout.IsCurrent(replacement));
        Assert.Equal(layout.Capacity, layout.Used);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NativeAtlasLayout(maximum + 1, 1, maximum));
    }
    /// <summary>Capacity expansion adds pages without uploads or relocation of retained cells; shrink releases empty pages.</summary>
    [Fact]
    public void GrowthAndEvictionPreserveRetainedSlots()
    {
        var pages = new List<AtlasTextureFixture>();
        using var atlas = new HabitatAtlas(false, 64, 4, (w, h, n) => { var page = new AtlasTextureFixture(); pages.Add(page); return page; });
        ushort[] data = new ushort[32 * 32 * 2];
        var first = atlas.Upload(data, 32); var second = atlas.Upload(data, 32);
        var third = atlas.Upload(data, 32); var fourth = atlas.Upload(data, 32);
        var original = first.Address; int texture = first.TextureId;
        atlas.SetCapacity(5);
        var entering = atlas.Upload(data, 32);
        Assert.Equal(2, atlas.PageCount);
        Assert.Equal(original, first.Address); Assert.Equal(texture, first.TextureId);
        Assert.Equal(4, pages[0].Uploads.Count); Assert.Single(pages[1].Uploads);
        Assert.All(pages[0].Uploads, upload => Assert.Same(data, upload.Values));
        second.Dispose();
        var changed = atlas.Upload(data, 32);
        Assert.Equal(texture, changed.TextureId);
        Assert.False(second.IsCurrent);
        Assert.Equal(2, atlas.PageCount);
        third.Dispose(); fourth.Dispose(); changed.Dispose();
        atlas.SetCapacity(2);
        Assert.True(first.IsCurrent); Assert.True(entering.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => atlas.Upload(data, 32));
        entering.Dispose(); atlas.SetCapacity(2);
        Assert.Single(pages, page => page.Disposed);
        Assert.Equal(1, atlas.PageCount);
        atlas.Clear();
        Assert.False(first.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => _ = first.TextureId);
        Assert.All(pages, page => Assert.True(page.Disposed));
        first.Dispose();
    }
    /// <summary>Invalid data and failed transfers never consume a slot or leave an empty graphics page alive.</summary>
    [Fact]
    public void InvalidUploadsAndFailedTransfersDoNotLeak()
    {
        using var atlas = new HabitatAtlas(true, 64, 2, (w, h, n) => new AtlasTextureFixture());
        Assert.Throws<ArgumentException>(() => atlas.Upload(new ushort[8], 2));
        Assert.Throws<ArgumentException>(() => atlas.Upload(new byte[3], 1));
        Assert.Equal(0, atlas.PageCount);
        var failing = new MockTexture();
        using var failure = new HabitatAtlas(true, 64, 2, (w, h, n) => failing);
        Assert.Throws<InvalidOperationException>(() => failure.Upload(new byte[16], 2));
        Assert.Equal(0, failure.Used); Assert.Equal(0, failure.PageCount); Assert.True(failing.Disposed);
    }
    #endregion

    #region Private
    /// <summary>Exercises the production rollback path for a failed partial GPU transfer.</summary>
    private sealed class MockTexture : IHabitatAtlasTexture
    {
        public int Id => 1;
        internal bool Disposed;
        /// <summary>Simulates an upload failure after page allocation.</summary>
        public void Upload(Array values, int size, int x, int y) { throw new InvalidOperationException("upload failure"); }
        /// <summary>Records disposal of a page whose only transfer failed.</summary>
        public void Dispose() { Disposed = true; }
    }
    #endregion
}
