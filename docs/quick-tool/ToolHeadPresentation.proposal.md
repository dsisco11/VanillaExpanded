# Asset-defined radial-menu tool presentation

Status: proposed; implementation and visual validation are outstanding.

## Purpose

Show a deliberately framed view of each tool's working head in its quick-swap menu wedge. The head should be large, readable, and oriented appropriately for that wedge. Handles and other parts of the model may extend outside the wedge and be clipped intentionally.

Tool assets define this presentation through a `ve-radial-menu-properties` object. The radial-menu presentation is independent of the base game's inventory GUI transform, which is authored for square item slots.

## Asset settings

Use asset patches to add `ve-radial-menu-properties` under the tool item's `attributes`, subject to confirming the existing asset-loading conventions during implementation. Share patches across tool variants where their geometry permits it; use individual overrides for different models.

The object defines a dedicated presentation transform with:

- **Rotation:** model-space orientation in degrees, chosen to expose the useful face of the tool head.
- **Scale:** uniform model scale relative to the radial renderer's reference size.
- **Translation:** model-space placement used to bring the head to the presentation origin.
- **Wedge-relative rotation:** an optional screen-space angle relative to the wedge's outward radial direction, applied after model orientation and projection.

Use the engine's existing transform data model where its contract is suitable. Final field names, coordinate conventions, transform order, and reference size must be documented before authoring patches. Defaults must be independent of the item's inventory GUI transform. Translation, rotation, and scale must have a consistent pivot so authors can position a head without compensating for unrelated item-slot settings.

Placement is anchored to the wedge's icon position. Menu size and hover supply the overall presentation scale; asset values must not encode fixed screen coordinates. Wedge-relative rotation lets a long tool follow its wedge's direction as the layout changes.

## Rendering behavior

1. Resolve the selected tool's radial-menu properties.
2. Obtain the mesh, textures, and applicable item-specific rendering state through the ordinary engine item-rendering path.
3. For a supported configured draw, replace the inventory GUI presentation transform with the dedicated radial-menu transform. Do not compound it with the inventory GUI transform or the existing category-specific head-centering offset.
4. Render through the existing icon capture, halo, hover, and wedge-stencil pipeline. Cropping outside the wedge is intentional.

The integration must distinguish item-specific mesh preparation from the inventory presentation transform. Inspect the engine's draw boundary before choosing an adapter; do not assume that the public GUI rendering call exposes the required override. Preserve lighting, depth behavior, and render-state cleanup, and avoid repeating item callbacks for measurement or rendering setup.

Keep any override scoped to the intended radial-menu draw and restore it safely after nested calls or failures. Do not mutate shared collectible GUI transforms or affect ordinary inventory rendering.

## Coverage and fallback

Start with the tool families displayed by quick-swap, tuning their patches against representative models and variants. Non-tool entries, tool-mode artwork, and center labels retain their existing presentation unless separately configured and supported.

Items without valid properties use the current rendering behavior, including its existing category offset. Custom renderers that cannot honor the dedicated transform also fall back. Registration and support for those renderers can be extended when a concrete need is established.

Validate configuration values and reject malformed or non-finite transforms and non-positive scales. Report invalid configuration with bounded diagnostics rather than repeatedly logging during rendering.

## Scope and ownership

The item asset owns its presentation settings. A focused item-rendering component reads and applies them; the radial-menu renderer supplies wedge placement, size, direction, hover, capture, halo, and clipping. Keep composition roots thin and preserve candidate selection, entry ordering, equipment operations, input, and hit testing.

Automatic bounds calculation, retained mesh geometry, containment searches, and automatic rotation or shrinking are outside this design. The acceptance criterion is readable tool-head framing, not full-model visibility.

## Validation

- Verify that configured draws use the dedicated transform independently of inventory GUI settings, and that ordinary inventory rendering is unaffected.
- Verify fallback for missing/invalid properties and unsupported renderers, plus exception-safe cleanup and single callback execution.
- Obtain user-run visual checks across representative tool heads, model/material variants, menu sizes, GUI scales, wedge positions, and hover states. Confirm readable heads, intentional handle cropping, suitable orientation, halo appearance, and stable placement.
- Run build and test tools through subagents under repository instructions. Source and headless checks do not establish visual acceptance; no game launch is authorized by this proposal.

## Decisions to resolve before implementation

- Confirm the asset property location and reusable engine transform type.
- Define exact transform units, pivot, order, reference scale, and wedge-relative angle convention.
- Identify a rendering integration point that replaces GUI presentation while retaining applicable mesh preparation and shading behavior.
- Establish initial tool-family patch coverage and explicit custom-renderer support boundaries.
