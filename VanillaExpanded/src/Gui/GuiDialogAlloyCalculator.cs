using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.Network;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Gui;

/// <summary>
/// A GUI dialog for calculating alloy metal ratios and required units.
/// Displays as a floaty dialog attached to the firepit block.
/// </summary>
public sealed class GuiDialogAlloyCalculator : GuiDialogBlockEntity
{
    #region Constants
    private const string DialogKey = "alloycalculator";
    private const double SliderWidth = 150;
    private const double LabelWidth = 80;
    private const double RowHeight = 35;
    private const double DropdownWidth = 150;
    private const double InputWidth = 70;
    private const double TargetUnitsButtonWidth = 20;
    private const double TargetUnitsControlGap = 5;
    private const double TargetUnitsButtonHeight = 13;
    private const double TargetUnitsButtonVerticalGap = 3;
    private const float TargetUnitsButtonFontSize = 12;
    private const int DefaultTargetUnits = 100;
    private const int TargetUnitsInputStep = 100;
    private const double WasteWarningHeight = 32;
    private const double WasteWarningGap = 6;
    private const double TitlebarHeight = 20;
    private const double SlotSize = 40;
    private const double ButtonHeight = 25;
    private const double SectionTitleHeight = 20;
    private const double SmeltingTemperatureHeight = 20;
    private const double FuelSlotHeight = SlotSize + 8;
    private const double FuelStatusHeight = 64;
    private const double FuelInfoHeight = 15 + SectionTitleHeight + FuelSlotHeight + FuelStatusHeight + 6;
    private const float FuelInfoRefreshInterval = 0.5f;
    #endregion

    #region Saved State
    /// <summary>
    /// Stores saved dialog state per block entity position.
    /// </summary>
    private static readonly Dictionary<BlockPos, SavedDialogState> savedStates = [];

    private sealed class SavedDialogState
    {
        public int SelectedAlloyIndex { get; set; }
        public int TargetUnits { get; set; } = DefaultTargetUnits;
        public Dictionary<int, int> SliderValues { get; set; } = [];
    }
    #endregion

    #region Fields
    private readonly GuiDialog? firepitDialog;
    private List<MetalDepositOption> depositOptions = [];
    private MetalDepositOption? selectedOption;
    
    /// <summary> Currently selected ingredients for the chosen alloy. </summary>
    private ImmutableArray<MetalDepositIngredient> selectedIngredients = [];
    private readonly Dictionary<int, int> sliderValues = [];
    private readonly Dictionary<int, ItemStack> calculatedStacks = [];
    private readonly List<SlideshowItemstackTextComponent> slideshowComponents = [];
    private int targetUnits = DefaultTargetUnits;
    private bool isAdjustingSliders;
    private float fuelInfoRefreshElapsed;
    private string? fuelInfoText;
    private string? smeltingTemperatureText;
    private ItemStack? fuelPreviewStack;
    private ItemStack[] prewarmingPreviewStacks = [];
    private ItemStack? savedFuelPreviewStack;
    private Action<bool, bool, double>? updateOptionalSectionLayout;
    
    // Cached handbook data for filtering
    private ItemStack[]? handbookStacks;
    private List<ItemStack>? smeltingContainers;
    private List<ItemStack>? smeltingFuels;
    private int maxFuelTemperature;
    #endregion

    #region Properties
    public override double DrawOrder => 0.2;
    public override string ToggleKeyCombinationCode => string.Empty;
    protected override double FloatyDialogPosition => 0.5;
    protected override double FloatyDialogAlign => 0;

    /// <summary>
    /// Gets the calculated ingredient stacks for the current alloy configuration.
    /// Key is the ingredient index, value is the ItemStack with the calculated amount.
    /// </summary>
    public IReadOnlyDictionary<int, ItemStack> CalculatedIngredientStacks => calculatedStacks;
    #endregion

    #region Accessors
    public double FirepitDialogWidth => firepitDialog?.SingleComposer?.Bounds.OuterWidth ?? 0;
    #endregion

    #region Constructor
    public GuiDialogAlloyCalculator(ICoreClientAPI capi, BlockPos blockPos, GuiDialog firepitDialog) 
        : base(Lang.Get($"{Constants.ModId}:gui-alloycalculator-title"), blockPos, capi)
    {
        if (IsDuplicate) return;
        this.firepitDialog = firepitDialog;
        LoadAlloys();
    }
    #endregion

    #region Initialization
    private void LoadAlloys()
    {
        BuildHandbookStacksCache();

        depositOptions = [.. capi.GetMetalAlloys()
            .Where(static alloy => alloy.Enabled && alloy.Ingredients.Length > 0)
            .Select(AlloyCalculatorLogic.FromAlloyRecipe)];
        depositOptions.AddRange(AlloyCalculatorLogic.CreatePureMetalOptions(
            handbookStacks ?? [],
            depositOptions,
            maxFuelTemperature));
        depositOptions.Sort(static (left, right) => string.Compare(
            GetDepositOptionDisplayName(left),
            GetDepositOptionDisplayName(right),
            StringComparison.CurrentCulture));
    }

    // TODO: There has to be a better way to calculate/cache these item-stack variants, ideally we should be capable of leveraging the cache that the handbook already has internally.
    /// <summary>Caches handbook variants and derives the maximum smelting temperature from burning fuels.</summary>
    private void BuildHandbookStacksCache()
    {
        var stacks = new List<ItemStack>();
        smeltingContainers = [];
        smeltingFuels = [];

        foreach (var obj in capi.World.Collectibles)
        {
            var objStacks = obj.GetHandBookStacks(capi);
            if (objStacks is null) continue;

            foreach (var stack in objStacks)
            {
                stacks.Add(stack);

                // Collect smelting containers (crucibles, etc.)
                if (stack.ItemAttributes?["cookingContainerSlots"].Exists == true)
                {
                    smeltingContainers.Add(stack);
                }

                // Collect fuels
                var combustProps = stack.Collectible.GetCombustibleProperties(capi.World, stack, null);
                if (combustProps is { BurnDuration: > 0, BurnTemperature: > 0 })
                {
                    smeltingFuels.Add(stack);
                }
            }
        }

        handbookStacks = stacks.ToArray();

        // Calculate max fuel temperature
        maxFuelTemperature = smeltingFuels
            .Select(f => f.Collectible.GetCombustibleProperties(capi.World, f, null)!.BurnTemperature)
            .DefaultIfEmpty(0)
            .Max();
    }
    #endregion

    #region Dialog Composition
    internal static ElementBounds CreateDialogBounds(ElementBounds? firepitBounds)
        => ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterTop)
            .WithFixedAlignmentOffset(
                (firepitBounds?.OuterWidth ?? 0) / 2,
                (firepitBounds?.absY ?? 0) / RuntimeEnv.GUIScale);

    internal static (double WarningReduction, double SlotReduction, double StatusReduction)
        GetOptionalSectionReductions(bool showWarning, bool showFuelSlot, double fuelStatusHeight)
        => (showWarning ? 0 : WasteWarningHeight + WasteWarningGap,
            showFuelSlot ? 0 : FuelSlotHeight,
            FuelStatusHeight - fuelStatusHeight);

    private void ComposeDialog()
    {
        updateOptionalSectionLayout = null;
        // Calculate number of ingredient rows
        var ingredientCount = selectedIngredients.Length;
        bool showRatioControls = selectedOption is not null
            && AlloyCalculatorLogic.ShouldShowRatioControls(selectedOption);

        // Define content bounds - this establishes the size of our dialog content
        // Width: either slider row or slot row, whichever is wider
        var sliderRowWidth = showRatioControls ? LabelWidth + SliderWidth : 0;
        var slotRowWidth = ingredientCount * SlotSize;
        var controlsWidth = DropdownWidth + 10 + InputWidth
            + TargetUnitsControlGap + TargetUnitsButtonWidth;
        var contentWidth = Math.Max(controlsWidth, Math.Max(sliderRowWidth, slotRowWidth));
        
        // Height: titlebar + dropdown row + sliders + slot row + button row
        var contentHeight = TitlebarHeight + 30;
        if (ingredientCount > 0)
        {
            if (showRatioControls)
            {
                contentHeight += ingredientCount * RowHeight;
            }
            contentHeight += 15 + SectionTitleHeight + SlotSize;
            contentHeight += 18 + SmeltingTemperatureHeight + WasteWarningHeight + WasteWarningGap + FuelInfoHeight + ButtonHeight;
        }
        var contentBounds = ElementBounds.Fixed(0, 0, contentWidth, contentHeight);

        // Background bounds with padding, sized to fit children
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(contentBounds);

        var dialogBounds = CreateDialogBounds(firepitDialog?.SingleComposer?.Bounds);

        // Build the UI - start below title bar
        var yOffset = TitlebarHeight;

        // Define element bounds
        var dropdownBounds = ElementBounds.Fixed(0, yOffset, DropdownWidth, 25);
        var inputBounds = ElementBounds.Fixed(DropdownWidth + 10, yOffset, InputWidth, 25);
        var incrementBounds = ElementBounds.Fixed(
            DropdownWidth + 10 + InputWidth + TargetUnitsControlGap,
            yOffset,
            TargetUnitsButtonWidth,
            TargetUnitsButtonHeight);
        var decrementBounds = ElementBounds.Fixed(
            DropdownWidth + 10 + InputWidth + TargetUnitsControlGap,
            yOffset + TargetUnitsButtonHeight + TargetUnitsButtonVerticalGap,
            TargetUnitsButtonWidth,
            TargetUnitsButtonHeight);
        yOffset += 30;

        var alloyValues = depositOptions.Select(static (_, i) => i.ToString());
        var alloyNames = depositOptions.Select(static (option, _) => GetDepositOptionDisplayName(option));
        var alloyIcons = depositOptions.Select(option =>
        {
            Item? outputItem = capi.World.GetItem(option.OutputCode);
            return outputItem is null ? null : new ItemStack(outputItem);
        });
        var selectedIndex = selectedOption is not null ? depositOptions.IndexOf(selectedOption) : 0;
        if (selectedIndex < 0) selectedIndex = 0;

        var composer = capi.Gui
            .CreateCompo($"{DialogKey}{BlockEntityPosition}", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get($"{Constants.ModId}:gui-alloycalculator-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddInteractiveElement(new GuiElementItemStackDropDown(
                capi,
                [.. alloyValues],
                [.. alloyNames],
                [.. alloyIcons],
                selectedIndex,
                OnAlloySelected,
                dropdownBounds,
                CairoFont.WhiteSmallText()), "alloyDropdown")
            .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-dropdown-tooltip"), CairoFont.WhiteDetailText(), 250, dropdownBounds.FlatCopy(), "dropdownTooltip")
            .AddNumberInput(inputBounds, OnTargetUnitsChanged, CairoFont.WhiteDetailText(), "targetUnits")
            .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-targetunits-tooltip"), CairoFont.WhiteDetailText(), 250, inputBounds.FlatCopy(), "targetUnitsTooltip")
            .AddButton("+", () => StepTargetUnits(true), incrementBounds, CairoFont.WhiteDetailText().WithFontSize(TargetUnitsButtonFontSize).WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Small, "incrementTargetUnits")
            .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-increment-tooltip"), CairoFont.WhiteDetailText(), 250, incrementBounds.FlatCopy(), "incrementTargetUnitsTooltip")
            .AddButton("-", () => StepTargetUnits(false), decrementBounds, CairoFont.WhiteDetailText().WithFontSize(TargetUnitsButtonFontSize).WithOrientation(EnumTextOrientation.Center), EnumButtonStyle.Small, "decrementTargetUnits")
            .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-decrement-tooltip"), CairoFont.WhiteDetailText(), 250, decrementBounds.FlatCopy(), "decrementTargetUnitsTooltip");

        // Add ingredient sliders if an alloy is selected
        if (selectedOption is not null && ingredientCount > 0)
        {
            if (showRatioControls)
            {
                for (var idx = 0; idx < ingredientCount; idx++)
                {
                    var ingredient = selectedIngredients[idx];
                    var ingredientIndex = idx;
                    var ingredientName = GetIngredientDisplayName(ingredient);

                    var labelBounds = ElementBounds
                        .Fixed(0, yOffset, LabelWidth, RowHeight)
                        .WithParent(contentBounds);
                    var sliderBounds = ElementBounds
                        .Fixed(LabelWidth, yOffset + 4, SliderWidth, 20)
                        .WithParent(contentBounds);

                    var sliderKey = $"slider_{ingredientIndex}";
                    var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                    var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                    var sliderTooltip = Lang.Get($"{Constants.ModId}:gui-alloycalculator-slider-tooltip", ingredientName, minPercent, maxPercent);

                    composer
                        .AddStaticText(ingredientName, CairoFont.WhiteSmallText(), labelBounds)
                        .AddSlider(value => OnSliderChanged(ingredientIndex, value), sliderBounds, sliderKey)
                        .AddHoverText(
                            sliderTooltip,
                            CairoFont.WhiteDetailText(),
                            250,
                            sliderBounds.FlatCopy().WithParent(contentBounds),
                            $"sliderTooltip_{ingredientIndex}");

                    yOffset += RowHeight;
                }
            }

            // Add second divider before slots
            yOffset += 5;
            var divider2Bounds = ElementBounds.Fixed(0, yOffset, contentWidth, 1);
            composer.AddInset(divider2Bounds, 1, 0.5f);
            yOffset += 10;

            composer.AddStaticText(
                Lang.Get($"{Constants.ModId}:gui-alloycalculator-metals-title"),
                CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(0, yOffset, contentWidth, SectionTitleHeight).WithParent(contentBounds));
            yOffset += SectionTitleHeight;

            // Create slideshow components for each ingredient
            slideshowComponents.Clear();
            var richTextComponents = new List<RichTextComponentBase>();
            const int slotPadding = 5;

            for (var i = 0; i < ingredientCount; i++)
            {
                var ingredient = selectedIngredients[i];
                var stacks = GetAllMetalVariantStacks(ingredient, 1);
                
                if (stacks.Length > 0)
                {
                    var slideshow = new SlideshowItemstackTextComponent(capi, stacks, SlotSize, EnumFloat.Inline)
                    {
                        ShowStackSize = true,
                        Background = true,
                        PaddingRight = slotPadding,
                        ExtraTooltipText = "\n" + Lang.Get($"{Constants.ModId}:gui-alloycalculator-itemstack-tooltip")
                    };
                    slideshowComponents.Add(slideshow);
                    richTextComponents.Add(slideshow);
                }
            }

            // Create centered slot bounds - parent to contentBounds for proper alignment
            var slotsWidth = ingredientCount * (SlotSize + slotPadding);
            var slotBounds = ElementBounds
                .Fixed(0, yOffset, slotsWidth, SlotSize + 8)
                .WithParent(contentBounds)
                .WithAlignment(EnumDialogArea.CenterFixed);
            composer.AddRichtext(richTextComponents.ToArray(), slotBounds, "ingredientSlots");

            yOffset += (int)SlotSize + 18;
            composer.AddDynamicText(
                string.Empty,
                CairoFont.WhiteDetailText().WithFontSize(12).WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(0, yOffset, contentWidth, SmeltingTemperatureHeight).WithParent(contentBounds),
                "smeltingTemperature");
            yOffset += SmeltingTemperatureHeight;
            var warningBounds = ElementBounds
                .Fixed(0, yOffset, contentWidth, WasteWarningHeight)
                .WithParent(contentBounds);
            composer.AddDynamicText(
                string.Empty,
                CairoFont.WhiteDetailText()
                    .WithFontSize(12)
                    .WithColor([1, 0.75, 0.3, 1])
                    .WithOrientation(EnumTextOrientation.Center),
                warningBounds,
                "wasteWarning");

            yOffset += WasteWarningHeight + WasteWarningGap;
            var fuelDividerBounds = ElementBounds.Fixed(0, yOffset + 5, contentWidth, 1);
            composer.AddInset(fuelDividerBounds, 1, 0.5f);
            yOffset += 15;
            var fuelTitleBounds = ElementBounds.Fixed(0, yOffset, contentWidth, SectionTitleHeight)
                .WithParent(contentBounds);
            composer.AddStaticText(
                Lang.Get($"{Constants.ModId}:gui-alloycalculator-fuel-title"),
                CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Center),
                fuelTitleBounds);
            yOffset += SectionTitleHeight;
            var fuelSlotBounds = ElementBounds
                .Fixed(0, yOffset, SlotSize, FuelSlotHeight)
                .WithParent(contentBounds)
                .WithAlignment(EnumDialogArea.CenterFixed);
            composer.AddRichtext(Array.Empty<RichTextComponentBase>(), fuelSlotBounds, "fuelSlot");
            yOffset += FuelSlotHeight;
            var fuelInfoBounds = ElementBounds
                .Fixed(0, yOffset, contentWidth, FuelStatusHeight)
                .WithParent(contentBounds);
            composer.AddRichtext(Array.Empty<RichTextComponentBase>(), fuelInfoBounds, "fuelInfo");
            yOffset += FuelStatusHeight + 6;
            var buttonBounds = ElementBounds
                .Fixed(0, yOffset, 100, ButtonHeight)
                .WithParent(contentBounds)
                .WithAlignment(EnumDialogArea.CenterFixed);
            var buttonTooltipBounds = buttonBounds.FlatCopy();
            composer
                .AddSmallButton(Lang.Get($"{Constants.ModId}:gui-alloycalculator-deposit"), OnDepositButtonClicked, buttonBounds, EnumButtonStyle.Normal, "depositButton")
                .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-deposit-tooltip"), CairoFont.WhiteDetailText(), 250, buttonTooltipBounds, "depositTooltip");

            (ElementBounds Bounds, double Y)[] fuelSectionBounds =
            [
                (fuelDividerBounds, fuelDividerBounds.fixedY),
                (fuelTitleBounds, fuelTitleBounds.fixedY),
                (fuelSlotBounds, fuelSlotBounds.fixedY),
                (fuelInfoBounds, fuelInfoBounds.fixedY)
            ];
            double expandedButtonY = buttonBounds.fixedY;
            bool warningExpanded = true;
            bool fuelSlotExpanded = true;
            double currentFuelStatusHeight = FuelStatusHeight;
            updateOptionalSectionLayout = (showWarning, showFuelSlot, fuelStatusHeight) =>
            {
                if (showWarning == warningExpanded && showFuelSlot == fuelSlotExpanded
                    && fuelStatusHeight == currentFuelStatusHeight) return;
                warningExpanded = showWarning;
                fuelSlotExpanded = showFuelSlot;
                currentFuelStatusHeight = fuelStatusHeight;
                var (warningReduction, slotReduction, statusReduction) = GetOptionalSectionReductions(
                    showWarning, showFuelSlot, fuelStatusHeight);
                foreach (var (bounds, originalY) in fuelSectionBounds)
                    bounds.fixedY = originalY - warningReduction;
                fuelSlotBounds.fixedHeight = showFuelSlot ? FuelSlotHeight : 0;
                fuelInfoBounds.fixedY -= slotReduction;
                fuelInfoBounds.fixedHeight = fuelStatusHeight;
                buttonBounds.fixedY = expandedButtonY - warningReduction - slotReduction - statusReduction;
                buttonTooltipBounds.fixedY = buttonBounds.fixedY;
                contentBounds.fixedHeight = contentHeight - warningReduction - slotReduction - statusReduction;
                SingleComposer.Bounds.MarkDirtyRecursive();
                SingleComposer.ReCompose();
            };
        }

        SingleComposer = composer.EndChildElements().Compose();
        fuelInfoText = null;
        smeltingTemperatureText = null;
        fuelPreviewStack = null;
        prewarmingPreviewStacks = [];
        savedFuelPreviewStack = null;

        // Set target units value
        var targetInput = SingleComposer?.GetNumberInput("targetUnits");
        if (targetInput is not null)
        {
            targetInput.Interval = TargetUnitsInputStep;
            targetInput.IntMode = false;
        }
        targetInput?.SetValue(targetUnits.ToString());

        // Initialize slider values after composition
        if (selectedOption is not null)
        {
            InitializeSliderValues();
            UpdateResultsDisplay();
        }
    }
    #endregion

    #region Slider Logic
    private void InitializeSliderValues()
    {
        if (selectedOption is null || SingleComposer is null) return;

        sliderValues.Clear();

        // Initialize each slider to the midpoint of its valid range
        for (var i = 0; i < selectedIngredients.Length; i++)
        {
            var ingredient = selectedIngredients[i];
            var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
            var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
            var midPoint = (minPercent + maxPercent) / 2;

            sliderValues[i] = midPoint;

            var slider = SingleComposer.GetSlider($"slider_{i}");
            slider?.SetValues(midPoint, minPercent, maxPercent, 1, "%");
        }

        // Normalize to ensure sum is 100%
        NormalizeSliderValues(-1);
    }

    private bool OnSliderChanged(int changedIndex, int newValue)
    {
        if (isAdjustingSliders || selectedOption is null) return true;

        sliderValues[changedIndex] = newValue;
        NormalizeSliderValues(changedIndex);
        SaveSliderValues();
        UpdateResultsDisplay();

        return true;
    }

    /// <summary>
    /// Saves current slider values to the saved state.
    /// </summary>
    private void SaveSliderValues()
    {
        var state = GetOrCreateSavedState();
        state.SliderValues.Clear();
        foreach (var (idx, value) in sliderValues)
        {
            state.SliderValues[idx] = value;
        }
    }

    private void NormalizeSliderValues(int changedIndex)
    {
        if (selectedOption is null || SingleComposer is null) return;

        isAdjustingSliders = true;

        try
        {
            var totalPercent = sliderValues.Values.Sum();
            var difference = totalPercent - 100;

            if (Math.Abs(difference) < 1) return; // Already at 100%

            // Distribute the difference among other sliders proportionally
            var otherIndices = sliderValues.Keys.Where(i => i != changedIndex).ToList();
            if (otherIndices.Count == 0) return;

            // Calculate how much each other slider can absorb
            var adjustments = new Dictionary<int, int>();
            var totalAdjustable = 0.0;

            foreach (var idx in otherIndices)
            {
                var ingredient = selectedIngredients[idx];
                var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                var currentValue = sliderValues[idx];

                // If we need to decrease (difference > 0), check how much we can decrease
                // If we need to increase (difference < 0), check how much we can increase
                var adjustable = difference > 0
                    ? currentValue - minPercent
                    : maxPercent - currentValue;

                adjustments[idx] = adjustable;
                totalAdjustable += adjustable;
            }

            if (totalAdjustable <= 0) return;

            // Apply proportional adjustments
            var remaining = Math.Abs(difference);
            foreach (var idx in otherIndices)
            {
                if (remaining <= 0) break;

                var proportion = adjustments[idx] / totalAdjustable;
                var adjustment = (int)Math.Round(Math.Abs(difference) * proportion);
                adjustment = Math.Min(adjustment, adjustments[idx]);
                adjustment = Math.Min(adjustment, remaining);

                if (difference > 0)
                {
                    sliderValues[idx] -= adjustment;
                }
                else
                {
                    sliderValues[idx] += adjustment;
                }

                remaining -= adjustment;

                // Update the slider UI
                var slider = SingleComposer.GetSlider($"slider_{idx}");
                if (slider is not null)
                {
                    var ingredient = selectedIngredients[idx];
                    var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                    var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                    slider.SetValues(sliderValues[idx], minPercent, maxPercent, 1, "%");
                }
            }
        }
        finally
        {
            isAdjustingSliders = false;
        }
    }
    #endregion

    #region Results Calculation
    private void UpdateResultsDisplay()
    {
        if (selectedOption is null || SingleComposer is null) return;

        calculatedStacks.Clear();
        var nuggetCounts = AlloyCalculatorLogic.CalculateAllNuggetsRequired(
            targetUnits, sliderValues, selectedIngredients);

        for (var i = 0; i < selectedIngredients.Length; i++)
        {
            var ingredient = selectedIngredients[i];
            var nuggets = nuggetCounts.GetValueOrDefault(i);

            // Update slideshow component with new stack size
            if (i < slideshowComponents.Count)
            {
                var stacks = GetAllMetalVariantStacks(ingredient, nuggets);
                slideshowComponents[i].Itemstacks = stacks;
            }

            // Store the primary stack (metal bit) for external use
            if (nuggets > 0)
            {
                var stack = GetMetalBitStack(ingredient, nuggets);
                if (stack is not null)
                {
                    calculatedStacks[i] = stack;
                }
            }
        }

        long wastedUnits = AlloyCalculatorLogic.CalculateWastedMetalUnits(
            targetUnits, sliderValues, selectedIngredients);
        SingleComposer.GetDynamicText("wasteWarning")?.SetNewText(wastedUnits > 0
            ? Lang.Get($"{Constants.ModId}:gui-alloycalculator-waste-warning", wastedUnits)
            : string.Empty);
        UpdateFuelInfo();
    }

    private void UpdateFuelInfo()
    {
        var element = SingleComposer?.GetRichtext("fuelInfo");
        var fuelSlot = SingleComposer?.GetRichtext("fuelSlot");
        if (element is null || fuelSlot is null) return;
        fuelInfoRefreshElapsed = 0;
        BlockEntityFirepit? firepit = capi.World.BlockAccessor
            .GetBlockEntity<BlockEntityFirepit>(BlockEntityPosition);
        AlloyFuelDepositPlan? plan = null;
        float? batchMeltingPoint = null;
        AlloyDepositResultCode result = firepit is null
            || calculatedStacks.Count != selectedIngredients.Length
            ? AlloyDepositResultCode.InvalidRequest
            : AlloyFuelDepositService.CreatePreview(capi, firepit, calculatedStacks,
                capi.World.Player.InventoryManager, out plan, out _, out batchMeltingPoint);
        string temperatureText = batchMeltingPoint is > 0
            ? Lang.Get($"{Constants.ModId}:gui-alloycalculator-smelting-temperature", batchMeltingPoint.Value.ToString("0.#"))
            : Lang.Get($"{Constants.ModId}:gui-alloycalculator-smelting-temperature-unavailable");
        if (temperatureText != smeltingTemperatureText)
        {
            smeltingTemperatureText = temperatureText;
            SingleComposer?.GetDynamicText("smeltingTemperature")?.SetNewText(temperatureText);
        }
        string text = result switch
        {
            AlloyDepositResultCode.Success when plan!.PrewarmingStacks.Length > 0 => Lang.Get(
                $"{Constants.ModId}:gui-alloycalculator-fuel-prewarming"),
            AlloyDepositResultCode.Success => string.Empty,
            AlloyDepositResultCode.InsufficientItems => Lang.Get($"{Constants.ModId}:gui-alloycalculator-fuel-info-unavailable"),
            AlloyDepositResultCode.InventoryClosed => Lang.Get($"{Constants.ModId}:gui-alloycalculator-deposit-fuel-inventory-closed"),
            _ => Lang.Get($"{Constants.ModId}:gui-alloycalculator-fuel-info-no-batch")
        };
        ItemStack? previewStack = result == AlloyDepositResultCode.Success && plan!.DesiredAmount > 0
            ? plan.DesiredStack.Clone() : null;
        if (previewStack is not null) previewStack.StackSize = plan!.DesiredAmount;
        bool samePreview = previewStack is null
            ? fuelPreviewStack is null
            : fuelPreviewStack is not null
                && previewStack.StackSize == fuelPreviewStack.StackSize
                && previewStack.Equals(capi.World, fuelPreviewStack, GlobalConstants.IgnoredStackAttributes);
        if (!samePreview)
        {
            fuelPreviewStack = previewStack;
            foreach (RichTextComponentBase component in fuelSlot.Components) component.Dispose();
            fuelSlot.SetNewText(previewStack is null
                ? Array.Empty<RichTextComponentBase>()
                : [new SlideshowItemstackTextComponent(capi, [previewStack], SlotSize, EnumFloat.Inline)
                {
                    ShowStackSize = true,
                    Background = true
                }]);
        }
        ItemStack[] prewarmingStacks = plan?.PrewarmingStacks ?? [];
        ItemStack? savedStack = prewarmingStacks.Length > 0 ? plan!.DesiredStack.Clone() : null;
        if (savedStack is not null) savedStack.StackSize = 1;
        if (text == fuelInfoText && prewarmingStacks.Length == prewarmingPreviewStacks.Length
            && prewarmingStacks.Zip(prewarmingPreviewStacks).All(pair => SameFuelPreview(pair.First, pair.Second))
            && SameFuelPreview(savedStack, savedFuelPreviewStack))
        {
            UpdateOptionalSectionLayout();
            return;
        }
        fuelInfoText = text;
        prewarmingPreviewStacks = prewarmingStacks.Select(stack => stack.Clone()).ToArray();
        savedFuelPreviewStack = savedStack;
        foreach (RichTextComponentBase component in element.Components) component.Dispose();
        var font = CairoFont.WhiteDetailText().WithFontSize(12);
        if (savedStack is null)
        {
            element.SetNewText(string.IsNullOrEmpty(text)
                ? Array.Empty<RichTextComponentBase>()
                : [new RichTextComponent(capi, text, font)]);
            UpdateOptionalSectionLayout();
            return;
        }
        var components = new List<RichTextComponentBase>
        {
            new RichTextComponent(capi, Lang.Get($"{Constants.ModId}:gui-alloycalculator-fuel-prewarming-label"), font)
            {
                VerticalAlign = EnumVerticalAlign.Middle
            }
        };
        components.Add(CreateFuelInfoSlot(prewarmingStacks));
        components.Add(new RichTextComponent(capi, text, font)
        {
            VerticalAlign = EnumVerticalAlign.Middle
        });
        components.Add(CreateFuelInfoSlot([savedStack]));
        element.SetNewText(components.ToArray());
        UpdateOptionalSectionLayout();
    }

    private void UpdateOptionalSectionLayout()
    {
        bool showWarning = AlloyCalculatorLogic.CalculateWastedMetalUnits(
            targetUnits, sliderValues, selectedIngredients) > 0;
        double statusHeight = string.IsNullOrEmpty(fuelInfoText)
            ? 0
            : Math.Ceiling(SingleComposer!.GetRichtext("fuelInfo").TotalHeight / RuntimeEnv.GUIScale) + 1;
        updateOptionalSectionLayout?.Invoke(showWarning, fuelPreviewStack is not null, statusHeight);
    }

    private bool SameFuelPreview(ItemStack? left, ItemStack? right)
        => left is null ? right is null : right is not null && left.StackSize == right.StackSize
            && left.Equals(capi.World, right, GlobalConstants.IgnoredStackAttributes);

    private SlideshowItemstackTextComponent CreateFuelInfoSlot(ItemStack[] stacks)
        => new(capi, stacks, SlotSize, EnumFloat.Inline)
        {
            ShowStackSize = true,
            Background = true,
            PaddingLeft = 4,
            PaddingRight = 4
        };

    /// <summary>
    /// Gets all metal variant stacks (nuggets, ore chunks, etc.) that smelt into the given metal.
    /// Filters by handbook visibility and smeltability.
    /// </summary>
    private ItemStack[] GetAllMetalVariantStacks(MetalDepositIngredient ingredient, int stackSize)
    {
        // The ingredient's ResolvedItemstack is the ingot - we need items that smelt into this
        ItemStack targetIngot = ingredient.ResolvedStack;
        if (handbookStacks is null) return [];

        // Filter handbook stacks to find items that smelt into this metal and can be smelted
        var stacks = handbookStacks
            .Where(stack => 
                targetIngot.Equals(capi.World, stack.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack, GlobalConstants.IgnoredStackAttributes)
                && CanSmelt(stack))
            .Select(stack => 
            {
                var clone = stack.Clone();
                clone.StackSize = stackSize;
                return clone;
            });

        var filtered = stacks.Where(static stack => stack.Collectible?.FirstCodePart() == "metalbit" || stack.Collectible?.FirstCodePart() == "nugget");
        return [.. filtered];
    }

    /// <summary>
    /// Checks if an item can be smelted based on fuel temperature and melting point.
    /// </summary>
    private bool CanSmelt(ItemStack stack)
    {
        var combustProps = stack.Collectible.CombustibleProps;
        if (combustProps is null) return false;
        
        // Check if fuel temperature is high enough to melt this item
        if (combustProps.MeltingPoint > maxFuelTemperature) return false;

        return true;
    }

    /// <summary>
    /// Gets a metal bit ItemStack for the given ingredient.
    /// </summary>
    private ItemStack? GetMetalBitStack(MetalDepositIngredient ingredient, int stackSize)
    {
        // Extract metal name from ingredient code (e.g., "ingot-copper" -> "copper")
        string code = ingredient.Code.Path;

        var metalName = code.Contains('-')
            ? code[(code.LastIndexOf('-') + 1)..]
            : code;

        // Get the metal bit item
        var bitCode = new AssetLocation("game", $"metalbit-{metalName}");
        var bitItem = capi.World.GetItem(bitCode);
        
        if (bitItem is null) return null;

        return new ItemStack(bitItem, stackSize);
    }
    #endregion

    #region Event Handlers
    private void OnAlloySelected(string code, bool selected)
    {
        if (!int.TryParse(code, out var index) || index < 0 || index >= depositOptions.Count)
        {
            return;
        }

        selectedOption = depositOptions[index];
        selectedIngredients = selectedOption.Ingredients.OrderBy(static ingredient => GetIngredientDisplayName(ingredient)).ToImmutableArray();
        
        // Save selected alloy index
        GetOrCreateSavedState().SelectedAlloyIndex = index;
        
        ComposeDialog();
    }

    private void OnTargetUnitsChanged(string value)
    {
        if (int.TryParse(value, out var units) && units > 0)
        {
            targetUnits = units;
            GetOrCreateSavedState().TargetUnits = units;
            UpdateResultsDisplay();
        }
    }

    /// <summary>
    /// Moves the target field to the nearest whole-nugget batch within the recipe ratios.
    /// </summary>
    private bool StepTargetUnits(bool increase)
    {
        int updatedUnits = AlloyCalculatorLogic.FindAdjacentWasteFreeTarget(
            targetUnits, sliderValues, increase, selectedIngredients);
        SingleComposer?.GetNumberInput("targetUnits")?.SetValue(updatedUnits.ToString());
        OnTargetUnitsChanged(updatedUnits.ToString());
        return true;
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnDepositButtonClicked()
    {
        BlockEntityFirepit? firepit = capi.World.BlockAccessor
            .GetBlockEntity<BlockEntityFirepit>(BlockEntityPosition);
        AlloyDepositResultCode ingredientResult = firepit is null || selectedOption is null
            ? AlloyDepositResultCode.InvalidRequest
            : AlloyDepositService.Execute(capi, firepit, selectedIngredients, calculatedStacks);
        ShowDepositError(ingredientResult, fuel: false);
        UpdateFuelInfo();
        if (ingredientResult != AlloyDepositResultCode.Success || firepit is null)
        {
            return true;
        }

        AlloyDepositResultCode fuelResult = AlloyFuelDepositService.Execute(capi, firepit);
        ShowDepositError(fuelResult, fuel: true);
        UpdateFuelInfo();
        return true;
    }
    #endregion

    #region Deposit Logic

    private void ShowDepositError(AlloyDepositResultCode result, bool fuel)
    {
        if (result == AlloyDepositResultCode.Success) return;

        string resultKey = result switch
        {
            AlloyDepositResultCode.InventoryClosed => "inventory-closed",
            AlloyDepositResultCode.InvalidRecipe => "invalid-recipe",
            AlloyDepositResultCode.InsufficientItems => "insufficient-items",
            AlloyDepositResultCode.InsufficientSpace => "insufficient-space",
            AlloyDepositResultCode.TransferFailed => "transfer-failed",
            _ => "invalid-request"
        };
        string keyPrefix = fuel ? "deposit-fuel" : "deposit";
        capi.TriggerIngameError(
            this,
            $"alloy-{keyPrefix}-{resultKey}",
            Lang.Get($"{Constants.ModId}:gui-alloycalculator-{keyPrefix}-{resultKey}"));
    }
    #endregion

    #region Dialog Lifecycle
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        MetalDepositOption? detectedOption = DetectOptionFromCrucible(out ItemStack[] contents);
        if (detectedOption is not null)
        {
            OnAlloySelected(depositOptions.IndexOf(detectedOption).ToString(), true);
            ApplyDetectedRatios(contents);
            return;
        }

        // Restore saved state or use defaults
        if (savedStates.TryGetValue(BlockEntityPosition, out var state))
        {
            targetUnits = state.TargetUnits;
            OnAlloySelected(state.SelectedAlloyIndex.ToString(), true);
            
            // Restore slider values after dialog is composed
            RestoreSliderValues(state);
        }
        else
        {
            OnAlloySelected("0", true);
        }
    }

    private MetalDepositOption? DetectOptionFromCrucible(out ItemStack[] contents)
    {
        contents = [];
        BlockEntityFirepit? firepit = capi.World.BlockAccessor
            .GetBlockEntity<BlockEntityFirepit>(BlockEntityPosition);
        if (firepit?.Inventory is not InventorySmelting inventory) return null;

        contents = [.. inventory.CookingSlots
            .Where(static slot => !slot.Empty)
            .Select(static slot => slot.Itemstack)
            .OfType<ItemStack>()];
        return AlloyCalculatorLogic.FindOptionForContents(
            contents,
            depositOptions,
            capi.GetMetalAlloys());
    }

    private void ApplyDetectedRatios(IReadOnlyList<ItemStack> contents)
    {
        if (SingleComposer is null || selectedOption is null) return;

        var composition = AlloyCalculatorLogic.CalculateContentComposition(
            contents,
            selectedIngredients);
        Dictionary<int, int> percentages = composition.Percentages;
        if (percentages.Count == 0) return;

        SingleComposer.GetNumberInput("targetUnits")?.SetValue(composition.TotalUnits.ToString());
        OnTargetUnitsChanged(composition.TotalUnits.ToString());

        isAdjustingSliders = true;
        try
        {
            foreach (var (index, percentage) in percentages)
            {
                MetalDepositIngredient ingredient = selectedIngredients[index];
                int minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                int maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                int clampedPercentage = Math.Clamp(percentage, minPercent, maxPercent);

                sliderValues[index] = clampedPercentage;
                SingleComposer.GetSlider($"slider_{index}")
                    ?.SetValues(clampedPercentage, minPercent, maxPercent, 1, "%");
            }
        }
        finally
        {
            isAdjustingSliders = false;
        }

        SaveSliderValues();
        UpdateResultsDisplay();
    }

    public override void OnGuiClosed()
    {
        capi.Gui.PlaySound(CloseSound);
    }

    /// <summary>
    /// Restores slider values from saved state.
    /// </summary>
    private void RestoreSliderValues(SavedDialogState state)
    {
        if (SingleComposer is null || selectedOption is null) return;

        isAdjustingSliders = true;
        try
        {
            foreach (var (idx, value) in state.SliderValues)
            {
                if (idx >= selectedIngredients.Length) continue;

                var ingredient = selectedIngredients[idx];
                var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
                var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);
                var clampedValue = Math.Clamp(value, minPercent, maxPercent);

                sliderValues[idx] = clampedValue;
                var slider = SingleComposer.GetSlider($"slider_{idx}");
                slider?.SetValues(clampedValue, minPercent, maxPercent, 1, "%");
            }
        }
        finally
        {
            isAdjustingSliders = false;
        }

        UpdateResultsDisplay();
    }

    /// <summary>
    /// Gets or creates a saved state for the current block entity.
    /// </summary>
    private SavedDialogState GetOrCreateSavedState()
    {
        if (!savedStates.TryGetValue(BlockEntityPosition, out var state))
        {
            state = new SavedDialogState();
            savedStates[BlockEntityPosition] = state;
        }
        return state;
    }

    public override void OnRenderGUI(float deltaTime)
    {
        fuelInfoRefreshElapsed += deltaTime;
        if (fuelInfoRefreshElapsed >= FuelInfoRefreshInterval) UpdateFuelInfo();
        if (capi.Settings.Bool["immersiveMouseMode"]
            && firepitDialog?.SingleComposer?.Bounds is ElementBounds firepitBounds)
        {
            var anchor = new Vec3d(BlockEntityPosition.X + 0.5,
                BlockEntityPosition.Y + FloatyDialogPosition, BlockEntityPosition.Z + 0.5);
            Vec3d projected = MatrixToolsd.Project(anchor, capi.Render.PerspectiveProjectionMat,
                capi.Render.PerspectiveViewMat, capi.Render.FrameWidth, capi.Render.FrameHeight);
            AlignImmersiveDialog(SingleComposer.Bounds, firepitBounds, capi.Render.FrameHeight - projected.Y);
        }
        base.OnRenderGUI(deltaTime);
    }

    internal static void AlignImmersiveDialog(ElementBounds bounds, ElementBounds firepitBounds, double projectedAnchorY)
    {
        // The base renderer resets absFixedY to this projected anchor, but retains absolute offsets.
        bounds.absFixedY = projectedAnchorY;
        bounds.absMarginY = 0;
        bounds.absOffsetY = 0;
        bounds.absOffsetY = firepitBounds.renderY - bounds.renderY;
        bounds.absOffsetX = bounds.OuterWidth / 2 + firepitBounds.OuterWidth / 2 - 5;
    }

    public override bool TryOpen()
    {
        if (depositOptions.Count == 0)
        {
            LoadAlloys();
            if (depositOptions.Count == 0)
            {
                return false; // No alloys available
            }
        }

        return base.TryOpen();
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Gets the display name for a material based on its asset location.
    /// Delegates to AlloyCalculatorLogic for testability.
    /// </summary>
    private static string GetMaterialDisplayName(in AssetLocation assetLocation)
        => AlloyCalculatorLogic.GetMaterialDisplayName(assetLocation);

    private static string GetDepositOptionDisplayName(in MetalDepositOption option)
        => AlloyCalculatorLogic.GetAlloyDisplayName(option.OutputCode);

    private static string GetIngredientDisplayName(in MetalDepositIngredient ingredient)
        => AlloyCalculatorLogic.GetIngredientDisplayName(ingredient.Code);
    #endregion
}
