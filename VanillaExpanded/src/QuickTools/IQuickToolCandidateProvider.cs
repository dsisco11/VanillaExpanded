using Vintagestory.API.Common;

namespace VanillaExpanded.QuickTools;

/// <summary>Resolves one stable semantic entry to a current existing stack.</summary>
public interface IQuickToolCandidateProvider
{
    /// <summary>Gets the entry identifier fixed by the quick-tool layout.</summary>
    string EntryId { get; }

    /// <summary>Resolves the current winner without moving items.</summary>
    QuickToolCandidate? Resolve(IPlayerInventoryManager manager, ItemSlot offhand);
}
