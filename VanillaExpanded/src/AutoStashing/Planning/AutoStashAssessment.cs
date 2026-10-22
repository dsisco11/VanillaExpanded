using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VanillaExpanded.AutoStashing.Planning;

/// <summary>Reports candidate membership, apparent capacity and independent help representatives for one read-only query.</summary>
internal sealed class AutoStashAssessment
{
    public static AutoStashAssessment Empty { get; } = new([], [], AutoStashCapacity.Unavailable, []);
    public IReadOnlySet<int> CandidateItemIds { get; }
    public IReadOnlySet<int> AvailableItemIds { get; }
    public bool HasCandidates => CandidateItemIds.Count > 0;
    public AutoStashCapacity Capacity { get; }
    public ItemStack[] DisplayStacks { get; }
    public bool HasAvailableItems => AvailableItemIds.Count > 0;
    public bool CanStartInteraction { get; internal set; } = true;
    public EnumHandling StartHandling { get; internal set; } = EnumHandling.PreventDefault;

    #region Public API
    /// <summary>Captures query-local IDs and already cloned representatives; availability remains advisory.</summary>
    public AutoStashAssessment(HashSet<int> candidates, HashSet<int> available, AutoStashCapacity capacity, ItemStack[] displayStacks)
    {
        CandidateItemIds = candidates;
        AvailableItemIds = available;
        Capacity = capacity;
        DisplayStacks = displayStacks;
    }
    #endregion
}
