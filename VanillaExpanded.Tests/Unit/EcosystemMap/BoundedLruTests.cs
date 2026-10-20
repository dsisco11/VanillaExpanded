using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks indexed protected eviction, including recency, dynamic source validity and wide-view shrink work.</summary>
public sealed class BoundedLruTests
{
    #region Public API
    /// <summary>Many cold protected entries are inspected once during shrink rather than again for every departing entry.</summary>
    [Fact]
    public void ShrinkIndexesProtectedTailOnce()
    {
        const int count = 10000;
        var cache = new BoundedLru<int, object>(count);
        var values = Enumerable.Range(0, count).Select(_ => new object()).ToArray();
        for (int i = 0; i < count; i++) cache.Put(i, values[i]);
        int inspections = 0;
        var removed = cache.Resize(count / 2, key => { inspections++; return key < count / 2; });
        Assert.Equal(count / 2, removed.Length);
        Assert.Equal(count / 2, cache.Count);
        Assert.InRange(inspections, count, count * 2);
        for (int i = 0; i < count / 2; i++) { Assert.True(cache.TryGet(i, out var value)); Assert.Same(values[i], value); }
    }

    /// <summary>Entering protected entries evict indexed off-view entries even when all protected entries are colder.</summary>
    [Fact]
    public void AdmissionUsesOffViewRecencyAndCanEvictItself()
    {
        var cache = new BoundedLru<int, object>(4);
        var values = Enumerable.Range(0, 7).Select(_ => new object()).ToArray();
        for (int i = 0; i < 4; i++) cache.Put(i, values[i]);
        var visible = new HashSet<int> { 0, 1, 4, 5 };
        cache.Resize(4, visible.Contains);
        Assert.True(cache.TryGet(2, out _)); // Make 3 the oldest off-view candidate.
        Assert.Same(values[3], cache.Put(4, values[4]));
        Assert.Same(values[2], cache.Put(5, values[5]));
        Assert.Same(values[6], cache.Put(6, values[6]));
        foreach (var key in visible) Assert.True(cache.TryGet(key, out _));
        Assert.Equal(4, cache.Count);
    }

    /// <summary>Independent lease invalidation or new protection cannot make an outdated candidate evict current data.</summary>
    [Fact]
    public void MutableProtectionIsRecheckedBeforeEviction()
    {
        var cache = new BoundedLru<int, object>(2);
        var first = new object(); var second = new object(); var third = new object();
        cache.Put(0, first); cache.Put(1, second);
        var visible = new HashSet<int> { 0, 1 };
        cache.Resize(2, visible.Contains);
        visible.Remove(0); visible.Add(2);
        Assert.Same(first, cache.Put(2, third)); // Invalidation needs the exceptional search, not a stale pinned classification.
        cache.Clear();
        visible.Clear(); cache.Resize(2, visible.Contains);
        cache.Put(0, first); cache.Put(1, second);
        visible.Add(0); visible.Add(2);
        Assert.Same(second, cache.Put(2, third)); // Skip the older indexed candidate that is now protected.
        Assert.True(cache.TryGet(0, out var retained)); Assert.Same(first, retained);
        Assert.Equal(2, cache.Count);
    }
    #endregion
}
