using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>A count-bounded LRU with caller-defined working-set protection; synchronization belongs to its owner.</summary>
internal sealed class BoundedLru<TKey, TValue> where TKey : notnull where TValue : class
{
    private int capacity;
    private Func<TKey, bool>? protectedKey;
    private readonly Dictionary<TKey, (TValue Value, LinkedListNode<TKey> Node)> entries = new();
    private readonly LinkedList<TKey> order = new();
    internal int Count => entries.Count;
    internal TKey[] Keys => entries.Keys.ToArray();

    #region Public API
    /// <summary>Sets the caller-supplied maximum retained entry count; workload tuning is separate.</summary>
    internal BoundedLru(int capacity) { this.capacity = capacity; }

    /// <summary>Changes retention to cover the current visible working set, returning entries removed during shrink.</summary>
    internal TValue[] Resize(int capacity, Func<TKey, bool>? protectedKey = null)
    {
        if (capacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity; this.protectedKey = protectedKey;
        var removed = new List<TValue>();
        while (entries.Count > capacity) removed.Add(Evict()!);
        return removed.ToArray();
    }
    /// <summary>Promotes a retained entry so repeated visible requests reuse it.</summary>
    internal bool TryGet(TKey key, out TValue? value)
    {
        if (!entries.TryGetValue(key, out var entry)) { value = null; return false; }
        order.Remove(entry.Node); order.AddFirst(entry.Node);
        value = entry.Value; return true;
    }

    /// <summary>Adds an entry and returns any eviction so its validity can be revoked.</summary>
    internal TValue? Put(TKey key, TValue value)
    {
        Remove(key);
        entries[key] = (value, order.AddFirst(key));
        return entries.Count > capacity ? Evict() : null;
    }

    /// <summary>Removes a retained item without touching unrelated regional or tile inputs.</summary>
    internal TValue? Remove(TKey key)
    {
        if (!entries.Remove(key, out var entry)) return null;
        order.Remove(entry.Node); return entry.Value;
    }

    /// <summary>Returns all removed values for explicit invalidation on purge or disposal.</summary>
    internal TValue[] Clear()
    {
        var removed = entries.Values.Select(entry => entry.Value).ToArray();
        entries.Clear(); order.Clear(); return removed;
    }
    #endregion

    #region Private
    /// <summary>Chooses an off-view victim first, including a just-admitted obsolete entry when all older entries are visible.</summary>
    private TValue? Evict()
    {
        // Capacity covers all protected keys. Walking backward preserves their identities despite stale admission order.
        var victim = order.Last;
        while (victim != null && protectedKey?.Invoke(victim.Value) == true) victim = victim.Previous;
        if (victim == null) throw new InvalidOperationException("Protected working set exceeds cache capacity.");
        return Remove(victim.Value);
    }
    #endregion
}
