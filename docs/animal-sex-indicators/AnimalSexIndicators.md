# Animal sex indicators

Living creatures with synchronized generation 1 or greater and a recognized male or female gender variant receive a small symbol above their selection box. Players, dead creatures, generation-zero creatures, and creatures with unknown gender receive no symbol. Adults and babies use the same eligibility rules.

The client draws camera-facing symbols in world space with a dedicated icon shader and terrain depth testing. The fragment shader samples the symbol texture, applies the configured tint, and discards transparent pixels. Walls and terrain hide the covered parts of a symbol. Symbols render at half the configured base size, giving a default width and height of 0.125 blocks. They retain 75% opacity within the nearer half of the viewing range and fade linearly to zero over the farther half. At the default range this fade runs from 4 to 8 blocks. Male symbols are blue and female symbols are pink by default; both include a dark outline.

ConfigLib groups the enable toggle, base size, viewing distance, opacity, and packed RGB colors under **Animal Sex Indicators**. Changes take effect while playing. The defaults are enabled, 0.25 blocks of base size (0.125 rendered), 8 blocks of viewing distance, and 75% opacity. Supported base size is 0.05–1 block, distance is 1–8 blocks, and opacity is 0–1. Existing saved size settings also render at half scale, and saved viewing distances above 8 are clamped to 8.

The configuration file also exposes `AnimalMaleIconColor` and `AnimalFemaleIconColor` as packed RGB integers. Defaults are 5089023 (hex 4DA6FF) and 16741813 (hex FF75B5).

The male and female symbols are static 64×64 PNG assets in `assets/vanillaexpanded/textures/animal-sex-indicators/`. Their white symbols retain configurable tinting and dark outlines. The engine texture cache owns the shared textures; the renderer creates one quad when entering a world and releases it when leaving. Animal breeding data stays authoritative on the server; this feature does not change it or send additional packets.
