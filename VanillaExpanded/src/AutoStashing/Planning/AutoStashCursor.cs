using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Owns one synchronous operation's source traversal and source-local destination retry state.</summary>
internal sealed class AutoStashCursor : IDisposable
{
    private readonly IEnumerator<(ItemSlot Slot, AutoStashSourcePass Pass)> sources;
    public ItemSlot? Source { get; private set; }
    public AutoStashSourcePass? Pass { get; private set; }
    public List<ItemSlot> Skipped { get; } = [];
    public List<ItemSlot> DeferredDirect { get; } = [];
    public HashSet<ItemSlot> RejectedDirect { get; } = [];
    public bool DirectPhase { get; set; }
    public bool RejectedDirectMove { get; set; }
    public bool DirectMergeFailed { get; set; }
    public bool AwaitingOutcome { get; set; }

    #region Public API
    /// <summary>Creates lazy source traversal; slots are enumerated as their pass is reached.</summary>
    public AutoStashCursor(IEnumerable<AutoStashSourcePass> passes) => sources = EnumerateSources(passes).GetEnumerator();

    /// <summary>Advances to a source and clears only its local retry state, retaining operation-wide exhausted failures.</summary>
    public bool MoveNext()
    {
        Skipped.Clear();
        DeferredDirect.Clear();
        RejectedDirect.Clear();
        DirectPhase = RejectedDirectMove = false;
        if (!sources.MoveNext())
        {
            Source = null;
            Pass = null;
            return false;
        }
        (Source, Pass) = sources.Current;
        return true;
    }

    /// <summary>Ends this source without restarting its enumeration or carrying stale retry state to the next source.</summary>
    public void FinishSource() => Source = null;

    /// <summary>Releases lazy inventory enumerators when execution completes or throws.</summary>
    public void Dispose() => sources.Dispose();
    #endregion

    #region Private
    /// <summary>Preserves engine inventory enumeration order and revisits it for each ordered policy pass.</summary>
    private static IEnumerable<(ItemSlot, AutoStashSourcePass)> EnumerateSources(IEnumerable<AutoStashSourcePass> passes)
    {
        foreach (AutoStashSourcePass pass in passes)
            foreach (ItemSlot slot in pass.Inventory)
                yield return (slot, pass);
    }
    #endregion
}
