# Cairo and presentation assertion inventory

This is the decision record for [cleanup Phase 1](CairoTestCleanup.todo).
It records work for later phases; it does not authorize or claim test removal.
Sections 1-6 retain the historical Phase 1 inventory. Section 7 records the
subsequent Phase 4 native-dependency decision against the retained tests.

## Policy and interpretation

The user confirmed on 2026-11-04:

- No exceptions: remove all pixel-output and presentation-only assertions.
- This includes whole-buffer non-modification checks.
- Functional geometry assertions are allowed when tied to hit testing, clipping,
  viewport containment, reachability, and scroll limits.

Do not substitute equivalent presentation assertions on constants, drawing
commands, decorative coordinates, shader flags, or private implementation details.
Caller-supplied values, content identity, configuration serialization, numerical
validity, and resource ownership are not presentation baselines merely because
their types contain colors, positions, matrices, or dimensions.

Use the following dispositions:

- **P / remove:** presentation-only; no appearance-equivalent replacement.
- **M / split:** remove presentation assertions; preserve the specified behavior.
- **F / retain:** functional contract; decouple unnecessary Cairo setup separately.

For M and F entries, the named test method owns the retained coverage unless a
different boundary is explicitly named. For P entries there is no behavioral
coverage obligation unless another existing owner is identified. Method entries
apply to all their theory rows. Line locations describe the pre-cleanup sources
and are navigation aids, not an assertion that those locations will remain fixed.

## Inventory scope and evidence

The static sweep covered all 256 C# files in `VanillaExpanded.Tests`: 169 Unit,
31 Component, 16 Integration, 21 shared TestSupport, 11 Mocks, four QuickTools,
three RadialMenu, and one root file. Source attribute counts were 939 facts,
440 theories, and 1,744 inline-data rows; these are not discovered test counts.
Generated output and other projects were excluded from those counts.

Searches included pixel/surface/context/buffer access, RGB/RGBA/alpha, bitmap and
GPU readback, palette/font/style assertions, dimensions and coordinates, drawing
commands, shader uniforms, SVG draws, captured mock invocations, and
font/composition/texture entry points. Shared helpers were traced to callers,
including `GuiTestPixels`, `Raster`, `States`, `AssertHints`, `AssertPopupDepth`,
`AssertEmptyStateCentered`, and `AssertBookTabPosition`. Production and game
sources were consulted read-only where needed to interpret a contract.

The earlier 18-method / 87-case Cairo pixel inventory is included below, but is
not the limit of this sweep. No additional live bitmap/GPU pixel-output assertion
was identified in this project. Mock atlas color samples and numerical simulation
buffers are not actual rendered-image comparisons.

This is static evidence only: no tests, builds, discovery checks, or native-free
execution were performed. Later phases must verify actual runtime independence.

## 1. GUI drawing, controls, and tabs

### [GuiControlVisualsTests](../VanillaExpanded.Tests/Unit/Gui/GuiControlVisualsTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `PaletteMatchesApprovedReferenceAndReturnsIndependentColors`; 43-52, data 16-38 | M: remove approved palette values and role-count bookkeeping. Preserve independent returned arrays and generic supplied-value conversion/argument contracts at `GuiControlPalette.Color`. |
| `FrameHasApprovedFillAndGuiScaledWholePixelOutline`; 61-72 | M: remove fill/outline/stroke pixels. Preserve caller context-state restoration, including `LineWidth`, at the frame state-ownership boundary. |
| `GradientsAreLocalAndInvalidPixelGeometryFailsExplicitly`; 76-91 | M: remove gradient pixels. Preserve invalid units/scale, opacity, rectangle, and unusable input-bound rejection at drawing/color/input-bounds boundaries, without requiring raster setup for unrelated validation. |

[RecipeBookVisualMetricsTests](../VanillaExpanded.Tests/Unit/RecipeBook/RecipeBookVisualMetricsTests.cs):
`MetricsSeparateGenericControlsFromRecipeBookComposition` (17-54) is P: remove
font-size, opacity, decorative metrics, and text-plus-inset baselines. Functional
text fit remains owned by composed-control containment tests below.

[GuiStyledScrollbarValidationTests](../VanillaExpanded.Tests/Unit/Gui/GuiStyledScrollbarValidationTests.cs):
`ScrollbarRejectsNonPositiveOrNonFiniteCallerSteps` (26-33) is F: preserve
rejection, parameter identity, and no allocation. Its invalid-constructor path
has no demonstrated native Cairo entry.

### [GuiElementIconTabsTests](../VanillaExpanded.Tests/Component/Gui/GuiElementIconTabsTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `FolderTabRendersDistinctBackgroundsAndFixedPixelAlignedNearestFilteredIcons`; 27-103 | M: remove icon colors/sizes/snapping, depth/filter policy, state-texture identity/cardinality, and draw decomposition. Preserve resource reuse/release through the tab texture owner. |
| `FolderTabRasterHasRoundedShouldersAndOpensIntoTheMatchingPanel`; 111-182 | P: remove raster shoulders, fills, outlines, accent, and panel-edge matching. |
| `SelectedTabStaysJoinedToHostPanelAcrossFractionalMovementAndScrolling`; 191-230 | M: remove seams, border alignment, snapping, and background count. Preserve recomposition resource stability and selected-tab containment inside actual scissor bounds. |
| `NativeScissorRetainsEverySelectedBorderAcrossFractionalMovementAndScrolling`; 246-295 | M: remove hardcoded arrow reservation and independent rounding/dimension goldens. Preserve selected-item containment and balanced clipping against the actual viewport. |
| `ClippedTabsRejectPointerActivationAndKeyboardWrapRestoresAccessAfterMovement`; 307-397 | F: clipped click rejection, keyboard wrap, focus loss, and selected-target reachability. |
| `MeasuredWidthShowsEveryTabWithoutScrollingOrArrows`; 409-439 | M: remove width/gap formula and decorative edge placement. Preserve all tabs fitting, appropriate overflow controls, scissor containment, and invalid-count rejection. |
| `SelectionKeyboardWrapAndWheelScrollingUseNativeBoundsAndClipHitTesting`; 449-501 | F: selection, notification suppression, keyboard/wheel input, clipped routing, disabled behavior, and reachability. |
| `PointerAndKeyboardHintsUseTabNamesAndAreOutsideTheClipAndDismissOffThePanel`; 506-519 | F: content/visibility, focus dismissal, and host clipping restoration. |
| `NativeCompositionRestoresTheHostClipWhenRenderingFails`; 524-539 | F: exception-path clip restoration. |
| `IconClickSelectsAndInvalidSelectionsAndDescriptionsFailExplicitly`; 543-562 | F: activation, callbacks, invalid input rejection, and preserved state. |

### [GuiStyledControlStateRenderingTests](../VanillaExpanded.Tests/Component/Gui/GuiStyledControlStateRenderingTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `PopupSelectionPresenceInvalidatesCachedIndicator`; 25-43 | M: remove indicator-color searches. Preserve selection transitions and stable resources on unchanged rendering at dropdown selection/resource boundaries; upload equality alone is insufficient selection coverage. |
| `ScrollbarPointerFeedbackTracksCaptureAndCancelsWithoutExtraScrollOrUploads`; 53-185 | M: remove hover/pressed texture identity, RGB, disabled alpha, and style draw counts. Preserve stepping/callbacks, drag/release-outside, focus/range-collapse cancellation, limits, reuse, and disposal. |

### [GuiStyledControlsNativeCompatibilityTests](../VanillaExpanded.Tests/Integration/Gui/GuiStyledControlsNativeCompatibilityTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `ButtonPreservesNativeActivationAndRendersApprovedStates`; 26-86 | M: remove colors/text alpha. Preserve mouse/keyboard activation, disabled/hidden behavior, cancellation, and ownership. |
| `ClearableInputUsesCallerLabelsAndNotifiesExactlyOnceWithNativeGeometry`; 95-160 | M: remove pixels/opacity, decorative inset, and selection-render coordinate golden. Preserve caller-font non-mutation, editing/caret/focus, exactly-once clear, and disjoint usable hit regions. |
| `DropdownCachesStyledResourcesAcrossNativeOpeningReplacementAndSelection`; 169-242 | M: remove colors, indicator pixels, alpha, buffer inequality, fixed heights, and decorative value-text dimensions. Preserve open/replace/select/callback behavior, reuse, and disposal. |
| `PopupLongLabelsKeepNativeWheelScrollingAndHitCoordinates`; 249-275 | M: remove copied decorative width allowance. Preserve label fit, wheel scrolling, keyboard End/Enter selection, and clipping restoration. |
| `SharedControlsKeepNativeComposerTabRoutingAndSingleFocusOutline`; 280-300 | M: remove rendered-outline cardinality. Preserve native tab routing/current focus. |
| `CheckboxHasFlatScaledFrameNativeTogglingAndDisabledIndicator`; 309-336 | M: remove fixed frame size, colors, and alpha. Preserve toggling, callbacks, disabled behavior, and disposal. |
| `NativeHelpersRemainOptInAndInvalidDropdownsFailExplicitly`; 340-356 | F: helper opt-in/type compatibility and invalid-list rejection. |
| `CheckboxLabelUsesOneNativeToggleAndFocusTargetAndRespectsDisabledAndClippedInput`; 365-413 | M: remove fixed checkbox size. Preserve label hit target, single toggle/focus, outside/disabled/clipped rejection, and keyboard activation. |
| `ScrollbarArrowsAndInsetThumbShareNativeRangeAndReleaseDragOutside`; 426-565 | M: remove colors/alpha/corner pixels, decorative widths, and style draw counts. Preserve native range/wheel equivalence, stepping, drag mapping, limits, release-outside, collapse, and ownership. |
| `ArrowButtonsUseNativeMomentaryActivationWithoutExtraFocusOrReleaseCallbacks`; 591-648 | M: remove hover color and idle/hover texture comparisons. Preserve momentary activation, focus, cancellation, callbacks, and release. |

### [GuiControlNativeProbeTests](../VanillaExpanded.Tests/Integration/Gui/GuiControlNativeProbeTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `BorderlessButtonRetainsNativeActivationAndVisibilityWithLocalFlatChrome`; 21-69 | M: remove painted/untouched sample comparisons, including outside pixels. Preserve activation, disabled behavior, and hidden rendering suppression. |
| `InputChromeAndCallerPlaceholderUseEightUnitInsetWithoutEngineFieldAccess`; 79-117 | M: remove RGB, padding/inset, and exact selection coordinates. Preserve placeholder shown/hidden behavior, editing/caret, notifications, and distinct editable/clear hit regions. |
| `PopupRestylesAfterNativeOpenSelectionAndListReplacementWithoutChangingRowGeometry`; 121-153 | M: remove fill samples/fixed row height. Preserve pointer selection, opening/replacement, keyboard selection, callbacks, and clipping cleanup. |
| `NativeComposerRetainsSingleStripFocusOutlineAndTabRouting`; 158-176 | M: remove outline count; preserve forward/reverse tab routing. |
| `TitleChromeCanBeOverpaintedLocallyWhileNativeDragPositionAndCloseRemainAuthoritative`; 180-208 | M: remove title-gradient samples. Preserve drag deltas, saved position, native movement mode, and close. |

[GuiElementIconTabsComposerIntegrationTests](../VanillaExpanded.Tests/Integration/Gui/GuiElementIconTabsComposerIntegrationTests.cs):
`NativeComposerOwnsIconOnlyButtonsAndBoundsAndRecompositionDisposesTextures`
(26-60) is M: remove fixed dimensions/gaps and decorative bottom alignment;
preserve composer/bounds ownership, recomposition stability, child cleanup, and
texture disposal.

## 2. Recipe Book shell, options, tiles, and tooltips

### [RecipeBookDialogLayoutTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookDialogLayoutTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `RecipeListInsetFramesViewportAndScrollbarWithoutOverlappingAdjacentControls`; 34-94 | M: remove decorative borders/insets/alignment and initial offset. Preserve viewport/scrollbar containment, non-overlapping reachable controls, partial-row clipping, scroll state, and balanced scissors. |
| `ResponsiveDialogRecompositionRestoresFocusedRecipeAndRowAnchor`; 108-145 | M: remove default-pitch/remainder goldens; use actual geometry. Preserve selection/focus/inspection, row anchor, and keyboard-focus restoration. |
| `ScaleTransitionCapturesTheOldCompositionRowBeforeRestoringAtTheNewScale`; 153-181 | M: remove default-pitch-derived remainder. Preserve old-composition anchor/inspection and caller-requested scroll continuity. |
| `BookToggleStaysLeftOfOutputSlotWhenParentAndOutputMoveResizeAndRecompose`; 190-222 | M: remove fixed gap/centering. Preserve opener reachability, moved hit region, non-overlap with inputs, hint coverage, and reattachment activation. |
| `ComposedControlsStaySeparatedAtMinimumSizeAndAfterResize`; 235-409 | M: remove copied minimum-size/style formula, preferred dimensions, fonts/weights, centering/alignment, and decorative gaps. Preserve long-label fit, containment/non-overlap, title-control reachability, usable recipe area, disabled actions, retained search, and resize. |
| `CategoryPanelShowsAllTabsAndCurrentHeadingAcrossSelectionResizeAndReopen`; 418-472 | M: remove panel padding/heading positioning. Preserve reachable/contained tabs, heading content, selection, resize/reopen restoration, and movement mode. |
| `BaselineShellAdoptsSharedControlsAndCentersTheRealMultiDigitCounter`; 484-582 | M: remove shell pixels, fonts, size golden, decorative padding/centering, checkbox draw dimensions/counts, and style-only type checks. Preserve control wiring, default sort, tab order, alternative-count content/fit, non-overlap, and options interaction. |

### Additional shell and options decisions

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| [RecipeBookDialogFeedbackTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookDialogFeedbackTests.cs): `EmptyFeedbackClearsOnlyTheRelevantFilterAndTracksLiveInventory`; 107-190 | M: remove indirect centering/spacing. Preserve diagnosis content, relevant-filter clearing, keyboard/mouse actions, live inventory, and scroll reset. |
| Same: `HandbookHintAppearsOverEnabledAndDisabledButtonAndFollowsResize`; 263-298 | F: actual hint hit-region agreement, enabled/disabled hover, resize. |
| Same: `CraftableHintAppearsOverCheckboxAndLabelAndHidesOutside`; 306-331 | F: checkbox/label hit coverage and dismissal. |
| Same: `SortHintRetainsLocalizedLabelOverDropdownAndHidesOutside`; 335-352 | F: localized content and hover routing. |
| Same: `AlternativeCounterHasNoMarkerWhenRecipeIsManuallySelected`; 356-380 | M: remove no-decorative-marker contract. Preserve explicit/automatic selection transitions and accurate alternative-count content at session/counter boundaries. |
| [RecipeBookOptionsTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookOptionsTests.cs): `RecipeBookOptionsSvgStatesUseIsolatedTexturesWithoutModifyingTheSharedDialogSurface`; 34-81 | M: remove whole-buffer equality, SVG colors/sizes/snapping/filter policy, six-upload golden. Preserve separate resource ownership, reuse, and disposal at options texture-owner boundary. |
| Same: `RecipeBookOptionsControlRestorationWithoutClearingBrowsingState`; 88-128 | M: remove decorative popup offset/alignment. Preserve focus, preference updates, escape/parent-close behavior, restoration/search, cleanup, and actual viewport containment. |
| [RecipeBookDialogCleanupTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookDialogCleanupTests.cs): `NativeTilesFitAndClipVisibleIconsAndDisposeTheirTexturesAndSubscriptions`; 33-112 | M: remove fixed tile size/preview-size/bake-scale goldens. Preserve clipping, shader restoration, request bounds, reuse, callback detachment, and release. |
| [RecipeBookLayoutLifecycleTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookLayoutLifecycleTests.cs): `FixedParentTranslationRetainsComposerAndRecalculatesOnlyItsRoot`; 137-174 | M: remove fixed docking gap. Preserve translation, focus/view continuity, composer reuse, and bounded recalculation. |

Other `RecipeBookOptionsTests` methods retain configured-size persistence,
immediate application, row anchors, invalid-input rollback/localized errors,
wheel routing, popup keyboard/focus behavior, and reopen persistence. Numeric
sizes chosen by the test/user are inputs, not approved decorative defaults.

### [RecipeBookDialogNativeInputTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookDialogNativeInputTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `NativeMovementMenuAllowsDraggingAndFixedResumesInventoryFollowing`; 32-100 | M: remove initial docking-gap golden. Preserve drag deltas, saved position, mode changes, following, and close/reopen. |
| `ScrollbarArrowsAndDragKeepNativeThumbAndRecipeViewportSynchronized`; 151-207 | M: remove four-draw decomposition/default row-step constants. Preserve stepping using actual geometry, synchronization, drag mapping, limits, and release. |
| `InlineSearchClearUsesNativeEditingAndPreservesOtherCriteriaAcrossResizeAndReopen`; 222-336 | M: remove decorative clearance/centering. Preserve usable disjoint hit regions, clipping before clear target, editing/clear, criteria, focus, restoration, and resources. |
| `BookButtonUsesSvgAndNativeMouseAndKeyboardActivation`; 462-506 | M: remove artwork identity/viewBox, icon size/tint/dimensions, state draw counts, and decorative placement. Preserve content/hints, hit reachability, toggling, focus, visibility, and cancellation. |
| `CategoryIconsComposeViaNativeSvgApiAndRegistrationAndTexturesFollowMenuLifetime`; 512-554 | M: remove state cardinality, art dimensions/tint/coordinates. Preserve registration ownership, replacement protection, menu lifetime, and cleanup. |

Other methods in this suite (starting at 107, 347, 562, 658, 728, 759, 787, 849,
926, 947, and 999) retain position restoration/clamping, category/filter/sort
actions, focus/tab order, integer editing, hotkey routing, and first-character
handling in their existing methods.

### Tile geometry and interaction

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| [RecipeBookTileGeometryTests](../VanillaExpanded.Tests/Unit/RecipeBook/RecipeBookTileGeometryTests.cs): `MaximumFittingColumnsAreCenteredAndEdgesAreSnappedWithoutAccumulatedDrift`; 28-59 | M: remove centering/default dimensions/gaps/padding/snapping goldens. Preserve maximum fitting columns, containment/non-overlap, and scrollable content extent. |
| Same: `ConfiguredTileSizeDrivesLayoutAndPreviewSizing`; 74-97 | M: remove default size/offsets and preview-resolution styling formula. Preserve honoring user-selected size, fitting columns, usable bounds, and content extent. |
| Same: `AViewportThatCannotFitOnePaddedTileFailsExplicitly`; 67-68; `TileSizeOutsideTheSupportedRangeFailsExplicitly`; 105-106 | F: invalid viewport/configured-size rejection. |
| [RecipeBookTilesGeometryTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookTilesGeometryTests.cs): `TopPaddingRejectsClicksAndNavigationAndScrollKeepFirstAndLastRecipesAccessible`; 30-86 | M: remove exact padding golden. Preserve padding click rejection, actual-content limits, first/last keyboard reachability. |
| Same: `ResizeRestoresRecipeRowAndInspectionAndDoesNotCarryAHeldPress`; 101-165 | M: remove outline shape/count/depth and default pitch. Preserve row/focus/inspection restoration, held-press cancellation, and scroll clamping. |
| Same: `FullHeightPartialRowsUseTheSameSnappedBoundsForIconsScissorsAndHitTesting`; 188-225 | F: agreement of actual hit/icon/scissor regions and partial-row clipping. |
| [RecipeBookTilesInteractionTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookTilesInteractionTests.cs): `NativeContainerRecyclesBoundedButtonsAndTabsAcrossVirtualRowsInBothDirections`; 204-236 | M: remove default-size assumption in focus containment limit. Preserve bounded recycling, focus/selection identity, and reachability. |

Other tile-interaction methods (30, 107, 159, 269) retain stale-press rejection,
live membership, offset clamping, overscan hit rejection, and scroll cancellation.

### [RecipeBookIconRendererTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookIconRendererTests.cs)

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `IconPreviewRequestsTrackTheConfiguredTileSize`; 26-44 | M: remove preview-resolution/style-gap formulas. Preserve configured-size handling and request/cache size distinctions. |
| `MixedStatesShareOneShaderActivationAndDoNotRebakeOnStatusChanges`; 51-98 | M: remove appearance-state sequences and decorative uniforms. Preserve resource sharing, atlas metadata, host-state restoration, no status-only rebake. Availability remains owned by session/evaluation tests. |
| `IngredientAvailabilityTransitionsUpdateBordersWithoutRebaking`; 105-136 | M: remove border/state command expectations. Preserve allocated quantities and no rebake/reallocation as inventory changes. |
| `AlternativeRecipesShareTheSameIconWhileTheirQuantityLabelsRemainIndependent`; 141-171 | F: quantity content, preview sharing, source non-mutation, replacement ownership. |
| `OnlyOutputQuantitiesAreDrawnForRecipesWithMultipleAlternatives`; 178-198 | M: remove fixed depth. Preserve label eligibility/content and allocation/disposal semantics. |
| `QuantityRenderingFailureRestoresHostShaderAndScissors`; 202-221 | F: failure-path restoration; fixed depth used as a failure locator is not a style contract. |
| `AtlasBakingAndPartialRowsCannotEscapeTheViewportOrTileClips`; 227-298 | F: nested clipping/containment. |
| `RenderingFailureRestoresHostShaderAndScissors`; 302 onward | F: failure restoration. |
| `TileIconAndScissorBoundsMatchNativeButtonPixelsAtFractionalPositions`; 325-358 | F: shared-region agreement/movement; no actual pixel assertion. |
| `ShaderCompilationFailureIsReportedAndReleasesTheFailedProgram`; 362-379 | F: failure reporting and partial cleanup. |
| `AvailabilitySelectionNativeFocusAndQuantitiesRemainIndependent`; 386-452 | M: remove appearance flags, inset, focus-outline shape/count/depth, label offsets. Preserve selection/focus identity, quantities, no rebake, balanced clipping at session/tile/resource boundaries. |

[RecipeBookIconCacheTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookIconCacheTests.cs):

- M: `BakingIsLimitedPerFrameAndPendingTilesStillRenderTheirStates` (99-128):
  remove appearance-state sequences/draw cardinality; preserve request budget,
  pending/ready publication, and allocations.
- M: `GuiScaleChangesRetainBothSizesAndReturningToTheOldScaleDoesNotRebake`
  (190-207): remove prescribed preview resolutions; preserve distinct size-key
  caching and reuse.
- F: other methods retain cache reuse, late completions, reload generations,
  foreign-allocation protection, failure/retry, queue-only lookup, shared budgets,
  stage scheduling, and disposal. Pending/ready resource publication is functional.

### Tooltip decisions

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| [RecipeBookTooltipCompositionTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookTooltipCompositionTests.cs): `IngredientSummaryWrapsBelowNativeGridAndRefreshesOnlyWhenCoverageChanges`; 35-123 | M: remove font colors/size hierarchy. Preserve ingredient content/counts, literal text safety, wrapping/containment/non-overlap, refresh identity, capped coverage, and cleanup. |
| Same: `UnresolvedRecipeTooltipShowsItsWarningWithoutConstructingAnInvalidNativeGrid`; 127-148 | F: warning content and refusal of invalid native grid. |
| Same: `RecipeTooltipOmitsEmptyDescriptionAndDividerWithoutLosingPreview`; 154-181 | M: remove decorative divider contract; preserve empty-description omission and valid preview. |
| [RecipeBookNativeTooltipIntegrationTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookNativeTooltipIntegrationTests.cs): `RecipeTooltipUsesNativeSelectedGridWithTitleAndReleasesReplacedPreviews`; 32-135 | M: remove pixels/RGBA, fonts/weights/centering, decorative background/divider shape/alignment. Preserve recipe/title content, grid choice, containment/non-overlap, replacement, cleanup. |
| Same: `RecipeTooltipCentersWrappedTitlesAndVisibleGridCellsInNarrowWindows`; 142-168 | M: remove centering/orientation/fixed grid formula. Preserve wrapping, readable containment, title/grid non-overlap. |
| Same: `RecipeTooltipShowsNativeStatsAndFormattedBehaviorDetailsAboveDividerAndGrid`; 178-256 | M: remove font colors/alignment, padding, panel/divider dimensions. Preserve stats/text, markup handling, wrapping/containment, preview cloning/non-mutation, transition suppression, caching, disposal. |
| Same: `NativeTooltipGridResolvesAllowedWildcardIngredientChoices`; 260 onward | F: native allowed/skip-variant resolution. |
| [RecipeBookNativePopupLayeringTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookNativePopupLayeringTests.cs): `NativeDropdownPopupRendersAboveOutputQuantityLabels`; 22-28 | F: relative occlusion/clipping protect popup usability. Do not retain an absolute-depth style baseline. |

## 3. Other project-wide presentation findings

### Layout, radial menus, and tool-head rendering

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| [RecipeBookCategoryIconsTests](../VanillaExpanded.Tests/Unit/RecipeBook/RecipeBookCategoryIconsTests.cs): `CategoriesResolveExactIconsAndModCategoriesUseTheTag`; 24-26 | P: decorative icon assignment. Semantic category selection remains covered by category projection and native category-input tests. |
| [RecipeBookLayoutTests](../VanillaExpanded.Tests/Unit/RecipeBook/RecipeBookLayoutTests.cs): `LayoutChangesSidesAndReportsImpossibleViewportRatherThanCoveringGrid`; 55-80 | M: remove fixed docking gaps; preserve side choice, translation, non-overlap, and impossible-placement refusal. |
| [RecipeBookLayoutCacheTests](../VanillaExpanded.Tests/Unit/RecipeBook/RecipeBookLayoutCacheTests.cs): `EqualFixedInputsReuseTheDockingResultAndMaterializeIndependentBounds`; 22-53 | M: remove fixed gap; preserve cached-result equality, independent bounds, and reuse. |
| [AlloyCalculatorDialogLayoutTests](../VanillaExpanded.Tests/Unit/AlloyCalculator/AlloyCalculatorDialogLayoutTests.cs): `OptionalSections_CollapseEmptySlotsAndUseMeasuredStatusHeight`; 31-42 | P: decorative section reductions/heights without hit/containment/input outcomes. |
| Same: `Resizing_MaintainsTopAndMovesBottom`; 51-85 | P: decorative anchoring without reachability/containment outcomes. |
| Same: `ImmersiveAlignment_FollowsFirepitScreenTopDespiteHeightAndAnchorChanges`; 91-121 | P: decorative top alignment only. |
| [RadialMenuTests](../VanillaExpanded.Tests/RadialMenu/RadialMenuTests.cs): `ThirtyFourAvailableWedgesKeepTheirOwnCenters`; 172-181 | M: remove tessellation-count growth; preserve every center's hit identity/reachability. |
| Same: `WedgeAlignedIconFitsRotatedSquare`; 230-260 | M: remove larger-than-alternative preference; preserve supplied-inset containment, corner hit identity, cache invalidation. |
| Same: `UprightIconUsesAvailableRingThickness`; 285-289 | P: decorative enlargement preference only. |
| Same: `CenterLabelScaleUsesEachLinesActualWidth`; 357-364 | P: larger typography preference only. |
| Same: `CenterLabelLayoutMatchesExhaustiveOptimalScale`; 368-390 | M: remove maximum font-scale golden; preserve complete content/no empty lines and functional circular text containment. |

[ToolHeadPresentationMatrixTests](../VanillaExpanded.Tests/Unit/ItemRendering/ToolHeadPresentationMatrixTests.cs):

- P: `IdentityMapsPivotToAnchorAndUsesEffectiveSize` (21-25),
  `SuppliedAngleTracksWedge` (35-39), `AuthoredTransformUsesDocumentedOrder`
  (43-48), `AuthoredPivotAndScaleAxesComposeCorrectly` (52-58), and
  `AuthoredTurnExposesThicknessAcrossWedges` (64-77). These assert rendered model
  placement/orientation/thickness, not functional hit or clipping geometry.
- F: `InvalidOrOverflowingSizeFallsBack` (86-89), unsafe-input handling.
- Remove the `AssertPoint` appearance helper when its P callers are removed.

[ToolHeadPresentationIntegrationTests](../VanillaExpanded.Tests/Unit/ItemRendering/ToolHeadPresentationIntegrationTests.cs):

| Method; source lines | Decision and behavioral owner |
| --- | --- |
| `DedicatedDrawPreservesPreparedMeshAndIndependentMatrix`; 23-41 | M: remove model-matrix size/position values. Preserve mesh identity, source-transform non-mutation, lifecycle, restoration. |
| `DedicatedDrawUsesFixedUpperRightLight`; 137-161 | P: fixed light direction/brightness. Existing failure/restoration methods own state restoration. |
| `PreparedUnshadedMaterialRemainsUnshaded`; 166-172 | M: remove chosen neutral RGBA; preserve prepared unshaded-state forwarding. |
| `ShadedDrawHalvesAmbientAndStrengthensDirectLight`; 183-208 | P: chosen lighting/tint appearance only. |
| `PreparedShaderEffectsAndOverlayAreSubmitted`; 214-239 | M: remove selected glow/ambient/intensity policy. Preserve borrowed overlay ownership and faithful forwarding of supplied texture metadata/settings. |

Other methods (50, 80, 107, 244, 260, 273) retain routing, fallback, invalidation,
null-mesh handling, exception propagation, and state restoration. Despite its
name, this suite uses mocked renderer/shader services, not a live GPU boundary.

### Indicator geometry, commands, and effect tuning

[ItemSlotIndicatorTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/ItemSlotIndicatorTests.cs):
`CalculateBounds_BottomAlignedAndClamped` (195-202) is P: decorative fill
rectangle without a hit/clipping contract. `PremultiplyColor_PreservesAlpha`
(207-214) is F: generic supplied-value conversion, not image sampling.
Provider applicability, fractions, priorities, caching, and effect selection remain F.

In the following table, rendering paths are under
[indicator rendering tests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Rendering/).

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| `ItemSlotIndicatorDrawInputTests.StyleInput_PreservesOutlineOpacityAndMakesBarsOpaque`; 20-26 | M: remove style-chosen alpha override. Preserve transparent-input rejection and required overlay scheduling at input/routing boundaries. |
| Same: `BoundedFill_MapsLevelsWithoutChangingResourceMeaning`; 35-44 | M: remove rendered height/matrix expectations; preserve fraction sanitation/unusable-input rejection. |
| Same: `BoundaryCue_FadesAtFixedSlotLevels`; 56-65 | P: cue opacity, position, thickness. |
| Same: `DrawRange_RejectsInvalidLevelsAndContainsEdgeCues`; 70-82 | M: preserve range rejection/containment; remove chosen cue thickness. |
| Same: `InputSanitation_PreservesClippedSlotsAndProviderDefinedFullWarnings`; 87-105 | F: invalid/invisible rejection, channel sanitation, valid clipped coordinates, matrix validity. |
| Same: `RectangleTransform_MatchesEngineMatrixOperationsWithoutMutatingCaller`; 113-130 | M: remove decorative transform/depth golden; preserve caller-matrix non-mutation. |
| `ItemSlotIndicatorStyleLayoutTests.Layout_UsesMappedBackgroundAndActualBarFraction`; 18-29 | P: chosen bar/background/outline layout. |
| Same: `Outline_ScalesAndNeverOverlaps`; 38-65 | M: remove thickness/inset and translucent-corner/complete-border styling. Preserve containment/nonnegative extents as clipping protection. |
| Same: `Input_RetainsZeroWithoutApplyingBackgroundRanges`; 73-86 | M: remove visible border/track dimensions; preserve applicable zero resource, raw-fraction semantics, invalid/invisible rejection. |
| Same: `Rectangle_PreservesFractionalBoundsInExistingMatrixPath`; 93-108 | M: remove prescribed depth/placement; preserve invalid-scale rejection and justified shared-edge clipping consistency. |
| `ItemSlotIndicatorStyleRendererTests.OrdinaryStyle_DrawsActualFractionWithoutCues`; 26-48 | M: remove strip decomposition/decorative positions; preserve supplied fraction, invisible rejection, state restoration. |
| Same: `EffectFallback_RetainsSelectedStyle`; 62-87 | M: remove strip count; preserve fallback selection, failure containment, restore-before-fallback. |
| Same: `CompatibleEffect_DrawsSelectedBackgroundOnce`; 100-120 | M: remove rectangle implementation golden; preserve no duplicate logical fallback/background and restoration. |
| Same: `StyleSwitch_PreservesFoodOverlayAndPreparedResources`; 127-154 | M: remove strip-count sequence; preserve independent overlay data, restoration order, no reallocation. |
| Same: `EffectStyles_ValidateAndPreserveDefinitionEquality`; 159-175 | F: metadata validation, ownership, equality, compatibility. |
| `ItemSlotIndicatorRendererTests.BoundedEmpty_FallbackDrawsMappedFillAndBoundaryCue`; 74-85 | M: remove cue thickness/fill geometry; preserve empty-resource semantics, fallback, balanced state scope. |
| Same: `BoundedEffect_DrawsCueAfterReturningToGui`; 90-108 | M: remove mapped-fill visual command; preserve restore-before-submission and original resource semantics. |
| `ItemSlotIndicatorOutlineClipTests.Outline_DrawsAfterItemScissorIsPopped`; 19-62 | M: remove strip cardinality; preserve post-pop order, one logical submission, no repeated submission, original routing. |

Other indicator/effect findings:

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| [ItemSlotIndicatorFrameUpdaterTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Animation/ItemSlotIndicatorFrameUpdaterTests.cs): `OrthoBoundary_UpdatesOnceAndUnsubscribesOnRepeatedDisposal`; 22-46 | M: remove exact render-order number; preserve required scheduling, one update, snapshot stability, unsubscription. |
| [LiquidSloshIndicatorEffectTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Effects/LiquidSloshIndicatorEffectTests.cs): `Definition_UsesSharedSurfaceAndVisibleDrawRange`; 13-22 | M: remove chosen segment count/appearance parameters; preserve camera demand and valid compatible geometry declaration. |
| [FoodGrainIndicatorEffectTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Effects/FoodGrainIndicatorEffectTests.cs): `InitialState_IsSeparatedAndAtRest`; 15-43 | M: remove decorative radius range/axis-distribution preference; preserve finite/profile-sized storage, containment, non-overlap, resting state, deterministic reset. |
| Same: `Mesh_AddressesAllSharedGrains`; 48-62 | M: remove background-style preference; preserve particle addressing, indices, storage/mesh compatibility, camera demand. |
| [SolidMetalSimulationTests](../VanillaExpanded.Tests/Unit/CrucibleIndicators/SolidMetalSimulationTests.cs): `MetalInitialization_HasIndependentSizedSeparatedState`; 13-31 | M: remove selected particle count/radius tuning; preserve profile-sized storage, deterministic initialization, resting state, containment/non-overlap. |
| Same: `MaterialForcing_KeepsFoodAndMetalIndependent`; 38-51 | M: remove chosen impulse strengths/relative particle density; preserve independent profiles, signed/no-lift behavior, bounded finite input. |
| [CrucibleIndicatorProviderTests](../VanillaExpanded.Tests/Unit/CrucibleIndicators/CrucibleIndicatorProviderTests.cs): `MetalProfile_HasIndependentSlowerDynamics`; 111-120 | M: remove relative material tuning; preserve profile/resource independence and valid solver configuration. |
| [LiquidSloshMotionStateTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Effects/LiquidSloshMotionStateTests.cs): `MetalProfile_RestrainsMotionAndBraking`; 22-48 | M: remove chosen variation ratio/response magnitudes; preserve signed drive/braking and bounded finite state. |
| Same: `VerticalJostle_IsBoundedAndSettlesWithoutIdleForcing`; assertion at 154 | M: remove the chosen 8% response bound. Preserve bounded finite response, settling, and absence of idle forcing in the same method. |
| Same: `ConstantAcceleration_IsConsistentAcrossFrameRates`; assertion at 169 | M: remove the approximately 0.4 output golden derived from the production 0.2 gain. Preserve frame-rate consistency and signed response in the same method, without pinning a visual response amplitude. |
| Same: `AccelerationLimit_PreservesCombinedDirection`; assertions at 181-183 | M: remove the chosen magnitude and jostle-dependent direction bounds. Preserve finite limiting and combined-input direction semantics in the same method, independently of selected visual tuning. |

These additional slosh assertions depend on built-in tuning in
[LiquidSloshMotionState](../VanillaExpanded/src/ItemSlotIndicators/Effects/LiquidSlosh/LiquidSloshMotionState.cs)
(46-50) and
[LiquidSloshSimulationProfile](../VanillaExpanded/src/ItemSlotIndicators/Effects/LiquidSlosh/LiquidSloshSimulationProfile.cs)
(15), rather than values supplied by the test caller.

[ItemSlotIndicatorFrameStateTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Animation/ItemSlotIndicatorFrameStateTests.cs)
has M dispositions for `ControlledRotations_UseRightwardAndUpwardLookRates`
(82-90), `Damping_IsFrameRateIndependentAndDecaysAfterMotionStops` (95-106),
`WrappingAndPitchedBasis_AvoidEulerDiscontinuities` (111-123),
`RateBoundsAndLargeTurns_DoNotProduceUnboundedImpulses` (128-137), and
`EyeBobbing_ReactsWithoutRotationAndDecaysWhenStopped` (144-155).
Remove selected response amplitudes/decay-time goldens. Their same-method owners
retain axis/sign meaning, frame-rate independence, settling, continuity,
boundedness, and discontinuity reset. Numerical invariants are not pixel exceptions.

### Palettes, colors, configuration, and functional exclusions

Subsequent decision (2026-11-04): the user retired the shipped-asset contract
tests during filesystem-IO cleanup. The configuration-asset preservation
obligations below are historical, not requirements to restore those tests.
Runtime configuration behavior and serialization tests remain in scope.

| File / method; source lines | Decision and behavioral owner |
| --- | --- |
| [CrucibleParticlePaletteTests](../VanillaExpanded.Tests/Unit/CrucibleIndicators/CrucibleParticlePaletteTests.cs): `MissingClient_UsesNeutralMetalColors`; 42-54 | P: selected fallback RGB palette. |
| Same: `Ingredients_RetainDistinctColorsAndInvalidateOnReplacement`; 16-37 | F: supplied sample decoding/provenance, stable sampling, invalidation. |
| [CrucibleIndicatorProviderTests](../VanillaExpanded.Tests/Unit/CrucibleIndicators/CrucibleIndicatorProviderTests.cs): `MetalColors_SampleContentsOnceAndReuseAcrossStacks`; 125-137; `FirepitInput_UsesIngredientsAndExcludesOtherSlots`; 142-186 | F: supplied sample decoding/cache reuse; ingredient versus vessel/output provenance, amount, eligibility. |
| [FoodContainerParticlePaletteTests](../VanillaExpanded.Tests/Unit/FoodContainerIndicators/FoodContainerParticlePaletteTests.cs): `PortionedFoodWithoutMealInterface_UsesItsOwnColors`; 17-28; `Ingredients_SupplyStablePaletteUntilTheirIdentityChanges`; 33-64 | F: source selection, supplied-value decoding, invalidation, no vessel sampling. |
| [LiquidContainerParticleColorTests](../VanillaExpanded.Tests/Unit/LiquidContainerIndicators/LiquidContainerParticleColorTests.cs): `BakedParticleTextureUsesAtlasAverage`; 16-32; `MaterialReplacementRefreshesCachedTint`; 37-56 | F: mock decoding, source selection, material replacement, caching; no actual pixel readback. |
| [AnimalSexDistanceFadeTests](../VanillaExpanded.Tests/Unit/AnimalSexIndicators/AnimalSexDistanceFadeTests.cs): `FadeReachesZeroAtEightBlocks`; 17-19 | M: remove near-opacity plateau/intermediate curve; preserve suppression at/beyond configured range at eligibility boundary. |
| Historical `Unit/AnimalSexIndicators/AnimalSexConfigurationTests.cs`: `AnimalControlsHaveDedicatedSection`; 11-33 | M at Phase 1: remove presentation-order weights; preserve client ownership, setting coverage, localization. The suite was subsequently removed by the filesystem-IO decision above. |
| [AutoLootConfigurationTests](../VanillaExpanded.Tests/Unit/AutoLoot/AutoLootConfigurationTests.cs): `Settings_AreLocalGroupedLocalizedAndMatchPersistedDefaults`; 17-49 | M: remove ordering weights; preserve persisted defaults, valid behavioral delay range, client ownership, localization. |
| [FreshnessIndicatorStyleConfigTests](../VanillaExpanded.Tests/Unit/Configuration/FreshnessIndicatorStyleConfigTests.cs): `ItemSlotIndicatorSettings_HaveDedicatedSection`; 15-31 | P: section placement/weight. Declaration coverage already belongs to `ConfigLibIntegrationTests.ConfigLibAsset_DeclaresEveryConfigProperty` (17-47). |
| [RecipeBookConfigContractTests](../VanillaExpanded.Tests/Unit/Configuration/RecipeBookConfigContractTests.cs): `RecipeBookToggle_IsClientSideLocalizedAndMatchesTheConfigDefault`; 29-42 | M: remove ordering weight; preserve default agreement, client ownership, localization. |
| [ConfigLoadingTests](../VanillaExpanded.Tests/Unit/Configuration/ConfigLoadingTests.cs): `LoadConfig_MissingConfig_CreatesAndPersistsDefaults`; 27-38 | M: remove selected visual-intensity baseline; preserve default-object creation, enablement, persistence. |

Explicit retain decisions preventing overbroad removal:

- All seven [ColorUtilExTests](../VanillaExpanded.Tests/Unit/Extensions/ColorUtilExTests.cs)
  methods (12, 22, 36, 51, 61, 72, 82): supplied-value interpolation, endpoints,
  sanitation, and numerical interpolation mode.
- Configuration serialization/selection: persisted keys and legacy mappings,
  live selectors, saved selection, enablement, and mode selection remain behavior.
- [ToolHeadPresentationResolverTests](../VanillaExpanded.Tests/Unit/ItemRendering/ToolHeadPresentationResolverTests.cs)
  (20-168): authored input decoding/coercion, sentinels, snapshots, validation,
  diagnostics, correction rereads; not rendered placement.
- [ToolHeadShaderUniformStateTests](../VanillaExpanded.Tests/Unit/ItemRendering/ToolHeadShaderUniformStateTests.cs):
  `NestedAndExceptionalMutationsRestoreExactCallerValues` (13): caller restoration.
- [AnimalSexIndicatorDrawTests](../VanillaExpanded.Tests/Unit/AnimalSexIndicators/AnimalSexIndicatorDrawTests.cs):
  `DrawPreservesFrameShaderActivation` (16-48): activation/exception safety.
- [ItemSlotIndicatorMeshGeometryTests](../VanillaExpanded.Tests/Unit/ItemSlotIndicators/Rendering/ItemSlotIndicatorMeshGeometryTests.cs)
  (19-63), effect-definition identity/validation, simulation shader guards,
  resource allocation/reload/failure: ABI, valid storage/indices, supplied values,
  bounds/stability, ownership, and failure handling.
- [RadialMenuTests](../VanillaExpanded.Tests/RadialMenu/RadialMenuTests.cs):
  `HitTestUsesRenderedRadiiAndFixedClockwiseWedges` (24),
  `NestedMenusHitTestFromOuterRingToSingleOptionDisc` (38),
  `MultiOptionCenterMenuOccupiesOnlyCenterDisc` (52),
  `NestedInteractionSelectsEntriesAtEveryDepth` (68),
  `RoundedCornersDoNotSelectTheirCutouts` (102),
  `CounterclockwiseCentersAgreeWithHitTest` (116),
  `IconCornersRemainWithinTheirWedges` (206),
  `UprightIconEdgesRetainWedgePadding` (301), and size/cache/input validation:
  actual supplied-geometry reachability and containment.
- Radial/QuickTools/ToolMode labels/quantities identifying selectable items,
  source ownership, directional mode mapping, paging, callbacks, selection/cancel.
- Other inventory, crafting/allocation, transfer, network, lifecycle, eligibility,
  resource-fraction, and persistence findings: domain values are not style metrics.

## 4. Shared helper and fixture decisions

| Source / helper; lines | Disposition and retained owner |
| --- | --- |
| [GuiTestPixels](../VanillaExpanded.Tests/TestSupport/Gui/GuiTestPixels.cs): `Pixel`; 7-13 | Remove after pixel callers; no approved use remains. |
| [GuiStyledControlsTestsTestSupport](../VanillaExpanded.Tests/TestSupport/Gui/GuiStyledControlsTestsTestSupport.cs): `Raster.Rgb`, `MaxAlpha`, `HasColor`; 14-28 | Remove raster observations, not replace with color-command goldens. |
| Same: `ControlsFixture`; 31-108 | Remove raster copying (59-61). Preserve locale/input setup, allocation/deletion, cleanup; decouple real composition later. |
| [GuiElementIconTabsTestsTestSupport](../VanillaExpanded.Tests/TestSupport/Gui/GuiElementIconTabsTestsTestSupport.cs): `AssertHints`; 16-50 | Retain hint/focus/clip behavior; depth used as a locator is not a fixed-depth contract. |
| Same: `NativeScissorRectangle`; 53-64 | Retain actual region conversion/containment, not border styling. |
| Same: `Create`, `Compose`; 87-103 | Broad Cairo entry; decouple unnecessary composition while retaining tab behavior. |
| [RecipeBookLifecycleTestsTestSupport](../VanillaExpanded.Tests/TestSupport/RecipeBook/RecipeBookLifecycleTestsTestSupport.cs): `AssertEmptyStateCentered`; 24-41 | Remove centering/orientation/spacing helper. Callers: feedback 190 and layout 300. |
| Same: `AssertBookTabPosition`; 68-84 | Remove fixed gap/centering; retain opener non-overlap, moved hit region, hint coverage. Callers: dialog layout 193-218 and native input 471. |
| [RecipeBookIconRendererTestsTestSupport](../VanillaExpanded.Tests/TestSupport/RecipeBook/RecipeBookIconRendererTestsTestSupport.cs): `States`; 49-51 | Remove appearance-state uses; domain availability/selection belongs to session/evaluation. |
| Same: `AssertPopupDepth`; 16-46 | Retain relative occlusion/clip restoration, not absolute depth. |
| Same: `CreateTiles`; 54-64 | Constructor font entry even without explicit composition. |
| [GuiControlNativeProbeTests](../VanillaExpanded.Tests/Integration/Gui/GuiControlNativeProbeTests.cs): `ButtonProbe`, `InputProbe`, `ListProbe`, `TitleProbe`; 277-451 | Remove appearance overpainting/pixel fields. Retain probes needed for actual activation/edit/list/drag/close. |
| [GuiRenderFixture](../VanillaExpanded.Tests/TestSupport/Gui/GuiRenderFixture.cs); 14-64 | Retain allocation/deletion, scissor safeguards, scale/thread restoration. Construction alone does not demonstrate native loading. |
| [RecipeBookRenderFixture](../VanillaExpanded.Tests/TestSupport/RecipeBook/RecipeBookRenderFixture.cs); 12-120 | Retain scheduling/completion, generations/lifetimes, shader/mesh ownership, teardown; remove unrelated text/composer setup from cache-only cases. |
| [RecipeBookTestFixtures](../VanillaExpanded.Tests/TestSupport/RecipeBook/RecipeBookTestFixtures.cs): `MenuFixture`; 22-377 | Split broad graphics/assets/translations/inventory/attachment/composition setup. Preserve state/input/lifetime contracts. Remove appearance-only `OptionsDraws`/`BookDraws`/`CategoryDraws` capture when no ownership/provenance use remains. |
| Same: `LifecycleFixture`; 403-467 | Retain world/session/filter/settings/lifecycle; no intrinsic Cairo operation identified in construction/finalization. |
| Same: `NativeScope`; 389-400; [RecipeBookInteractionTestsTestSupport](../VanillaExpanded.Tests/TestSupport/RecipeBook/RecipeBookInteractionTestsTestSupport.cs): `GuiScaleScope`; 69-73 | Retain global-state restoration/serialization; not native-loading operations. |
| [RecipeBookLayoutCallCounter](../VanillaExpanded.Tests/TestSupport/RecipeBook/RecipeBookLayoutCallCounter.cs) | Retain cache/lifecycle measurement/placement/composition counters, not appearance oracles. |
| Other Recipe Book allocation/catalogue/transfer/filter/performance/tooltip-summary support | Retain domain/input ownership and cleanup; no image-observation contract identified. |
| Indicator rendering support, other feature mocks/support | Retain fake backend/resource/mesh lifetime and domain observations. No additional image/style helper identified. |

## 5. Native Cairo entry-point trace

Assertion policy and native loading are separate: removing pixels does not
automatically make retained tests Cairo-free.

### Engine sources

Engine references are read-only and outside this worktree, under
`D:\CODE\VintageStory\vsapi`.

| Source; lines | Native entry or distinction |
| --- | --- |
| `Client\UI\CairoFont.cs`; 33-36 | Static initializer creates a 1x1 surface/context. Normal font construction/factories can load Cairo before drawing. |
| Same; 243-257 | Measurement calls native context/font/text extents. |
| `Client\UI\GuiComposer.cs`; 360-429 | Real composition creates surface/context, composes elements, uploads texture. |
| Same; 236-239, 134 | Recomposition and `CreateEmpty` still invoke real composition. |
| `Client\UI\TextTextureUtil.cs`; 15-19, 51-85, 100-105 | Constructor only stores API; generation allocates/draws Cairo resources. |
| `Client\UI\Elements\Impl\Interactive\Text\GuiElementEditableTextBase.cs`; 175, 378, 414 | Editing measurement and text/selection textures introduce surfaces. |
| Native `GuiElementTextButton`, `GuiElementToggleButton`, `GuiElementScrollbar`; 107/145, 81/116/145, 89-95 respectively | Control composition/recomposition introduces surfaces; type references alone do not. |

### Production sources

| Source; lines | Native entry |
| --- | --- |
| [GuiControlVisuals](../VanillaExpanded/src/Gui/GuiControlVisuals.cs); 89-90, 110-115 | Font measurement and drawing surfaces. |
| [GuiElementIconTabs](../VanillaExpanded/src/Gui/GuiElementIconTabs.cs); 104, 116, 121 | Fonts, child composition, font-backed hints. |
| [RecipeBookIntegration](../VanillaExpanded/src/RecipeBook/RecipeBookIntegration.cs); 155-171 | Font-backed opener plus recomposition on attachment. |
| [GuiDialogRecipeBook](../VanillaExpanded/src/RecipeBook/GuiDialogRecipeBook.cs); 288, 331-399 | Composition, real font and multiline text measurement. |
| [GuiElementRecipeBookTile](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookTile.cs); 22, 69 | Constructor creates two fonts; lightweight composition override does not remove that dependency. |
| [RecipeBookIconRenderer](../VanillaExpanded/src/RecipeBook/RecipeBookIconRenderer.cs); 43-49 | Real quantity-label text textures. |
| [GuiElementRecipeBookOptions](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookOptions.cs); 72-106 | Fonts, icon surfaces, popup composition. |
| [GuiElementRecipeBookTooltip](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookTooltip.cs); 74; [RecipeBookTooltip](../VanillaExpanded/src/RecipeBook/RecipeBookTooltip.cs); 20, 32, 66-74 | Rich text composition and fonts. |
| Styled button/input/dropdown/checkbox/scrollbar/arrow implementations | Composition paths allocate surfaces. |

### Fixture-to-behavior ownership

- Enabled `MenuFixture` attachment creates the opener/fonts and recomposes:
  browsing restoration, category presentation, dialog feedback/layout, handbook
  actions, layout-cache dialog/lifecycle, opener state, options/search clear,
  session ownership, and relevant system-lifecycle methods inherit it.
  Preserve their behavior at session/settings/browsing/layout/cache/system
  boundaries, with focused real-control wiring where required.
- Native translation-recompose, dialog input, handbook, first three native-bounds
  methods, and tooltip methods inherit the same fixture. Domain mocks alone do
  not prove actual engine interoperability.
- Tab `Create -> Compose` affects tab component/composer suites; retain tab
  selection/input/clipping/resource owners.
- `ControlsFixture.Compose` and real control construction affect state-rendering
  and styled-control compatibility; retain activation/editing/list/focus/scroll/
  resource owners.
- Tile construction affects renderer, geometry/interaction, native tile input,
  tile-using cache cases, and the first cleanup method. Constructor fonts remain
  even with mocked atlas/render services.
- Output quantities greater than one can reach real text textures.
- `RecipeBookDialogCleanupTests.RepeatedDialogCleanupDetachesCallbacksAndCancelsQueuedInventoryWork`
  uses `GuiComposer.CreateEmpty` at 136; preserve queued-work/callback cleanup at
  a focused lifecycle boundary rather than assuming an empty composer is managed.

Do not infer native loading merely from a fixture/class/category name:

- `GuiRenderFixture` constructs `TextTextureUtil`, but its constructor does not draw.
- Invalid-step scrollbar validation rejects before arrow creation; the engine
  base constructor creates only a managed texture wrapper.
- Cache-only `GuiRequestsOnlyQueueAndDoNotTouchTheRenderApi`,
  `RepeatedPreparationSharesOneFourOutstandingRequestBudget`, and
  `ReloadAndDisposalDiscardRequestsNotYetSubmittedToTheNativeQueue` (320, 345,
  414) use mocked cache boundaries, not tiles/fonts.
- `RecipeBookSystemLifecycleTests.WorldLifecycleOwnsLocallyQueuedNativePendingAndReadyIcons`
  (273) similarly uses mocked cache/lifecycle operations.
- `DisabledAtWorldLoadDoesNotLoadSystemAndEnablingWaitsForReload` (60-92) avoids
  attachment; production integration returns before font/control creation.
- `NativeTranslationCacheCompatibilityTests.LocaleFallbackAndNativeReloadsReplaceEntryTablesWithoutReplacingServices`
  (92 onward) exercises translation services, not fonts.
- `RecipeBookNativeBoundsCompatibilityTests.NativeRootCalculationLeavesInitializedChildrenAloneAndRepairsAnExplicitlyDirtyChild`
  (423-457) exercises bounds, not Cairo.
- `ElementBounds`, shader/texture types, imports, and mock Cairo signatures alone
  are not proof of native calls.

### Dependency/configuration distinctions

[Test project](../VanillaExpanded.Tests/VanillaExpanded.Tests.csproj) lines 35-38
reference managed `cairo-sharp`; line 91 separately copies Windows native Cairo.
The [mod project](../VanillaExpanded/VanillaExpanded.csproj) also references the
managed assembly. Retained engine signatures may require that assembly even
after native calls disappear.

SkiaSharp is separate (test project 31-34, 92); the menu bitmap factory at
144-145 is a potential Skia path, not Cairo evidence.

The [CI workflow](../.github/workflows/build.yml) runs unfiltered tests on Linux.
There is no established category-based Cairo-free lane. At Phase 1, no native
exception or separate lane was approved; see section 7 for the later decision.

## 6. Handoff and Phase 1 traceability

Test levels remain based on actual boundaries, not native dependencies:
isolated rules are Unit; composed collaborators behind mocked services are
Component; actual engine interoperability is Integration. No inspected method
establishes a deployed end-to-end boundary. Retagging is later work.

| Phase 1 task | Controlling source / applicable requirement | Static evidence / later owner |
| --- | --- | --- |
| Confirm policy | User clarification; plan purpose/constraints | Policy section: no pixel/presentation exceptions; functional geometry permitted. |
| Inventory pixels/helpers | Plan Phase 1 | Whole-project sweep; sections 1-4 include all earlier pixel methods, helpers, callers, and dispositions. |
| Include non-pixel presentation | Plan Phase 1 | Sections 1-3 include palettes, fonts, dimensions, coordinates, shader/draw expectations, lighting, and tuning. |
| Classify each suspect assertion | Policy and plan preserve-behavior constraints | P/M/F method entries and explicit functional exclusions identify removal and coverage owners. |
| Trace Cairo separately | Plan Phase 1; directly inspected engine/production sources in section 5 | Entry-point/fixture trace and non-native distinctions; runtime proof belongs to later verification. |
| Record method/fixture decisions | Plan Phase 1 completion contract | This document is the persistent decision record; sections 1-5 assign same-method or named boundary owners. |

All identified assertion families have dispositions; no policy ambiguity remains
requiring a user decision. Later implementation must preserve activation,
callbacks, editing, focus, disabled/hidden behavior, state persistence, actual
hit/scissor agreement, reachability/limits, stale-press cancellation, scheduling,
reload generations, ownership/disposal, non-mutation, and failure restoration.
Do not treat a reduced test count as evidence that those obligations are met.

## 7. Phase 4 native dependency decision

On 2026-11-04, the user selected: "Approve focused native behavioral residuals;
document each dependency and purpose." This approval applies to the actual
engine/control, measured-text, resource-ownership, and drawing-context contracts
below. It does not permit pixel-output or presentation-only assertions, approve
an optional native-backed suite, or make test category a capability selector.
These tests remain part of the existing test selection.

The table identifies current test methods by suite. "All tests" includes each
fact/theory in that suite, not shared helpers or unrelated managed suites.
The historical method dispositions in sections 1-2 continue to define the
behavior to preserve, not appearance baselines.

### Approved residuals

| Current test scope | Native entry | Retained behavioral purpose |
| --- | --- | --- |
| [GuiControlContextStateIntegrationTests](../VanillaExpanded.Tests/Integration/Gui/GuiControlContextStateIntegrationTests.cs): `FrameRestoresCallerContextState` | Real `ImageSurface`/`Context`, then `GuiControlDrawing.Frame` | Verify restoration of the caller's actual Cairo context state. Invalid-input and color-conversion tests remain in the managed `GuiControlVisualsTests` suite. |
| [GuiElementIconTabsTests](../VanillaExpanded.Tests/Component/Gui/GuiElementIconTabsTests.cs): all tests | Real tab fonts, child composer, hints, and tab texture generation | Native selection/input/focus, clipped hit routing, selected-tab containment, overflow/reachability, host-clip restoration, and resource reuse/disposal. `MeasureWidth` arithmetic alone is not the reason for this exception. |
| [GuiStyledControlStateRenderingTests](../VanillaExpanded.Tests/Component/Gui/GuiStyledControlStateRenderingTests.cs): both tests | `ControlsFixture.Compose` and real styled controls | Actual dropdown selection transitions and scrollbar capture/cancellation, with stable resources and cleanup. |
| [GuiStyledControlsNativeCompatibilityTests](../VanillaExpanded.Tests/Integration/Gui/GuiStyledControlsNativeCompatibilityTests.cs): all tests | Native/styled control constructors, fonts, editing measurement, and composition | Verify opt-in control wiring, caller labels, activation, editing, callbacks, tab/focus routing, scrolling, clipped hit targets, and resource ownership. |
| [GuiControlNativeProbeTests](../VanillaExpanded.Tests/Integration/Gui/GuiControlNativeProbeTests.cs): all tests | Real engine controls, fonts, scratch contexts, and composer composition | Actual native button/input/list/tab/title behavior, including editing, drag persistence, close, focus, and hit-region agreement. |
| [GuiElementIconTabsComposerIntegrationTests](../VanillaExpanded.Tests/Integration/Gui/GuiElementIconTabsComposerIntegrationTests.cs): its single test | Native composer and tab composition | Composer/bounds ownership, recomposition stability, and child texture disposal. |
| [RecipeBookBrowsingRestorationTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookBrowsingRestorationTests.cs): all tests | Enabled `MenuFixture`, opener and real dialog composition | Restoration through actual parent/menu opening, closure, failed opening, and composer replacement, including control state and current inventory. |
| [RecipeBookCategoryPresentationTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookCategoryPresentationTests.cs): both tests | Enabled `MenuFixture`, category tabs and localized heading composition | Actual category-code routing despite excluded/duplicate labels, localized hints/headings, and selection/search retention through resize/reopen. |
| [RecipeBookDialogFeedbackTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookDialogFeedbackTests.cs): all tests | Real menu, footer/search/filter controls, and tooltip composition | Actual feedback actions, filter/control wiring, live-inventory refresh, disabled actions, hint hit regions, tooltip focus dismissal, and alternative-count content. |
| [RecipeBookDialogLayoutTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookDialogLayoutTests.cs): all tests | Dialog font/text measurement and real composer/control construction | Measured label fit, usable/nonoverlapping controls, native viewport/hit/scissor agreement, and focus/row-anchor restoration across layout changes. |
| [RecipeBookHandbookActionTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookHandbookActionTests.cs): both tests | Real menu and handbook control | Actual button callback/error wiring and disabled state for unresolved output previews. |
| [RecipeBookLayoutCacheDialogTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookLayoutCacheDialogTests.cs): both tests | `GuiDialogRecipeBook.MeasureLayout` and actual dialog composition | Verify the dialog hands validated settings/live scale to its cache and accepts actual composed bounds. The separate cache tests cover arithmetic/invalidation without these fonts. |
| [RecipeBookLayoutLifecycleTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookLayoutLifecycleTests.cs): all tests | Real dialog measurements, composer, options popup, and render lifecycle | Actual compositor reuse/invalidation, moved hit regions, view/focus continuity, attachment failure, insufficient-space closure, and rejected-input feedback across recomposition. |
| [RecipeBookOpenerStateTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookOpenerStateTests.cs): all tests | Font-backed opener and attached dialog composition | Actual opener press/release/cancellation, failed-open usability, and open-state synchronization. |
| [RecipeBookOptionsTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookOptionsTests.cs): all tests | Real options fonts/SVG/context and popup composition; real menu for interaction cases | Options-owned texture lifetime and actual keyboard/mouse/wheel/focus routing, validation feedback, setting persistence/application, anchor retention, and parent-close cleanup. The texture-only case uses focused GUI services, not a world/menu fixture. |
| [RecipeBookSearchClearTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookSearchClearTests.cs): its single test | Real menu/search input composition | Verify clear activation requires an uninterrupted left-button gesture on the actual input control. |
| [RecipeBookSessionOwnershipTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookSessionOwnershipTests.cs): `SessionFilterRegistrationsSurviveRecompositionAndAreReleasedWithTheDialog` only | Real dialog composer replacement | Prove dialog-owned filter registrations survive recomposition and are released with the dialog. Independent session ownership remains tested without a menu. |
| [RecipeBookSystemLifecycleTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookSystemLifecycleTests.cs): `DisablingLiveDisposesSystemAndBlocksReactivationUntilReload`, `UnrelatedConfigReloadKeepsEnabledSystemAndExistingMenu`, `WorldCleanupDetachesRestorationAndInvalidatesQueuedRecomposition` only | Enabled `MenuFixture`, actual menu attachment/recomposition | Live system/menu disposal, preservation on unrelated reload, and cancellation of queued restoration/recomposition. Disabled-startup and world-cache policy tests use focused lifecycle setup. |
| [RecipeBookDialogCleanupTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookDialogCleanupTests.cs): `NativeTilesFitAndClipVisibleIconsAndDisposeTheirTexturesAndSubscriptions` only | Real tile construction and drawing/composition resources | Actual tile clipping, shader restoration, request bounds, resource reuse/release, and callback detachment. Queued inventory-work cleanup uses an owned uncomposed composer. |
| [RecipeBookIconCacheTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookIconCacheTests.cs): `ScrollingRetainsRecentPreviewsAndReloadsRebuildOwnedResources`, `DisposedTileRetainsDeferredIconCompletionUntilWorldCacheDisposal`, `ClosingAndReopeningTheMenuReusesTheWorldsBakedIcons`, `AtlasSubmissionsRunBeforeGuiDrawingAndPublishOnlyOnNativeCompletion` only | Real tile consumer constructors create fonts; renderer may generate quantity labels | Preserve the tile-to-cache boundary: viewport-driven requests, renderer ownership/reload, consumer disposal before deferred completion, reuse across consumers, and actual submission/draw ordering. Other cache-policy tests request entries directly without tile/text composition. |
| [RecipeBookIconRendererTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookIconRendererTests.cs): all tests | Real tiles create fonts; renderer generates real quantity-label textures for outputs greater than one | Actual tile-to-renderer/cache handoff, size/status/selection mapping, independent quantity-resource lifetime, viewport/tile clipping, and shader/scissor/resource restoration on failures. No font or shader appearance values are approved. |
| [RecipeBookTilesGeometryTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookTilesGeometryTests.cs): all tests | Real tile constructors create fonts | Actual native button hit regions, first/last item reachability, partial-row clipping, anchor/inspection/focus restoration, and held-press cancellation. |
| [RecipeBookTilesInteractionTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookTilesInteractionTests.cs): all tests | Real tile constructors create fonts | Actual native container/button rebinding, virtualization, focus/hover recovery, clipped clicks, scroll limits, and stale-press rejection. |
| [RecipeBookTooltipCompositionTests](../VanillaExpanded.Tests/Component/RecipeBook/RecipeBookTooltipCompositionTests.cs): all tests | Real menu, tooltip fonts, rich text/native grid composition | Measured/wrapped summary fit and refresh, warning/description content, and safe preview/grid construction with unresolved or absent output. |
| [RecipeBookDialogNativeInputTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookDialogNativeInputTests.cs): all tests | Real menu/composer and native controls | Actual native drag/movement, search editing, category/sort/checkbox/options input, scrolling, focus/tab/hotkey routing, SVG registration, and texture lifetimes. |
| [RecipeBookTileNativeInputTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookTileNativeInputTests.cs): both tests | Real tiles and native scrollbar/control construction | Actual mouse/keyboard/shift activation, focus visibility, and refresh deferral. |
| [RecipeBookNativeBoundsCompatibilityTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookNativeBoundsCompatibilityTests.cs): first three tests only | Real menu/options popup composition | Native hit/render/scissor translation and retained focus without unnecessary recomposition. `NativeRootCalculationLeavesInitializedChildrenAloneAndRepairsAnExplicitlyDirtyChild` uses bounds only and is not a native-Cairo exception. |
| [RecipeBookNativeTooltipIntegrationTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookNativeTooltipIntegrationTests.cs): all tests | Real native tooltip fonts, rich text and recipe-grid composition | Native tooltip content/choice resolution, measured wrapping/containment, and replacement-preview ownership. |
| [RecipeBookNativePopupLayeringTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookNativePopupLayeringTests.cs): its single test | Real dropdown popup and quantity-label textures | Verify relative popup occlusion and balanced clipping against actual quantity labels, not absolute drawing-depth constants. |
| [RecipeBookHandbookIntegrationTests](../VanillaExpanded.Tests/Integration/RecipeBook/RecipeBookHandbookIntegrationTests.cs): its single test | Real menu/handbook button | Native mouse/keyboard activation of the selected output's handbook page without crafting placement. |
| [NativeTranslationCacheCompatibilityTests](../VanillaExpanded.Tests/Integration/RecipeBook/NativeTranslationCacheCompatibilityTests.cs): `RecipeBookRecomposesAfterNativeSameLocaleTranslationReloads` only | Actual dialog remeasurement/recomposition after translation reload | Prove same-locale native translation reload invalidates the real dialog while retaining browsing state. The independent translation-service test does not require Cairo. |

### Boundary decisions

- Retain `RecipeBookSession`, `RecipeBookLayout`, `RecipeBookLayoutCache`, and
  `RecipeBookIconCache` as the managed policy boundaries. Cache request budgets,
  generations, completion/retry, size keys, and world-cache disposal do not need
  tile consumers merely to request an icon. Their focused tests are in
  [RecipeBookIconCachePolicyTests](../VanillaExpanded.Tests/Unit/RecipeBook/RecipeBookIconCachePolicyTests.cs);
  the four real tile-consumer methods remain in the component suite.
- Constructing an uncomposed `GuiComposer` normally is sufficient for disabled
  attachment and queued-work cleanup. `CreateEmpty` is not a managed replacement:
  it invokes real composition.
- `GuiRenderFixture.TextTexture` is lazy. Access still returns the real engine
  service; generation remains native. Its managed constructor alone was not a
  Cairo entry in the first place.
- Tile buttons inherit the engine's text-button implementation, whose normal
  constructor requires fonts. Replacing that control merely to remove constructor
  font initialization would change the native input/focus boundary being tested.
  The approved tile-consumer cases retain that dependency explicitly.
- Layout arithmetic and cache invalidation already accept measured dimensions
  or a measurement callback. Injecting pretend text metrics into dialog-fit
  tests would not verify real localized text containment; production keeps its
  real font and multiline measurement paths. No new measurement abstraction is
  needed for the managed layout/cache coverage.
- Imports, Cairo types in mocked method signatures, managed texture wrappers,
  GUI-scale/thread scopes, native bounds arithmetic, and inventory/network
  interoperability are not independently evidence of native Cairo execution.
  The approval does not extend to other methods merely sharing a file, fixture
  type, or test category.

This decision does not claim Cairo elimination. The native scopes above use
the independent `RequiresNative=Cairo` capability. Skia was assessed separately:
the styled checkbox overrides the engine's pattern-backed switch composition;
the number input uses Cairo embossing, and the list menus are not multiselect.
The custom dialog background and SVG delegates calling mocked `DrawSvg` do not
reach the engine's bitmap-backed pattern helpers. The unused bitmap factory
setups were removed rather than treating a lazy `BitmapExternal` constructor
as evidence of executed Skia work. No retained test declares a Skia requirement.
See the [test guidance](../README.md#native-dependencies-and-assertion-policy)
for selection and platform setup. Native cases remain mandatory in CI.

Clean-process dependency checks and Windows/Linux execution belong to Phase 6.
Retained native behavioral coverage still needs resolvable libraries in the
environments that execute it; declared capability selection is not runtime
proof that the complementary scope cannot load native graphics libraries.
