# Weather conditions

The weather indicator occupies 36 × 36 GUI units before native GUI scaling in the top-right `world-status` group below the calendar while visible and releases its space when hidden. The composite artwork consists of transparent 64 × 64 textures drawn at the same origin, using one shared backdrop. Fog and haze share one channel; thunder has its own channel. Shapes distinguish weather types and intensity levels independently of colour.

## Composition and visibility

Native GUI scaling applies to the measured footprint and its child bounds. Composite sources use a 64 × 64 transparent canvas; fallback sources use a 96 × 96 transparent canvas.

The active composite set consists of 15 textures. Independent precipitation, fog/haze, and thunder layers combine inside one fixed footprint. A missing or incomplete composite pack uses precipitation and fog textures from `textures/hud/weather/`. When both are active, their native bounds divide the slot horizontally. Thunder and intensity have no fallback artwork.

The indicator hides and releases its layout space when precipitation, fog/haze, and thunder are all inactive, in both icon and text styles. Cloud coverage is not sampled or displayed; the legacy sky assets are not required or loaded. Disabling the feature, losing the player or climate data, and leaving the world clear presentation and transition history. Changing API ownership reloads texture bindings; unchanged layer identities reuse their bindings across GUI scale and weather changes. Native elements own rasterized SVG textures for both composite and fallback artwork.

See [wind speed](../wind-speed/WindSpeed.md) for local wind readings.

## Weather inputs

Sampling runs every 300 ms, outside rendering.

| Channel | Source and meaning |
| --- | --- |
| Precipitation | Local current climate rainfall, with native `BlendedPrecType`. Automatic selection uses temperature below `snowThresholdTemp` for snow and otherwise rain. Explicit rain, snow, and hail remain explicit. Invalid automatic inputs do not invent rain. |
| Fog/haze | Weather snapshot fog density multiplied by its weight, combined with weather flat fog evaluated at camera height. Flat fog uses `1 - exp(-(eyeY - layerY) * density)`, clamped to 0–1, with `layerY = seaLevel + weighted FlatFogYPos`. Negative density represents mist below the layer. The stronger classified fog/mist band wins. Underwater and temporal scene fog are not sampled. |
| Thunder | Native near and distant lightning rates, summed and multiplied by local `RainCloudOverlay`, clamped to 0–1. The native minimum-temperature gate must pass. This describes current thunder activity, not a forecast or an individual lightning flash. Rain and wind alone never imply thunder. |

## Intensity bands

Channels vary independently. Values must exceed an entry boundary to advance; an active band persists until its lower exit boundary is crossed. Non-finite intensity inputs clear that channel.

| Channel | Entry boundaries | Exit boundaries |
| --- | --- | --- |
| Precipitation: absent, light, moderate, heavy | .01, .25, .65 | .005, .20, .55 |
| Distance fog/haze: absent, light, moderate, dense | .0025, .008, .020 | .002, .006, .016 |
| Local mist opacity: absent, light, moderate, dense | .15, .40, .75 | .12, .32, .65 |
| Thunder: absent, light, moderate, heavy | .001, .25, .65 | .0005, .20, .55 |

The native particle renderer stops particles at rainfall ≤ .02, so the precipitation entry/exit thresholds allow a trace-precipitation reading after particles stop.

## Artwork contract

Composite layers live under `assets/vanillaexpanded/textures/hud/weather/composite/`. Each icon is loaded directly from its editable `.svg` source and rasterized at 64 × 64 for display. Composite loading requires the complete active SVG set; an absent or partial set independently selects `rain.svg`, `snow.svg`, `hail.svg`, and `fog.svg` from `textures/hud/weather/` without requesting missing composite textures. These fallback sources retain their transparent 96 × 96 canvases and also use the shared 64 × 64 SVG rasterizer.

| Draw order | Texture names |
| --- | --- |
| Precipitation | `rain-light`, `rain-moderate`, `rain-heavy`; equivalent `snow-*` and `hail-*` sets |
| Fog/haze | `fog-light`, `fog-moderate`, `fog-dense` |
| Thunder | `thunder-light`, `thunder-moderate`, `thunder-heavy` |

Each composite layer uses the entire transparent 64 × 64 canvas. Spatial allocation belongs in the artwork; no condition moves another composite layer. Thunder draws last. Keep shape boundaries distinct in monochrome and avoid overlap that erases precipitation type. Pack availability is cached for the client API binding and cleared with presentation state. Native texture elements rasterize only the selected SVG layers, own the generated textures, and dispose them on replacement or teardown. Each live or source-trace composition has distinct elements and bounds, including independently owned fallback textures. A single fallback fills the slot; simultaneous precipitation and fog use vertically centered half-size slots. Layer identities, local bounds, clipping and the shared backdrop anchor are prepared before rendering.

## Artwork appearance

The active artwork uses distinct precipitation marks without a cloud canopy or outer ring. Straight fog bands sit below precipitation. Rain uses fine cyan diagonal streaks, snow uses simple six-spoke white flakes, and hail uses pale-blue solid round pellets with short motion ticks. A yellow lightning bolt occupies a reserved central gap.

Rain and hail increase from two to four to six marks. Snow increases from one to two to four larger flakes, preserving its distinct shape at small HUD sizes. Fog increases from one to three strokes. Thunder increases bolt breadth. Intensity distinctions and precipitation types therefore do not depend solely on colour.

The 15 active SVG sources contain the paths, colours, and contrasting outlines directly on a shared `0 0 64 64` view box. Each declares `preserveAspectRatio="xMidYMid meet"` and uses explicit fills and rounded strokes, without stylesheet rules, clones, masks, or filters. Edit these source assets directly; no PNG counterpart is required.
