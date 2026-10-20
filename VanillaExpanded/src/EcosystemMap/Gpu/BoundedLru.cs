using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>A count-bounded LRU with indexed off-view eviction; synchronization belongs to its owner.</summary>
internal sealed class BoundedLru<TKey, TValue> where TKey : notnull where TValue : class
{
    private int capacity;
    private Func<TKey, bool>? protectedKey;
    private readonly Dictionary<TKey, (TValue Value, LinkedListNode<TKey> Node, LinkedListNode<TKey>? Candidate)> entries = new();
    private readonly LinkedList<TKey> order = new();
    private readonly LinkedList<TKey> candidates = new();
    internal int Count => entries.Count;
    internal TKey[] Keys => entries.Keys.ToArray();

    #region Public API
    /// <summary>Sets the caller-supplied maximum retained entry count; workload tuning is separate.</summary>
    internal BoundedLru(int capacity) { this.capacity = capacity; }

    /// <summary>Indexes the new working set once, then shrinks without rescanning protected entries for every victim.</summary>
    internal TValue[] Resize(int capacity, Func<TKey, bool>? protectedKey = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity; this.protectedKey = protectedKey;
        // Keep candidate order identical to global LRU order, so protection changes preserve off-view recency.
        candidates.Clear();
        foreach (var key in order)
        {
            var entry = entries[key];
            entry.Candidate = protectedKey?.Invoke(key) == true ? null : candidates.AddLast(key);
            entries[key] = entry;
        }
        var removed = new List<TValue>();
        while (entries.Count > capacity) removed.Add(Evict()!);
        return removed.ToArray();
    }

    /// <summary>Promotes retained data and its eviction candidate without walking the protected working set.</summary>
    internal bool TryGet(TKey key, out TValue? value)
    {
        if (!entries.TryGetValue(key, out var entry)) { value = null; return false; }
        order.Remove(entry.Node); order.AddFirst(entry.Node);
        // Hot cache hits only update recency; mutable protection is rechecked when an eviction is actually needed.
        if (entry.Candidate != null) { candidates.Remove(entry.Candidate); candidates.AddFirst(entry.Candidate); }
        value = entry.Value; return true;
    }

    /// <summary>Adds an entry and returns an off-view eviction, including the incoming entry if every older entry is protected.</summary>
    internal TValue? Put(TKey key, TValue value)
    {
        Remove(key);
        var node = order.AddFirst(key);
        var candidate = protectedKey?.Invoke(key) == true ? null : candidates.AddFirst(key);
        entries[key] = (value, node, candidate);
        return entries.Count > capacity ? Evict() : null;
    }

    /// <summary>Removes a retained item and both recency links without touching unrelated input ownership.</summary>
    internal TValue? Remove(TKey key)
    {
        if (!entries.Remove(key, out var entry)) return null;
        order.Remove(entry.Node);
        if (entry.Candidate != null) candidates.Remove(entry.Candidate);
        return entry.Value;
    }

    /// <summary>Returns all removed values for explicit invalidation on purge or disposal.</summary>
    internal TValue[] Clear()
    {
        var removed = entries.Values.Select(entry => entry.Value).ToArray();
        entries.Clear(); order.Clear(); candidates.Clear(); return removed;
    }
    #endregion

    #region Private
    /// <summary>Consumes the indexed oldest off-view entry, respecting independently changed source validity.</summary>
    private TValue? Evict()
    {
        while (candidates.Last is { } candidate)
        {
            if (protectedKey?.Invoke(candidate.Value) != true) return Remove(candidate.Value);
            // A mutable predicate may newly protect an indexed entry; never evict it using stale classification.
            var entry = entries[candidate.Value];
            candidates.Remove(candidate); entry.Candidate = null; entries[candidate.Value] = entry;
        }
        // Source invalidation can make a previously protected entry evictable without a view change.
        // This exceptional fallback searches once for that changed source; ordinary admission uses the index.
        for (var victim = order.Last; victim != null; victim = victim.Previous)
            if (protectedKey?.Invoke(victim.Value) != true) return Remove(victim.Value);
        throw new InvalidOperationException("Protected working set exceeds cache capacity.");
    }
    #endregion
}
