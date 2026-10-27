# Wind speed

The wind indicator registers in the bottom-left `player-status` group at order 96. The default group is unlocked and movable; explicit saved layout permissions remain authoritative. It supports the shared HUD's movement, grouping, hidden-item editing, hover label, and context-menu settings.

`EnableWindSpeedOverlay` controls its visibility; global `EnableHudOverlays` also applies. `WindSpeedIndicatorStyle` selects `Icon` or `Text`. Both settings apply live and are exposed through ConfigLib and the indicator menu.

## Sampling and visibility

Every 300 ms, the indicator reads the magnitude of native `GetWindSpeedAt(player.Pos.XYZ)`, including local modifiers. Valid samples from the most recent five seconds are averaged before percentage conversion and visibility classification. Startup uses only available samples, so there is no ramp from an invented zero. Samples expire by elapsed client time; repeated refreshes at the same timestamp replace that sample. Missing or nonfinite input clears the average immediately. Player replacement, disabling, death, world exit, and a backwards clock clear the history.

The readout uses the windmill calculation `(int)(100 * speed)`: fractional percentages are truncated and values above 100% remain visible. These are native wind-strength percentages, not physical velocity units. It requires a living local player.

The bands control calm-wind visibility and transition history; they do not select the displayed percentage. Percentage changes refresh the reading even within the same band.

| Band | Entry boundary | Exit boundary |
| --- | --- | --- |
| Light | .05 | .035 |
| Moderate | .30 | .25 |
| Strong | .70 | .60 |

Values must exceed a boundary to activate or retain that band. Calm, missing, and nonfinite wind hide the indicator through the standard fade and space collapse. Replacement entities, disabling, death, and world exit clear classification history. Drawing uses prepared content and never samples gameplay state.

## Presentation and resource ownership

Icon mode uses the shared compact icon size and normal HUD text. The reading keeps its prepared font size as GUI scale changes; wider readings expand the footprint. The icon uses `assets/vanillaexpanded/textures/hud/wind-speed/icon.svg`, double-hook line art, below a localized whole-percentage reading such as `65%`. Text mode uses the short name `WIND` above the same reading and loads no icon. Wind strength does not alter the icon's shape.

The controller prepares localized strings and native font measurements. Composer-owned texture and text GuiElements own the rasterized SVG and lettering; text mode registers a name and reading through the shared native text composition. Native bounds position the rows, and fitting and clipping are cached before rendering. Live and source-trace compositions have independent elements and owned textures. Resources are reused across unchanged refreshes and released on style changes, API changes, completed exits, or world exit.
