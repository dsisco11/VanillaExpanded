# VanillaExpanded

## Overview

VanillaExpanded is a mod for VintageStory that aims to add quality-of-life enhancements, missing functionality, and minor bug fixes while remaining true to the vanilla game and with minimal impacts to game balance.

![Game Version](https://img.shields.io/badge/Vintage%20Story-1.21.5+-blue)
![Version](https://img.shields.io/github/v/release/dsisco11/VanillaExpanded?label=Version&color=green)

## Features

### Auto-Stash

You can now bulk transfer matching items from your inventory into storage containers!

1. When interacting with a container, hold the `interact` button (default: right-click).
2. If you have items in your inventory which match items already in the container, a brief progress-bar will be shown at the center of the screen.
3. After the progress-bar completes, all matching items from your inventory will be moved into the container.

For querns, first place a grindable item in the input, then hold right-click on the quern's body to top it up with matching items from your backpack and hotbar. The output slot is left untouched, and interacting with the upper grinding section still works normally.

<https://github.com/user-attachments/assets/5be8da04-435e-4200-9ecb-41ea837d2a25>

### Auto-Loot

Collect available corpse loot without shift-clicking each stack:

1. Harvest the corpse normally first. AutoLoot does not skin, butcher, generate loot, or bypass access restrictions.
2. Press and hold the right mouse button on a supported corpse. A normal click still opens the vanilla loot window; no extra hotkey or modifier is needed.
3. Keep holding while the radial **Auto Looting** indicator completes (0.5 seconds by default). It always renders above the loot window; moving the cursor over that window does not cancel the hold.
4. Everything that fits is transferred into your player storage. The loot window closes automatically if the corpse is empty. If any items remain, the window stays open with the overflow, and you must release before holding again for another pass.

Release, close the loot window, change world targets, move out of reach, or disable AutoLoot to cancel. Attached-container Auto-Stash takes precedence when its gesture is used.

AutoLoot is enabled by default. To turn it off or adjust the hold duration, open **Pause menu > Mod Settings > VanillaExpanded > Auto Loot** with ConfigLib installed. For manual configuration, see [Auto Loot settings](#auto-loot-settings).

AutoLoot runs entirely on the client: neither VanillaExpanded nor ConfigLib is required on the server for this feature. ConfigLib is optional for the client-side settings menu. AutoLoot and Auto-Stash can be enabled independently. The server still authorizes item transfers; completing the progress indicator does not guarantee that every item was collected.

**Supported targets:** dead, harvested, non-player corpses with the standard game loot window. Modded creatures using the same vanilla harvesting behavior are supported; custom looting systems, attached bags, player corpses, and general containers are not AutoLoot targets.

**If nothing moves:** check that the corpse has been harvested, still contains loot, and its normal loot window is open. Make room in your inventory, stay within interaction range, then release and hold right-click again. AutoLoot makes one collection attempt per hold; it does not keep retrying when your inventory is full.

AutoLoot has not been verified in game on unmodified servers, under network latency, or with simultaneous looters.

### Recipe Book

Open your inventory and click the **Recipe Book** button to the left of the crafting-grid output slot. The menu helps you find recipes and arrange their ingredients; it does not craft or collect the result automatically.

**Browse recipes**

- Search by item name or item code. Use the category icons to narrow the list; hover an icon to see its name. **All** shows every output, and **Other** groups outputs without a specific category.
- Check **Hide uncraftable** to show recipes with enough usable ingredients for one craft. Search and category filters remain active.
- Choose **Availability** (default) to prioritize supported recipes and uses for items in your crafting grid, or **Name** to browse alphabetically. To find uses for an ingredient, put it in the grid; duplicate stacks and larger quantities do not increase its priority.
- Changing search, category, Hide uncraftable, or sort returns the list to the top. Use the search field's **x**, **Clear search**, or **Show uncraftable** to relax the corresponding filter without clearing the others.

**Inspect and place ingredients**

Hover or keyboard-focus a tile to see the item's description, recipe, and available/required ingredient counts. Counts update with your inventory. A red border means no required ingredients are available; yellow means some are available. Darkened icons indicate unsupported placement; inspect their tooltips for an explanation.

- **Click:** place available ingredients for one craft, leaving missing ingredients absent.
- **Shift-click:** fill ingredient positions as far as available stock and stack limits allow.
- **Craft afterward:** click the normal crafting result slot.
- **Previous / Next:** choose and pin an alternative recipe for the selected output. **Auto** restores automatic choice. Hide uncraftable can hide an unavailable pin even if another alternative is craftable; uncheck it to reveal the pin. Selecting another output or changing category clears the pin.
- **Handbook:** open the selected output's survival handbook page without placing ingredients.

Ingredients come from your hotbar, backpack storage, and the nine crafting-grid inputs, not cursor-held items, equipment, or remote containers. The book supports the 3x3 crafting grid, not cooking, smithing, knapping, clay forming, or other workstations. Leave room in carried storage for displaced grid contents. Interrupted transfers can leave a partial arrangement; check the grid before trying again.

**Options and reopening**

Click the titlebar's **sliders** icon to open **Options**:

| Option | Default | Behavior |
| --- | --- | --- |
| **Reopen with inventory** | On | Reopen the book with your inventory if it was open when the inventory closed. Turn it off to open the book manually. |
| **Recipe tile size** | 45 | Enter a whole number from 30 to 90 GUI units. Valid changes resize tiles immediately while keeping your browsing state. Invalid edits restore the last accepted size with an error on Enter or leaving the field. |

Both options are saved on your client across game sessions. Category, search, sort, and your place in the list are remembered within the current world; Hide uncraftable resets when the book reopens.

Click outside Options or press Escape to dismiss the dropdown. Otherwise, Escape or the inventory hotkey closes the inventory and book together; typing in search or tile size suppresses the inventory hotkey. Tab moves between controls, including the dropdown's controls, and arrow keys navigate focused categories, sorting, and recipes. Enter, Space, or keypad Enter activates a focused recipe; hold Shift for the fill action. Opening with the Recipe Book button focuses search; automatic reopening leaves focus with the inventory.

The book is enabled by default. Toggle it under **Pause menu > Mod Settings > VanillaExpanded > Recipe Book** with ConfigLib, or set `EnableRecipeBook` in `ModConfig/VanillaExpanded.json`. Disabling applies immediately through ConfigLib; enabling requires re-entering the world or restarting.

Some recipes use generic output previews or do not support placement. Recipe/modpack and multiplayer compatibility have not been exhaustively verified in game. See the [Recipe Book developer guide](docs/RecipeBook.md) for architecture and maintenance details.

### Quick Tools

Hold the Quick Tools hotkey (assign it in Controls) to open a radial menu of available tools, weapons, healing items, and light sources from your hotbar and backpack. Select an item to equip it instantly; use the center **Unequip** action to restore your previous item or stow the item currently held.

### Approximate Time

Hover over any visible HUD indicator while the mouse is free (for example, with your inventory open) to see its title. HUD indicators keep their space while hidden, so the other indicators stay in place. The time icon remains rightmost in the bottom-left group. Each bottom-left icon has a faint black circle that fades smoothly to transparent at its edge and hides with the icon.

A compact texture icon at the right end of the status group beside the bottom-left edge of the toolbar indicates estimated dawn, morning, noon, afternoon, dusk, or night. Icons follow seasonal daylight and the local solar path. Local sunlight, including sunlight reaching covered spaces, recalibrates the estimate; darkness gradually introduces up to eight hours of error on a normal day. The server saves time spent online without sunlight across reconnects and world reloads; sunlight resets it. Multiplayer tracking requires the mod on the server. There is no numeric label. Toggle it live with `EnableApproximateTimeOverlay`.

### Weather Conditions

Small weather icons appear beside the toolbar’s bottom-left edge during rain, snow, hail, or fog. Fog can appear alongside precipitation. The icons use transparent textures and hide during fair weather. Toggle them with `EnableWeatherConditionsOverlay`; changes apply immediately.

### Temporal Rift Activity

A fissure icon beside the toolbar's bottom-left edge, before the time icon, shows the current rift activity. The tear widens and branches from pale cyan at low activity through cyan at medium, amber at high or very high, and red-orange at apocalyptic. It hides during calm activity. Toggle it live with `EnableRiftActivityOverlay`.

### Temporal Storm Warning

A fractured iris icon beside the toolbar's bottom-left edge, between rift activity and approximate time, warns of temporal storms. A narrow eye indicates an approaching storm, an open eye with separated portal fragments indicates an imminent storm, and a ruptured portal with outward spikes indicates an active storm. The imminent icon pulses in opacity, accelerating from one pulse every two seconds to three pulses per second as the storm nears. During waning, the active icon starts the same accelerating pulse until the storm ends. It hides between storms while retaining its space. Toggle it live with `EnableTemporalStormOverlay`.

### Body Temperature Warning

A compact thermometer appears just left of the toolbar, aligned with its bottom edge, when body temperature becomes too low or high. Its colored column rises with temperature, and the adjacent reading shows whole degrees (for example, `34°C`). The icon has an outline for contrast.

Cold warnings follow the game’s synchronized freezing effect: the icon appears while that effect is active, and its blue color deepens with the effect’s intensity. Elevated-temperature warnings use the converted reading and appear in orange above 42°C and red above 44°C for the default player. Vanilla’s normal warmth ceiling does not reach these thresholds. The numeric reading uses the character panel’s temperature conversion, rounded to whole degrees. Heat warnings are informational: the game has no native overheating-damage threshold. A quarter-degree recovery margin prevents the heat warning flickering near its visibility boundary.

While a temperature warning is visible, orange chevrons above the thermometer indicate warming and blue chevrons below indicate cooling. One chevron means slow change; two mean fast change.

The warning uses synchronized body temperature and stays hidden in Creative and Spectator modes. Toggle it through `EnableBodyTemperatureOverlay`, or turn off `ShowBodyTemperatureReading` for the icon alone.

### Bow Ammunition

While holding a supported base-game bow in your main hand, a compact `<count>x <arrow icon>` indicator shows the next arrow type and its total positive quantity in inventories eligible for bow selection.

The arrow icon focuses on the arrowhead over a translucent black circular background. The default placement is just above the saturation meter, aligned with its left edge. With no eligible arrows, it shows a muted arrow symbol and localized `No arrows / 0`. Unsupported custom or derived bows hide the indicator.

### New Hotkeys

- Hotkey for quickly swapping a light source into the off-hand (default: `F`) or hotbar (default: `Shift + F`) when available (press again to swap the light source back into its prior slot).

- Hotkey for quickly opening the Quick Tools radial menu (default: `R`).

### Alloy Calculator

When opening a firepit with a crucible, an Alloy Calculator dialog automatically appears alongside the firepit UI. This tool helps you calculate the exact metal ratios needed for creating alloys:

- **Select an alloy** from the dropdown to see its ingredient requirements
- **Adjust the target units** to specify how much metal you want to produce
- **Fine-tune ingredient ratios** using the sliders which automatically stay within valid alloy ranges
- **See required amounts** as item stacks for easy reference
- **Preview fuel needs** with an indicator showing the preferred available fuel and estimated count for the selected batch, even before depositing ingredients or filling the fuel slot
- **See prewarming options** when available lower-temperature fuels can save smelting fuel: the indicator cycles through useful options and the amount of each needed to save at least one item of the selected smelting fuel. Prewarming is not automatic; burn the displayed amount of one option before switching fuel or using **Deposit**, which immediately replaces unsuitable queued fuel.
- **Deposit button** automatically transfers the required ingredients from your backpack, hotbar, and opened storage containers into the crucible. It first reserves space for each selected ingredient form (such as nuggets and metal bits), then spreads the ingredients evenly across spare slots without mixing incompatible stacks.

The calculator remembers your preferred alloy and ratios per crucible when you reopen the dialog, but not across restarts.

### Animal Sex Indicators

Small male and female symbols appear above living animals of generation 1 or higher, including young animals. Pregnant females display a heart-marked female symbol. Icons fade with distance and are hidden by walls and terrain. They are enabled by default; size, viewing distance, opacity, and colors can be adjusted in the [Animal Sex Indicators settings](#animal-sex-indicators-1).

### Quality of Life Additions

- Player respawn point appears as a glowing gear symbol on the ground.

### Implemented Missing Functionalities

- Ignitable things (firepits, etc) can now be ignited using lanterns, oil lamps, and candles.

### New Recipes

_Note: for decrafting recipes_  
_A low-tier tool (e.g. knife) yields ~50% of the original materials._  
_A high-tier tool (e.g. saw or shears) yields ~70% of the original materials._

- Planks & Firewood can be cut into sticks using a knife or saw (saw yields more).
- Linen & Leather bags can be de-crafted back into their crafting components using a knife or shears (shears yield more).
- Wattle fences/gates can be de-crafted back into sticks and wattle using a knife or saw (saw yields more).
- Metal tool-heads can be de-crafted back into metal-bits using a chisel.
- Metal arrow-heads can be de-crafted back into metal-bits using a chisel.

## Configuration

VanillaExpanded supports [ConfigLib](https://mods.vintagestory.at/configlib) for in-game configuration. If ConfigLib is installed, a "Mod Settings" button appears in the pause menu where you can toggle features on or off.

Without ConfigLib, settings can be edited manually in `ModConfig/VanillaExpanded.json`.

### Available Settings

The sections and setting order below match the ConfigLib menu. Setting names are the keys used in `ModConfig/VanillaExpanded.json`; ranges and choices are those offered by ConfigLib.

#### Auto Stash

| Setting                      | Default | Range / choices | Description                                                             |
| ---------------------------- | ------- | --------------- | ----------------------------------------------------------------------- |
| `EnableAutoStash` \*         | `true`  | `true`, `false` | Enable auto-stashing items into containers by holding the interact key. |
| `AutoStashDelay`             | `0.5`   | 0.1–2           | Time in seconds to hold the interact key before auto-stashing begins.   |
| `AutoStashGuiRendersTopmost` | `true`  | `true`, `false` | Render the auto-stash progress bar above dialogs and other game UI.     |

#### Auto Loot Settings

| Setting          | Default | Range / choices         | Description                                                                                                        |
| ---------------- | ------- | ----------------------- | ------------------------------------------------------------------------------------------------------------------ |
| `EnableAutoLoot` | `true`  | `true`, `false`         | Enable hold-to-loot for supported harvested corpses. Client-local; disabling cancels current progress immediately. |
| `AutoLootDelay`  | `0.5`   | 0.1-2 seconds, step 0.1 | Hold duration before collection. Client-local; ConfigLib changes apply immediately, including during a hold.       |

There is no AutoLoot layering option: its progress always renders above dialogs, independently of `AutoStashGuiRendersTopmost`. Both AutoLoot controls work live through ConfigLib, including enabling the feature after loading the world. Without ConfigLib, edit the client's `ModConfig/VanillaExpanded.json` and restart to load the changes. Invalid non-positive or non-finite manual delays cancel the attempt and log an error rather than collecting.

#### Ignition Tools

| Setting                  | Default | Range / choices | Description                                                      |
| ------------------------ | ------- | --------------- | ---------------------------------------------------------------- |
| `EnableIgnitionTools` \* | `true`  | `true`, `false` | Enable lighting fires using lanterns, candles, and oil lamps.    |
| `IgnitionDelay`          | `0.5`   | 0.1–2           | Time in seconds to hold the interact key before igniting a fire. |

#### Spawn Decal

| Setting               | Default | Range / choices | Description                                         |
| --------------------- | ------- | --------------- | --------------------------------------------------- |
| `EnableSpawnDecal` \* | `true`  | `true`, `false` | Show a glowing decal at the player's respawn point. |
| `SpawnDecalSize`      | `0.4`   | 0.2–1           | Size of the spawn point decal.                      |

#### Alloy Calculator

| Setting                       | Default | Range / choices | Description                                                                                                                            |
| ----------------------------- | ------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `EnableAlloyCalculator`       | `true`  | `true`, `false` | Enable the alloy calculator GUI for crucibles.                                                                                         |
| `DisableAlloyCalculatorPatch` | `false` | `true`, `false` | Disable firepit GUI detection for the alloy calculator. Requires a world reload or restart; prevents automatic opening while disabled. |

#### Recipe Book

| Setting              | Default | Range / choices | Description |
| -------------------- | ------- | --------------- | ----------- |
| `EnableRecipeBook` | `true` | `true`, `false` | Enable the client-side recipe book. Disabling applies immediately; enabling requires a world reload or restart. |

#### Gameplay

| Setting                             | Default | Range / choices | Description                                                                                                 |
| ----------------------------------- | ------- | --------------- | ----------------------------------------------------------------------------------------------------------- |
| `EnableBucketSourceProtectionPatch` | `true`  | `true`, `false` | Protect existing water blocks from bucket spills when liquid source transport is disabled in world options. |
| `EnableEquipLightHotkey`            | `true`  | `true`, `false` | Enable the hotkey to equip light sources to offhand/hotbar.                                                 |

#### Recipe Toggles

| Setting                    | Default | Range / choices | Description                                                           |
| -------------------------- | ------- | --------------- | --------------------------------------------------------------------- |
| `EnableBackpackDecraft`    | `true`  | `true`, `false` | Enable decrafting backpacks into leather using knife or shears.       |
| `EnableLinenSackDecraft`   | `true`  | `true`, `false` | Enable decrafting linen sacks into flax fibers using knife or shears. |
| `EnableMetalBitsRecycling` | `true`  | `true`, `false` | Enable recycling metal tool heads into metal bits using a chisel.     |
| `EnableStickRecipes`       | `true`  | `true`, `false` | Enable crafting sticks from planks and firewood.                      |
| `EnableWattleDecraft`      | `true`  | `true`, `false` | Enable decrafting wattle blocks into sticks.                          |

#### Item Slot Indicators

| Setting                                     | Default                | Range / choices                                              | Description                                                                                                                                                                                                         |
| ------------------------------------------- | ---------------------- | ------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `EnablePerishableItemFreshnessIndicators`   | `true`                 | `true`, `false`                                              | Show freshness on perishable item slots using the selected indicator style.                                                                                                                                         |
| `EnablePreparationIndicators`               | `true`                 | `true`, `false`                                              | Show preparation progress on item slots.                                                                                                                                                                            |
| `EnableClothingIndicators`                  | `true`                 | `true`, `false`                                              | Show clothing condition on item slots.                                                                                                                                                                              |
| `EnableLiquidContainerIndicators`           | `true`                 | `true`, `false`                                              | Show liquid volume on liquid containers and watering cans.                                                                                                                                                          |
| `EnableCrucibleIndicators`                  | `true`                 | `true`, `false`                                              | Show metal amount in filled crucibles and firepit input crucibles.                                                                                                                                                  |
| `EnableNightVisionFuelIndicators`           | `true`                 | `true`, `false`                                              | Show night-vision fuel on item slots.                                                                                                                                                                               |
| `PerishableItemFreshnessIndicatorStyle`     | `freshness-background` | `freshness-background`, `freshness-outline`, `freshness-bar` | Choose a bottom-up background fill, colored outline, or horizontal progress bar.                                                                                                                                    |
| `PerishableItemFreshnessIndicatorIntensity` | `0.35`                 | 0.05–1                                                       | Opacity of freshness backgrounds and outlines; progress bars remain fully opaque.                                                                                                                                   |
| `FoodLevelIndicatorStyle`                   | `progress-bar`         | `progress-bar`, `slot-background`                            | Show remaining portions for any item with serving data, not just food vessels, as a progress bar or animated particles. Full pie slices show 100%; vessels use their serving capacity. Freshness has its own style. |
| `EnableLiquidSloshEffect`                   | `true`                 | `true`, `false`                                              | Animate liquid volume indicators; disabling retains plain fills and pauses the liquid simulation.                                                                                                                   |
| `EnableCrucibleEffect`                      | `true`                 | `true`, `false`                                              | Animate solid and molten metal; disabling retains a plain amount indicator.                                                                                                                                         |
| `CrucibleIndicatorCapacityUnits`            | `2560`                 | 100–25600                                                    | Metal units represented by a full indicator; a visual reference, not a container capacity limit.                                                                                                                    |

#### Radial Menus

| Setting                       | Default | Range / choices | Description                                                                                              |
| ----------------------------- | ------- | --------------- | -------------------------------------------------------------------------------------------------------- |
| `RadialMenuBackdropOpacity`   | `0.2`   | 0–1             | Opacity of the screen-darkening backdrop behind radial menus; 0 disables it.                             |
| `RadialMenuOpacity`           | `0.5`   | 0–1             | Background and border opacity of radial menus. Icons and labels retain their opacity.                    |
| `EnableToolModeRadialMenu` \* | `true`  | `true`, `false` | Replace base-game tool-mode grids with a radial menu.                                                    |
| `ToolModeMenuSize`            | `1`     | 0.15–2.5        | Scale the tool-mode radial menu; 1 keeps its default size.                                               |
| `ToolModeRingSize`            | `1`     | 0.15–2.5        | Scale tool-mode option-ring thickness while preserving the center circle and gaps.                       |
| `ToolModeCenterSize`          | `1`     | 0.15–2.5        | Scale the tool-mode center circle; 1 keeps its default size.                                             |
| `ToolModeIconSize`            | `0.75`  | 0.15–2.5        | Scale icons within tool-mode wedges; 1 fills the available icon space. Larger values may overlap wedges. |
| `QuickToolSelectOnRelease`    | `true`  | `true`, `false` | Select the hovered quick-tool entry when the activation key is released.                                 |
| `QuickSwapMenuSize`           | `1`     | 0.15–2.5        | Scale the quick-swap radial menu; 1 keeps its default size.                                              |

#### Animal Sex Indicators

| Setting                     | Default    | Range / choices | Description                                                                                              |
| --------------------------- | ---------- | --------------- | -------------------------------------------------------------------------------------------------------- |
| `EnableAnimalSexIndicators` | `true`     | `true`, `false` | Show terrain-occluded sex symbols above living generation 1+ animals, with a heart for pregnant females. |
| `AnimalSexIndicatorSize`    | `0.25`     | 0.05–1          | Base size in blocks, rendered at half this value (default visible size: 0.125 blocks).                   |
| `AnimalSexIndicatorRange`   | `8`        | 1–8             | Viewing distance in blocks; symbols fade over the farther half of this range.                            |
| `AnimalSexIndicatorOpacity` | `0.75`     | 0–1             | Maximum opacity before distance fading; 0.75 means 75%.                                                  |
| `AnimalMaleIconColor`       | `5089023`  | 0–16777215      | Packed RGB decimal color; default blue (#4DA6FF).                                                        |
| `AnimalFemaleIconColor`     | `16741813` | 0–16777215      | Packed RGB decimal color for female and pregnant-female icons; default pink (#FF75B5).                   |

#### HUD Overlays

| Setting                      | Default      | Range / choices                                                                                                                                                                                                              | Description                                                                             |
| ---------------------------- | ------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| `EnableHudOverlays`          | `true`       | `true`, `false`                                                                                                                                                                                                              | Enable passive HUD overlays; applies live without restarting.                           |
| `EnableBowAmmunitionOverlay` | `true`       | `true`, `false`                                                                                                                                                                                                              | Show the next-arrow type and type-total quantity while holding a supported bow.         |
| `EnableBodyTemperatureOverlay` | `true` | `true`, `false` | Show the dynamic thermometer when too cold or hot; applies live. |
| `ShowBodyTemperatureReading` | `true` | `true`, `false` | Show whole degrees beside the thermometer; disable for the icon alone. |
| `HeldItemStatusAnchor`       | `saturation` | `saturation`, `hotbar`, `screen-left-top`, `screen-center-top`, `screen-right-top`, `screen-left-middle`, `screen-center-middle`, `screen-right-middle`, `screen-left-bottom`, `screen-center-bottom`, `screen-right-bottom` | Select native docking or a screen position. Unknown values fall back to saturation.     |
| `HeldItemStatusOffsetX`      | `-4`         | -2048–2048 in ConfigLib                                                                                                                                                                                                      | Horizontal offset in GUI units; positive moves right. Nonfinite values fall back to -4. |
| `HeldItemStatusOffsetY`      | `0`          | -2048–2048 in ConfigLib                                                                                                                                                                                                      | Vertical offset in GUI units; positive moves down. Nonfinite values fall back to 0.     |
| `EnableWeatherConditionsOverlay` | `true` | `true`, `false` | Show texture icons for active rain, snow, hail, and weather fog; applies live. |
| `EnableRiftActivityOverlay` | `true` | `true`, `false` | Show a fissure icon for temporal rift activity; hidden during calm activity. Applies live. |
| `EnableTemporalStormOverlay` | `true` | `true`, `false` | Show approaching, imminent, and active temporal storms with a fractured iris icon. Applies live. |
| `EnableApproximateTimeOverlay` | `true` | `true`, `false` | Show sunlight-calibrated approximate time; applies live. |

The two offsets are shared across placement choices. The saturation default compensates for content padding and leaves a four-unit gap above the meter. Hotbar docking attaches the group's left-middle to the hotbar's right-middle; screen placements use matching attachment and pivot points. Native viewport clamping keeps the group within the safe rectangle. Placement changes apply live and retain cached presentation; restored content receives a fresh sample before display. Without ConfigLib, edit the JSON file and restart the game to load manual changes.

- Settings marked with an asterisk require a world reload when enabling a feature that was disabled when the world loaded, because its systems or patches were not installed.

Recipe-toggle changes require a world reload to rebuild the available recipes. `DisableAlloyCalculatorPatch` also requires a world reload or restart. The normal `EnableAlloyCalculator` toggle can change live when its patch is installed.

## Testing

Tests are organized by feature in [VanillaExpanded.Tests](VanillaExpanded.Tests/).
Use .NET 10 and set `VINTAGE_STORY` to the matching VintageStory client
installation.

### Test Organization

- [Unit tests](VanillaExpanded.Tests/Unit/): isolated rules and calculations.
- [Component tests](VanillaExpanded.Tests/Component/): production components working together with mocked external services.
- [Integration tests](VanillaExpanded.Tests/Integration/): native game compatibility, including GUI input and crafting transfers.

### Running Tests

```bash
# Run all tests
dotnet test

# Run only unit tests (fast feedback)
dotnet test --filter "Category=Unit"

# Run only component tests
dotnet test --filter "Category=Component"

# Run only integration tests
dotnet test --filter "Category=Integration"

# Run tests without declared native graphics requirements
dotnet test --filter "RequiresNative!=Cairo"

# Run tests requiring native graphics
dotnet test VanillaExpanded.Tests/VanillaExpanded.Tests.csproj --filter "RequiresNative=Cairo"

# Run tests for a specific feature
dotnet test --filter "FullyQualifiedName~AutoStashing"
```

When adding tests, reuse the existing [mocks](VanillaExpanded.Tests/Mocks/) and [test support](VanillaExpanded.Tests/TestSupport/). See the [contributor instructions](.github/copilot-instructions.md) for conventions.

### Native dependencies and assertion policy

GUI tests require native graphics libraries. On Windows, the test project copies
Cairo from the game installation. On Linux, Cairo, fontconfig, and fonts must be
available in the host environment. The `RequiresNative` filters select native
dependencies independently of test categories.

Tests verify behavior rather than rendered pixels or incidental appearance.
See the [dependency inventory](docs/CairoTestInventory.md#7-phase-4-native-dependency-decision)
for native test boundaries and detailed assertion rules.

## License

This project is licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International Public License for all users except Anego Studios.

Additional grant to Anego Studios:
Anego Studios and its affiliates are granted a perpetual, worldwide, non-exclusive, royalty-free license to use, modify, sublicense, and distribute this code, or derivative works, as part of the official VintageStory game or related products, under any terms of their choosing, without the obligations of Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International Public License, provided that attribution to the original author (“David Sisco”) is given in the game credits or documentation.

## Developer documentation

- [Recipe Book](docs/RecipeBook.md): filtering, ordering, UI lifecycle, and rendering.
- [Alloy Calculator](docs/AlloyCalculator.md): calculation and inventory-transfer design.
- [Item-slot indicator effects](docs/ItemSlotIndicatorEffects.md): registration, rendering, and resource ownership.
- [Anchored HUD overlays](docs/hud-overlays/HudOverlays.md): passive registration, configuration, and resource ownership contracts.
