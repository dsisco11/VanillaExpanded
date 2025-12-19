# VanillaExpanded

## Overview

VanillaExpanded is a mod for VintageStory that aims to add quality-of-life enhancements, missing functionality, and minor bug fixes while remaining true to the vanilla game and with minimal impacts to game balance.

![Game Version](https://img.shields.io/badge/Vintage%20Story-1.21.5+-blue)
![Version](https://img.shields.io/badge/Version-1.2.1-green)

## Features

### Auto-Stash

You can now bulk transfer matching items from your inventory into storage containers!

1. When interacting with a container, hold the `interact` button (default: right-click).
2. If you have items in your inventory which match items already in the container, a brief progress-bar will be shown at the center of the screen.
3. After the progress-bar completes, all matching items from your inventory will be moved into the container.

<https://github.com/user-attachments/assets/5be8da04-435e-4200-9ecb-41ea837d2a25>

### New Hotkeys

- Hotkey for quickly swapping a light source into the off-hand (default: `F`) or hotbar (default: `Shift + F`) when available (press again to swap the light source back into its prior slot).

### Alloy Calculator

When opening a firepit with a crucible, an Alloy Calculator dialog automatically appears alongside the firepit UI. This tool helps you calculate the exact metal ratios needed for creating alloys:

- **Select an alloy** from the dropdown to see its ingredient requirements
- **Adjust the target units** to specify how much metal you want to produce
- **Fine-tune ingredient ratios** using the sliders which automatically stay within valid alloy ranges
- **See required amounts** as item stacks for easy reference
- **Deposit button** automatically transfers the required ingredients from your inventory into the crucible, spreading them evenly across slots

The calculator remembers your settings per crucible, so your preferred alloy and ratios are restored when you reopen the dialog (currently not remembered across restarts).

### Quality of Life Additions

- Player respawn point appears as a glowing gear symbol on the ground.

### Implemented Missing Functionalities

- Ignitable things (firepits, etc) can now be ignited using lanterns (_oillamps & candles pending_).

### New Recipes

_Note: for decrafting recipes_  
_A low-tier tool (e.g. knife) yields ~50% of the original materials._  
_A high-tier tool (e.g. saw or shears) yields ~70% of the original materials._

- Planks & Firewood can be cut into sticks using a knife or saw (saw yields more).
- Linen & Leather bags can be de-crafted back into their crafting components using a knife or shears (shears yield more).
- Wattle fences/gates can be de-crafted back into sticks and wattle using a knife or saw (saw yields more).
- Metal tool-heads can be de-crafted back into metal-bits using a chisel.
- Metal arrow-heads can be de-crafted back into metal-bits using a chisel.

## License

This project is licensed under the Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International Public License for all users except Anego Studios.

Additional grant to Anego Studios:
Anego Studios and its affiliates are granted a perpetual, worldwide, non-exclusive, royalty-free license to use, modify, sublicense, and distribute this code, or derivative works, as part of the official VintageStory game or related products, under any terms of their choosing, without the obligations of Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International Public License, provided that attribution to the original author (“David Sisco”) is given in the game credits or documentation.
