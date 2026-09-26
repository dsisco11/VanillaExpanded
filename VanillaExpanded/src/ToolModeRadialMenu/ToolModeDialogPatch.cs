using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ToolModeRadialMenu;

/// <summary>Intercepts the base tool-mode dialog only when the radial replacement can open.</summary>
[HarmonyPatch(typeof(GuiDialogToolMode), "OnKeyCombinationToggle")]
internal static class ToolModeDialogPatch
{
    /// <summary>Suppresses the vanilla grid after the radial menu accepts the same interaction.</summary>
    [HarmonyPrefix]
    private static bool BeforeToolModeToggle(ref bool __result)
    {
        if (ToolModeRadialMenuSystem.Active?.TryOpen() != true) return true;
        __result = true;
        return false;
    }
}