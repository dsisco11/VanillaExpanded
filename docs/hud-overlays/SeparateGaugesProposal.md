# Separate copper HUD gauges

Status: approved design with runtime integration. The selected direction is three independent compact gauges with a shared aged-copper aesthetic. This replaces the combined landscape-clock, rim-thermometer, and calendar design in the [landscape clock proposal](../landscape-clock/LandscapeClockProposal.md). It does not specify an implementation plan.

## Shared appearance and construction

Use dark aged brown copper, restrained warm highlights, subtle patina, dark recesses, and consistent fasteners and typography. Avoid bright orange saturation, pale silver highlights, and fine decorative detail that disappears at HUD size.

Each gauge has its own backing, independently rendered readings, and foreground housing. The foreground includes any clamps and glass overlays that must cover a reading. Author all components against one shared layout per gauge. Use simple apertures and sufficient transparent margins; keep all layers aligned to the same geometry.

Gauge textures should be no larger than 512×512; small marks can use smaller reusable textures. Display dimensions come from the central `HudOverlaySizes` palette, with normal HUD text sizing. Current palette widths are 90 GUI units for the clock, 90 for the radial thermometer, and 96 for the calendar; rectangular instruments preserve their canvas aspect ratios.

## Clock: 24-hour radial dial

The face contains **24 equal angular segments, one per hour**. Time advances clockwise:

| Position | Time |
|---|---|
| Top | 12:00 — noon |
| Right | 18:00 |
| Bottom | 00:00 — midnight |
| Left | 06:00 |

This places the daytime portion toward the top and nighttime toward the bottom. Hour positions stay fixed; seasonal changes alter their phase colors rather than rotating the entire face. A distinct pointer indicates the current estimated time independently of the colored segments. The selected Workshop design has no hour numbers. Any ticks, dividers, and phase colors are drawn at runtime, never baked into its textures.

Segment tints are supplied dynamically, never baked into the artwork. Proposed colors are muted gold for **day**, peach for **dawn**, rust for **dusk**, and blue for **night**. Neutral segment masks, dividers, and the copper housing remain reusable across all dates.

Determine the phases across the represented day using the game's calendar and solar model at the player's location, including the applicable date and sun-angle information. Evaluate the daily solar cycle, not merely the current sun angle. Dawn and dusk must distinguish a rising from a setting sun. The implementation uses normalized `GetSunPosition(position, totalDays).Y`: daylight above the horizon, twilight to −6° solar elevation, and night below −6°. Adjacent samples distinguish rising from setting sun.

The dial must reflect changing phase durations with season and location; it must not assume fixed sunrise/sunset hours or equal day/night halves. Days without a particular phase should not invent a colored interval for it.

**Boundary treatment:** when a phase boundary falls within an hour, blend that segment's tint according to the fraction of the hour occupied by each phase. This preserves 24 segments while representing changes smaller than one hour. Twelve five-minute samples per hour provide the coverage blend.

Retain the existing approximate-time behavior for the pointer. Recalculate the phase distribution when its calendar/location inputs change; prepare this data outside drawing. Missing solar data should retain a valid previous distribution or show an explicitly neutral face, rather than fabricate normal daylight hours.

## Temperature: independent thermometer

Selected form: B, the horseshoe liquid gauge. A circular copper housing contains a thick red-liquid glass tube around its inside edge, with a bulb at the lower-left and a capped tip at the lower-right. Copper neck and tip clamps cover the glass. A normal-sized Celsius reading occupies the center. The instrument uses a 90 × 90 GUI-unit footprint above the calendar.

Retain the environmental range **−40°C to +60°C**, ticks every **5°C**, and labels at **−40, 0, and +60**. Place runtime ticks and separate number textures along the inner scale using the same angular calibration as the fill mask. Use registered empty/full textures with a clockwise angular alpha reveal for the liquid display. Clamp visual fill at the endpoints while keeping the actual numeric reading available outside the range.

## Calendar: independent mechanical counter

A slim rectangular copper housing has two recessed counters for **three-letter month / day**, for example `OCT 14`. Its 512 × 192 canvas renders at 96 × 36 GUI units, with a wider month window and a narrower day window. Characters use normal HUD text sizing.

Read the date from the game calendar. Month abbreviations must correspond to its month names and localization, rather than assuming the example's Gregorian month. Numbers and letters are live content, not baked into the housing texture.

## Review criteria

- The three gauges look like one instrument set while remaining independently readable.
- Clock orientation is noon at the top and midnight at the bottom; phase proportions respond to calendar and solar conditions.
- The thermometer's fill and scale agree, and clamps cover the glass correctly.
- Calendar characters remain readable at the intended HUD size.
- Layers align and remain correctly positioned when their palette dimensions change.

Design A (Workshop) is selected. The independent layer composition is documented in [HUD overlays](HudOverlays.md). Runtime integration is implemented; small-size in-game acceptance remains outstanding.
