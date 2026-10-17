# Presentation tests

Test behavior and relationships rather than the chosen visual design.

- Do not assert specific palette colors, opacity values, font sizes, artwork pixels, or fixed presentation bounds and dimensions. Referring to a production style constant instead of a literal does not make a visual expectation appropriate.
- Verify input routing, visibility and applicability, persistence, draw order, resource ownership, caching, and cleanup.
- Verify layout through containment, alignment, non-overlap, scaling, and forwarding of caller-supplied geometry or texture dimensions.
- Verify animation through continuity, relative progression, reversal, and lifecycle callbacks rather than prescribed brightness values.
- Synthetic test inputs may include coordinates, sizes, and colors when testing calculations or data forwarding. Derive expectations from those inputs rather than duplicating the current appearance.

In-game review establishes visual appearance; headless tests establish the behavioral contracts they exercise.
