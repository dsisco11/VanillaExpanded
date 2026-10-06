# Asset-defined radial-menu tool presentation

Status: proposed; implementation and visual validation are outstanding.

## Purpose

Show a deliberately framed view of each tool's working head in its quick-swap menu wedge. The head should be large, readable, and oriented appropriately for that wedge. Handles and other parts of the model may extend outside the wedge and be clipped intentionally.

Tool assets define this presentation through a `ve-radial-menu-properties` object. The radial-menu presentation is independent of the base game's inventory GUI transform, which is authored for square item slots.

## Asset settings

Use asset patches to add `ve-radial-menu-properties` under the tool item's `attributes`. Read the resolved collectible's `Attributes`, not stack attributes or its inventory GUI transform. Share patches across variants with matching geometry; use the existing asset variant-resolution conventions for different shapes. Preserve unrelated attributes and create a missing attributes parent without replacing existing data.

The object defines a dedicated presentation transform with:

- **Rotation:** model-space orientation in degrees, chosen to expose the useful face of the tool head.
- **Scale:** uniform model scale relative to the radial renderer's reference size.
- **Translation:** model-space placement used to bring the head to the presentation origin.
- **Wedge-relative rotation:** an optional screen-space angle relative to the wedge's outward radial direction, applied after model orientation and projection.

### Schema and transform convention

```json
{
  "ve-radial-menu-properties": {
    "transform": {
      "rotation": { "x": 0, "y": 0, "z": 0 },
      "translation": { "x": 0, "y": 0, "z": 0 },
      "scale": 1
    },
    "wedgeRotationDegrees": 0
  }
}
```

This is a schema example, not tuned artwork. A present empty properties object is valid identity presentation. Missing transform/vector components default to zero rotation/translation and scale one. An absent properties object selects fallback. Accepted fields and numeric conversions follow engine typed deserialization; unknown fields are ignored. Deserialization failures, a null transform, non-finite values, and non-positive scale components select fallback.

Reuse `ModelTransform` with explicitly initialized identity values, default origin `(0.5, 0.5, 0.5)`, positive scale, and default `Rotate = false`. Its `AsMatrix` provides the existing translation/rotation/scale math. The enclosing properties object adds the optional wedge angle, which that engine type does not provide.

Initialize these values with `ModelTransform.ItemDefaultGui()` and populate the engine's `ModelTransformNoDefaults` input type, seeded with those defaults, through `JsonUtil.PopulateObject` typed deserialization. Transfer its decoded transform fields to the owned `ModelTransform`. This avoids `ModelTransform`'s deserialization callback treating an explicitly authored sentinel vector as omitted. Retain only rendering validation around that operation; do not independently decode vector components or implement transform defaults.

Coordinates are normalized uploaded model units, with one unit corresponding to 16 shape-coordinate units for ordinary shape meshes. Model X points right and Y up before authored rotation; positive Z follows the engine model convention. The default pivot is the model-space point `(0.5, 0.5, 0.5)`; engine transform fields may override it. Translation is in model units, after local rotation/scale, independent of GUI scale; authors use it to bring the working head to the anchor. Rotation follows the engine's degree-based X/Y/Z matrix composition.

For column vectors, define the dedicated model matrix as:

```text
A = ModelTransform.AsMatrix
  = T(t) * T(o) * Rx * Ry * Rz * S(assetScale) * T(-o)
M = T(iconX, iconY, depth) * Rz(screenAngle)
    * S(sizePixels, -sizePixels, sizePixels) * T(-o) * A
```

Here `o` is the resolved engine transform pivot and `t` the authored translation. The rightmost operation applies first. `T(-o) * A` simplifies to `T(t) * Rx * Ry * Rz * S(assetScale) * T(-o)`, so the pivot maps to the icon anchor at identity and stays there under rotation when translation is zero. Do not subtract the pivot on the opposite side of `A`.

At scale one, one model unit spans `sizePixels` screen pixels before projection. This is a reference size, not full-model fitting. The menu already supplies effective size including GUI/menu sizing and hover; do not call `GuiElement.scaled` again. Negative Y scale converts model-up to screen-up in the engine's Y-down GUI coordinates. Retain the existing icon depth argument (`100`) and capture depth policy.

If `wedgeRotationDegrees` is omitted, `screenAngle` is zero and the authored view stays screen-fixed. If supplied, `screenAngle` equals the actual wedge center angle plus that value. Angles are clockwise from screen up, matching `RadialMenuLayout`; supplied zero tracks the outward direction. For a center disc without a radial direction, use zero as the reference angle. Wedge rotation acts around the icon anchor on the completed presentation; it is not an extra model-space Z rotation.

Placement is anchored to the wedge's icon position. Menu size and hover supply the overall presentation scale; asset values must not encode fixed screen coordinates. Wedge-relative rotation lets a long tool follow its wedge's direction as the layout changes.

## Rendering behavior

1. Resolve the selected tool's radial-menu properties.
2. Obtain the mesh, textures, and applicable item-specific rendering state through the ordinary engine item-rendering path.
3. For a supported configured draw, replace the inventory GUI presentation transform with the dedicated radial-menu transform. Do not compound it with the inventory GUI transform or the existing category-specific head-centering offset.
4. Render through the existing icon capture, halo, hover, and wedge-stencil pipeline. Cropping outside the wedge is intentional.

### Selected engine boundary and support

Use a narrowly validated Harmony transpiler on the installed `InventoryItemRenderer.RenderItemstackToGui(ItemSlot, double, double, double, float, int, float, bool, bool, bool)` overload. Replace its completed local model matrix after ordinary GUI construction, before temperature/shader preparation. Keep the existing model/model-view uploads and draw path. Overriding only `ItemRenderInfo.Transform` is insufficient: the ordinary construction additionally applies GUI-scaled translation, item pixel offsets, and item/block-specific conventions.

Scope the request to the exact detached slot and intended GUI invocation. Bind it once on entry; nested GUI calls during callbacks cannot inherit it. Store the unoffset wedge anchor separately and continue passing the existing fallback position to the engine. At the matrix boundary, an eligible draw uses `M` with the unoffset anchor; an ineligible draw retains the original matrix and category offset without a second draw or callback retry. Recheck eligibility after callbacks because renderer registration can change there.

Initially support the ordinary inventory mesh path under the engine's orthographic GUI projection, including callbacks replacing the mesh or preparing textures/rendering state. Callback changes to `ItemRenderInfo.Transform` are presentation overrides and are deliberately superseded by configured radial settings; mesh and other preparation remain intact. Registered custom GUI delegates are unsupported even if they appear to consume the supplied matrix. Inspect their registration through a cached accessor to the engine's GUI renderer registry, query it live, and fail closed if access, projection support, or hook validation fails. Do not infer support from a non-null mesh. This does not guarantee compatibility with other mods changing shader/draw behavior outside these boundaries.

Pass `rotate: false` and `showStackSize: false`. Preserve callback-selected mesh, textures, culling, alpha test, color, overlays, temperature/damage effects, lighting submission, depth behavior, and render-state cleanup. Authored orientation naturally changes normal-based shading; preserve the engine's shading pipeline rather than freezing the inventory view's brightness.

Keep any override scoped to the intended radial-menu draw and restore it safely after nested calls or failures. Do not mutate shared collectible GUI transforms or affect ordinary inventory rendering.

## Coverage and fallback

Start with the tool families displayed by quick-swap, tuning their patches against representative models and variants. Non-tool entries, tool-mode artwork, and center labels retain their existing presentation unless separately configured and supported.

Items without valid properties use the current rendering behavior, including its existing category offset. Custom renderers that cannot honor the dedicated transform also fall back. Registration and support for those renderers can be extended when a concrete need is established.

Validate configuration values and reject deserialization failures, non-finite transforms, and non-positive scale components. Also reject settings whose composed `ModelTransform.AsMatrix` contains non-finite components, since individually finite inputs can overflow when combined. The later rendering boundary must validate its final matrix after applying menu size, placement, and wedge rotation. Report invalid configuration with bounded diagnostics rather than repeatedly logging during rendering.

## Scope and ownership

The item asset owns its presentation settings. A focused item-rendering component reads and applies them; the radial-menu renderer supplies wedge placement, size, direction, hover, capture, halo, and clipping. Keep composition roots thin and preserve candidate selection, entry ordering, equipment operations, input, and hit testing.

Use focused files in an item-rendering domain for property resolution, presentation matrix construction, scoped invocation state, and the engine hook/eligibility adapter. `QuickToolItemIcon` owns opt-in and fallback placement; an optional radial-icon context capability conveys wedge direction without changing unrelated `IRadialMenuIcon` implementations. The mod composition root installs/disposes the adapter and performs no transform or patch-selection policy.

Automatic bounds calculation, retained mesh geometry, containment searches, and automatic rotation or shrinking are outside this design. The acceptance criterion is readable tool-head framing, not full-model visibility.

## Validation

- Verify that configured draws use the dedicated transform independently of inventory GUI settings, and that ordinary inventory rendering is unaffected.
- Verify fallback for missing/invalid properties and unsupported renderers, plus exception-safe cleanup and single callback execution.
- Obtain user-run visual checks across representative tool heads, model/material variants, menu sizes, GUI scales, wedge positions, and hover states. Confirm readable heads, intentional handle cropping, suitable orientation, halo appearance, and stable placement.
- Run build and test tools through subagents under repository instructions. Source and headless checks do not establish visual acceptance; no game launch is authorized by this proposal.

## Evidence and remaining validation

The contracts above were established on 2026-10-06 from current repository source, installed assets, and Mono.Cecil inspection of `G:/Vintagestory/VintagestoryLib.dll`, assembly version `1.22.7.0`, SHA256 `E08F22B493B92FEAF0AAEB79D22437EA0F7EFC38AA7F72A04A47F98BC0E40DF0`.

| Decision | Evidence |
| --- | --- |
| Attribute location and patch conventions | [CollectibleObject.Attributes](../../../vsapi/Common/Collectible/Collectible.cs); [JsonObject indexer/AsObject](../../../vsapi/Datastructures/JsonObject.cs); installed [pickaxe attributes](G:/Vintagestory/assets/survival/itemtypes/tool/pickaxe.json); repository [healing-item patches](../../VanillaExpanded/assets/vanillaexpanded/patches/healingitems.json) and installed survival attribute patches. |
| Reused transform math | [ModelTransform](../../../vsapi/Common/Collectible/ModelTransform.cs): `AsMatrix`, `ItemDefaultGui`, `EnsureDefaultValues`, and degree rotations. Explicit identity values avoid sentinel/default ambiguity. |
| Single preparation and matrix hook | Installed `InventoryItemRenderer`: GUI method calls `GetItemStackRenderInfo` at `IL_0011`; preparation calls slot/collectible callbacks at `IL_0352`/`IL_0367`; local model matrix construction ends at `IL_0238`, before preparation at `IL_0239`. Offsets identify this inspected version, not a portable hook signature. Match semantic instruction structure during installation and reject incompatible layouts. |
| Draw/shading/eligibility | Model/model-view submissions at GUI `IL_03EC`/`IL_041C`; custom registry lookup at `IL_0439`–`IL_0459`, delegate call `IL_0472`, ordinary mesh draw `IL_04E1`. `ClientEventAPI.itemStackRenderersByTarget` indexes collectible class, GUI target, and ID. Installed `assets/game/shaders/gui.vsh` transforms positions with model-view and normals with model matrix. |
| Menu size, direction, capture and fallback | [RadialMenuLayout](../../VanillaExpanded/src/RadialMenu/RadialMenuLayout.cs), [RadialMenuRenderer](../../VanillaExpanded/src/RadialMenu/RadialMenuRenderer.cs), [RadialMenuIconHalo](../../VanillaExpanded/src/RadialMenu/RadialMenuIconHalo.cs), and [QuickToolItemIcon](../../VanillaExpanded/src/QuickTools/QuickToolItemIcon.cs); angles are clockwise from up, hover scales size/placement once, capture owns depth, fallback has a category offset. |

These are source/assembly findings and design decisions, not an executed hook or visual acceptance. Initial asset coverage and shape-specific authoring groups are recorded in [ToolHeadPresentation.todo](ToolHeadPresentation.todo#initial-asset-coverage). Numeric transforms, hook verification, and renderer tests remain implementation work.
