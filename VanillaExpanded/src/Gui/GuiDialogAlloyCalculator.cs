using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace VanillaExpanded.Gui;

/// <summary>
/// A GUI dialog for calculating alloy metal ratios and required units.
/// </summary>
public sealed class GuiDialogAlloyCalculator : GuiDialog
{
    #region Constants
    private const string ModId = "vanillaexpanded";
    private const string DialogKey = "alloycalculator";
    private const double DialogWidth = 400;
    private const double SliderWidth = 280;
    private const double LabelWidth = 100;
    private const double RowHeight = 40;
    private const int DefaultTargetUnits = 100;
    #endregion

    #region Fields
    private List<AlloyRecipe> alloys = [];
    private AlloyRecipe? selectedAlloy;
    private readonly Dictionary<int, int> sliderValues = [];
    private int targetUnits = DefaultTargetUnits;
    private bool isAdjustingSliders;
    #endregion

    #region Properties
    public override string ToggleKeyCombinationCode => "ve.alloycalculator";
    public override double DrawOrder => 0.2;
    #endregion

    #region Constructor
    public GuiDialogAlloyCalculator(ICoreClientAPI capi) : base(capi)
    {
        LoadAlloys();
    }
    #endregion

    #region Initialization
    private void LoadAlloys()
    {
        alloys = capi.GetMetalAlloys()
            .Where(static a => a.Enabled && a.Ingredients.Length > 0)
            .OrderBy(static a => GetAlloyDisplayName(a))
            .ToList();
    }

    private static string GetAlloyDisplayName(AlloyRecipe alloy)
    {
        var outputCode = alloy.Output?.Code?.Path ?? "unknown";
        // Extract metal name from "ingot-bronze" format
        var metalName = outputCode.Contains('-') 
            ? outputCode[(outputCode.LastIndexOf('-') + 1)..] 
            : outputCode;
        return Lang.GetMatching($"material-{metalName}") ?? metalName;
    }

    private static string GetIngredientDisplayName(MetalAlloyIngredient ingredient)
    {
        var code = ingredient.Code?.Path ?? "unknown";
        var metalName = code.Contains('-') 
            ? code[(code.LastIndexOf('-') + 1)..] 
            : code;
        return Lang.GetMatching($"material-{metalName}") ?? metalName;
    }
    #endregion

    #region Dialog Composition
    private void ComposeDialog()
    {
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);

        // Calculate content height based on selected alloy
        var contentHeight = CalculateContentHeight();

        var composer = capi.Gui
            .CreateCompo(DialogKey, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get($"{ModId}:gui-alloycalculator-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds);

        var yOffset = 30.0; // Offset to account for title bar height

        // Alloy selection dropdown
        AddAlloyDropdown(composer, ref yOffset);

        // Target units input
        AddTargetUnitsInput(composer, ref yOffset);

        // Ingredient sliders (if an alloy is selected)
        if (selectedAlloy is not null)
        {
            AddIngredientSliders(composer, ref yOffset);
            AddResultsDisplay(composer, ref yOffset);
        }

        SingleComposer = composer.EndChildElements().Compose();

        // Initialize slider values after composition
        if (selectedAlloy is not null)
        {
            InitializeSliderValues();
            UpdateResultsDisplay();
        }
    }

    private double CalculateContentHeight()
    {
        var baseHeight = 120.0; // Title + dropdown + target units
        if (selectedAlloy is not null)
        {
            baseHeight += selectedAlloy.Ingredients.Length * RowHeight; // Sliders
            baseHeight += selectedAlloy.Ingredients.Length * 25; // Results
            baseHeight += 60; // Padding
        }
        return baseHeight;
    }

    private void AddAlloyDropdown(GuiComposer composer, ref double yOffset)
    {
        var labelBounds = ElementBounds.Fixed(0, yOffset, LabelWidth, 25);
        var dropdownBounds = ElementBounds.Fixed(LabelWidth + 10, yOffset, SliderWidth, 25);

        var alloyValues = alloys.Select((_, i) => i.ToString()).ToArray();
        var alloyNames = alloys.Select(GetAlloyDisplayName).ToArray();

        var selectedIndex = selectedAlloy is not null ? alloys.IndexOf(selectedAlloy) : 0;
        if (selectedIndex < 0) selectedIndex = 0;

        composer
            .AddStaticText(Lang.Get($"{ModId}:gui-alloycalculator-alloy"), CairoFont.WhiteSmallText(), labelBounds)
            .AddDropDown(alloyValues, alloyNames, selectedIndex, OnAlloySelected, dropdownBounds, "alloyDropdown");

        yOffset += 35;
    }

    private void AddTargetUnitsInput(GuiComposer composer, ref double yOffset)
    {
        var labelBounds = ElementBounds.Fixed(0, yOffset, LabelWidth, 25);
        var inputBounds = ElementBounds.Fixed(LabelWidth + 10, yOffset, 80, 25);

        composer
            .AddStaticText(Lang.Get($"{ModId}:gui-alloycalculator-targetunits"), CairoFont.WhiteSmallText(), labelBounds)
            .AddNumberInput(inputBounds, OnTargetUnitsChanged, CairoFont.WhiteDetailText(), "targetUnits");

        yOffset += 40;
    }

    private void AddIngredientSliders(GuiComposer composer, ref double yOffset)
    {
        if (selectedAlloy is null) return;

        // Add section header
        var headerBounds = ElementBounds.Fixed(0, yOffset, DialogWidth - 40, 25);
        composer.AddStaticText(Lang.Get($"{ModId}:gui-alloycalculator-ratios"), CairoFont.WhiteSmallText(), headerBounds);
        yOffset += 30;

        for (var i = 0; i < selectedAlloy.Ingredients.Length; i++)
        {
            var ingredient = selectedAlloy.Ingredients[i];
            var ingredientName = GetIngredientDisplayName(ingredient);

            var minPercent = (int)Math.Round(ingredient.MinRatio * 100);
            var maxPercent = (int)Math.Round(ingredient.MaxRatio * 100);

            var labelBounds = ElementBounds.Fixed(0, yOffset, LabelWidth, 30);
            var sliderBounds = ElementBounds.Fixed(LabelWidth + 10, yOffset + 5, SliderWidth, 20);

            var sliderKey = $"slider_{i}";
            var ingredientIndex = i; // Capture for closure

            composer
                .AddStaticText($"{ingredientName}:", CairoFont.WhiteSmallText(), labelBounds)
                .AddSlider(value => OnSliderChanged(ingredientIndex, value), sliderBounds, sliderKey);

            yOffset += RowHeight;
        }
    }

    private void AddResultsDisplay(GuiComposer composer, ref double yOffset)
    {
        if (selectedAlloy is null) return;

        yOffset += 10;

        // Add section header
        var headerBounds = ElementBounds.Fixed(0, yOffset, DialogWidth - 40, 25);
        composer.AddStaticText(Lang.Get($"{ModId}:gui-alloycalculator-required"), CairoFont.WhiteSmallText(), headerBounds);
        yOffset += 25;

        // Add result text for each ingredient
        for (var i = 0; i < selectedAlloy.Ingredients.Length; i++)
        {
            var resultBounds = ElementBounds.Fixed(10, yOffset, DialogWidth - 60, 20);
            composer.AddDynamicText("", CairoFont.WhiteDetailText(), resultBounds, $"result_{i}");
            yOffset += 22;
        }
    }
    #endregion

    #region Slider Logic
    private void InitializeSliderValues()
    {
        if (selectedAlloy is null || SingleComposer is null) return;

        sliderValues.Clear();

        // Initialize each slider to the midpoint of its valid range
        for (var i = 0; i < selectedAlloy.Ingredients.Length; i++)
        {
            var ingredient = selectedAlloy.Ingredients[i];
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
        if (isAdjustingSliders || selectedAlloy is null) return true;

        sliderValues[changedIndex] = newValue;
        NormalizeSliderValues(changedIndex);
        UpdateResultsDisplay();

        return true;
    }

    private void NormalizeSliderValues(int changedIndex)
    {
        if (selectedAlloy is null || SingleComposer is null) return;

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
                var ingredient = selectedAlloy.Ingredients[idx];
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
                    var ingredient = selectedAlloy.Ingredients[idx];
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
        if (selectedAlloy is null || SingleComposer is null) return;

        for (var i = 0; i < selectedAlloy.Ingredients.Length; i++)
        {
            var ingredient = selectedAlloy.Ingredients[i];
            var ingredientName = GetIngredientDisplayName(ingredient);
            var percent = sliderValues.TryGetValue(i, out var val) ? val : 0;
            var units = targetUnits * percent / 100.0;

            var resultText = SingleComposer.GetDynamicText($"result_{i}");
            resultText?.SetNewText($"{ingredientName}: {units:F1} {Lang.Get($"{ModId}:gui-alloycalculator-units")} ({percent}%)");
        }
    }
    #endregion

    #region Event Handlers
    private void OnAlloySelected(string code, bool selected)
    {
        if (!int.TryParse(code, out var index) || index < 0 || index >= alloys.Count)
        {
            return;
        }

        selectedAlloy = alloys[index];
        ComposeDialog();
    }

    private void OnTargetUnitsChanged(string value)
    {
        if (int.TryParse(value, out var units) && units > 0)
        {
            targetUnits = units;
            UpdateResultsDisplay();
        }
    }

    private void OnTitleBarClose()
    {
        TryClose();
    }
    #endregion

    #region Dialog Lifecycle
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        if (alloys.Count > 0 && selectedAlloy is null)
        {
            selectedAlloy = alloys[0];
        }

        ComposeDialog();

        // Set initial target units value
        var targetInput = SingleComposer?.GetNumberInput("targetUnits");
        targetInput?.SetValue(targetUnits.ToString());
    }

    public override bool TryOpen()
    {
        if (alloys.Count == 0)
        {
            LoadAlloys();
            if (alloys.Count == 0)
            {
                return false; // No alloys available
            }
        }

        return base.TryOpen();
    }
    #endregion
}
