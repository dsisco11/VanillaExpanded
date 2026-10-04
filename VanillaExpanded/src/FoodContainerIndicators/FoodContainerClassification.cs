using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.FoodContainerIndicators;

/// <summary>Distinguishes reusable food vessels from foods that also implement the meal interface.</summary>
internal static class FoodContainerClassification
{
    #region Public API
    /// <summary>Requires both meal handling and game vessel metadata; meal inheritance alone also includes pies.</summary>
    internal static bool IsFoodContainer(CollectibleObject collectible)
    {
        if (collectible.GetCollectibleInterface<IBlockMealContainer>() is null) return false;
        // Bowls declare their eaten vessel, while pots and crocks declare an emptied vessel or serving flag.
        var attributes = collectible.Attributes;
        return attributes?.IsTrue("mealContainer") == true
            || !string.IsNullOrEmpty(attributes?["eatenBlock"].AsString())
            || !string.IsNullOrEmpty(attributes?["emptiedBlockCode"].AsString());
    }
    #endregion
}
