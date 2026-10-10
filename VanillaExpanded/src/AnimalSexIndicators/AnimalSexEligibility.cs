using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VanillaExpanded.AnimalSexIndicators;

/// <summary>Identifies living bred animals using synchronized generation and resolved gender metadata.</summary>
internal static class AnimalSexEligibility
{
    #region Public API
    /// <summary>Returns a recognized sex only for living animals of generation one or greater.</summary>
    public static string? GetSex(Entity entity)
    {
        // Require a creature and breeding metadata; gender comes from variants, never code suffixes.
        if (entity is not EntityAgent || entity is EntityPlayer || !entity.Alive
            || entity.WatchedAttributes.GetInt("generation", 0) < 1) return null;
        string? gender = entity.Properties?.Variant.TryGetValue("gender", out string value) == true ? value : null;
        return gender is "male" or "female" ? gender : null;
    }

    /// <summary>Selects the pregnancy variant from current synchronized breeding state.</summary>
    public static string? GetIconVariant(Entity entity)
    {
        // Preserve eligibility and read the live watched tree so conception and birth update immediately.
        string? sex = GetSex(entity);
        return sex == "female" && entity.WatchedAttributes.GetTreeAttribute("multiply")?.GetBool("isPregnant", false) == true
            ? "female-pregnant" : sex;
    }
    #endregion
}
