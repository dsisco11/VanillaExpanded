using System;

namespace VanillaExpanded;

/// <summary>
/// Configuration settings for the VanillaExpanded mod.
/// When ConfigLib is installed, these settings can be modified via an in-game GUI.
/// Otherwise, edit the generated VanillaExpanded.json file in the ModConfig folder.
/// </summary>
public class VanillaExpandedConfig
{
    #region Feature Toggles
    /// <summary>
    /// Enable auto-stashing items into containers by holding the interact key.
    /// </summary>
    public bool EnableAutoStash { get; set; } = true;

    /// <summary>
    /// Enable lighting fires using lanterns, candles, and oil lamps.
    /// </summary>
    public bool EnableIgnitionTools { get; set; } = true;

    /// <summary>
    /// Show a glowing decal at the player's respawn point.
    /// </summary>
    public bool EnableSpawnDecal { get; set; } = true;

    /// <summary>
    /// Enable the alloy calculator GUI for crucibles.
    /// </summary>
    public bool EnableAlloyCalculator { get; set; } = true;

    /// <summary>
    /// Explicitly disables the Harmony patch used to detect firepit GUI open/close events for the alloy calculator.
    /// Enable this only if it conflicts with other mods or breaks after a game update.
    /// Note: Changing this typically requires a restart to take effect.
    /// </summary>
    public bool DisableAlloyCalculatorPatch { get; set; } = false;

    /// <summary>
    /// Enable the hotkey to equip light sources to offhand/hotbar.
    /// </summary>
    public bool EnableEquipLightHotkey { get; set; } = true;

    /// <summary>Enables client-owned temporary quick-tool selections through native inventory flips.</summary>
    public bool EnableQuickTools { get; set; } = true;

    /// <summary>Selects the hovered quick-tool menu entry when its activation key is released.</summary>
    public bool QuickToolSelectOnRelease { get; set; } = true;

    #endregion

    #region Recipe Toggles
    /// <summary>
    /// Enable decrafting backpacks into leather using knife or shears.
    /// </summary>
    public bool EnableBackpackDecraft { get; set; } = true;

    /// <summary>
    /// Enable decrafting linen sacks into flax fibers using knife or shears.
    /// </summary>
    public bool EnableLinenSackDecraft { get; set; } = true;

    /// <summary>
    /// Enable recycling metal tool heads into metal bits using a chisel.
    /// </summary>
    public bool EnableMetalBitsRecycling { get; set; } = true;

    /// <summary>
    /// Enable crafting sticks from planks and firewood.
    /// </summary>
    public bool EnableStickRecipes { get; set; } = true;

    /// <summary>
    /// Enable decrafting wattle blocks into sticks.
    /// </summary>
    public bool EnableWattleDecraft { get; set; } = true;
    #endregion

    #region Timing Settings
    /// <summary>
    /// Time in seconds to hold the interact key before auto-stashing begins.
    /// </summary>
    public float AutoStashDelay { get; set; } = 0.5f;

    /// <summary>
    /// Time in seconds to hold the interact key before igniting a fire.
    /// </summary>
    public float IgnitionDelay { get; set; } = 0.5f;
    #endregion

    #region Visual Settings (Client-Side)
    /// <summary>
    /// Size of the spawn point decal (0.2 to 1.0).
    /// </summary>
    public float SpawnDecalSize { get; set; } = 0.4f;

    /// <summary>
    /// Render the auto-stash progress bar above all other base game UI (dialogs, HUD, etc.) instead of behind it.
    /// </summary>
    public bool AutoStashGuiRendersTopmost { get; set; } = true;
    #endregion
}
