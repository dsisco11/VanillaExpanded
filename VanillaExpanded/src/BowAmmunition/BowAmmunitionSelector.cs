using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.BowAmmunition;

/// <summary>Uses the verified base bow's read-only selection method without changing firing behavior.</summary>
internal static class BowAmmunitionSelector
{
    private static readonly System.Func<ItemBow, EntityAgent, ItemSlot>? select = Bind();
    #region Public API
    /// <summary>Accepts only the exact installed base bow with a verified selector binding.</summary>
    public static bool IsSupported(CollectibleObject? collectible) => select != null && collectible?.GetType() == typeof(ItemBow);
    /// <summary>Invokes the owning selector without retaining the returned live slot.</summary>
    public static ItemSlot? Select(ItemBow bow, EntityAgent player)
    {
        if (!IsSupported(bow)) return null;
        return select!(bow, player);
    }
    #endregion
    #region Private
    /// <summary>Binds once and fails closed when the installed method no longer satisfies its contract.</summary>
    private static System.Func<ItemBow, EntityAgent, ItemSlot>? Bind()
    {
        var method = AccessTools.DeclaredMethod(typeof(ItemBow), "GetNextArrow", new[] { typeof(EntityAgent) });
        if (method == null || method.DeclaringType != typeof(ItemBow) || !method.IsFamily || method.IsVirtual
            || method.ReturnType != typeof(ItemSlot)) return null;
        // The validated method is nonvirtual; Harmony's default binding calls that exact method.
        try { return AccessTools.MethodDelegate<System.Func<ItemBow, EntityAgent, ItemSlot>>(method); }
        catch (ArgumentException) { return null; }
        catch (MemberAccessException) { return null; }
    }
    #endregion
}
