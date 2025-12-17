using System;

using HarmonyLib;

using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.AlloyCalculator;

/// <summary>
/// Harmony patches to detect when the firepit GUI is opened/closed.
/// </summary>
[Harmony]
internal static class FirepitGuiPatch
{
    /// <summary>
    /// Event raised when a firepit dialog is opened.
    /// </summary>
    public static event Action<GuiDialogBlockEntityFirepit>? FirepitDialogOpened;

    /// <summary>
    /// Event raised when a firepit dialog is closed.
    /// </summary>
    public static event Action<GuiDialogBlockEntityFirepit>? FirepitDialogClosed;

    [HarmonyPatch(typeof(GuiDialogBlockEntityFirepit), nameof(GuiDialogBlockEntityFirepit.OnGuiOpened))]
    [HarmonyPostfix]
    private static void OnFirepitGuiOpened(GuiDialogBlockEntityFirepit __instance)
    {
        FirepitDialogOpened?.Invoke(__instance);
    }

    [HarmonyPatch(typeof(GuiDialogBlockEntityFirepit), nameof(GuiDialogBlockEntityFirepit.OnGuiClosed))]
    [HarmonyPostfix]
    private static void OnFirepitGuiClosed(GuiDialogBlockEntityFirepit __instance)
    {
        FirepitDialogClosed?.Invoke(__instance);
    }
}
