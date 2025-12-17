using System;
using System.Collections.Generic;
using System.Linq;

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
    private const string ModId = "vanillaexpanded";
    private const string DialogKey = "alloycalculator";
    private const double SliderWidth = 130;
    private const double LabelWidth = 80;
    private const double AmountWidth = 60;
    private const double RowHeight = 28;
    private const double DropdownWidth = 150;
    private const double InputWidth = 70;
    private const int DefaultTargetUnits = 100;
    private const double TitlebarHeight = 20;
    private const double SlotSize = 40;
    #endregion

    #region Fields
    private readonly GuiDialog? firepitDialog;
    private List<AlloyRecipe> alloys = [];
    private AlloyRecipe? selectedAlloy;
    private readonly Dictionary<int, int> sliderValues = [];
    private readonly Dictionary<int, ItemStack> calculatedStacks = [];
    private int targetUnits = DefaultTargetUnits;
    private bool isAdjustingSliders;
    private DummyInventory? ingredientInventory;
    #endregion

    #region Properties
    public override double DrawOrder => 0.2;
    public override string ToggleKeyCombinationCode => string.Empty;
    protected override double FloatyDialogPosition => 0.5;
    protected override double FloatyDialogAlign => 1.0;

    /// <summary>
    /// Gets the calculated ingredient stacks for the current alloy configuration.
    /// Key is the ingredient index, value is the ItemStack with the calculated amount.
    /// </summary>
    public IReadOnlyDictionary<int, ItemStack> CalculatedIngredientStacks => calculatedStacks;
    #endregion

    #region Constructor
    public GuiDialogAlloyCalculator(ICoreClientAPI capi, BlockPos blockPos, GuiDialog firepitDialog) 
        : base(Lang.Get($"{ModId}:gui-alloycalculator-title"), blockPos, capi)
    {
        if (IsDuplicate) return;
        this.firepitDialog = firepitDialog;
        LoadAlloys();
    }
    #endregion

    #region Accessors
    public double FirepitDialogWidth => firepitDialog?.SingleComposer?.Bounds.OuterWidth ?? 0;
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
        // Calculate number of ingredient rows
        var ingredientCount = selectedAlloy?.Ingredients.Length ?? 0;

        // Define content bounds - this establishes the size of our dialog content
        // Width: either slider row or slot row, whichever is wider
        var sliderRowWidth = LabelWidth + SliderWidth + AmountWidth;
        var slotRowWidth = ingredientCount * SlotSize;
        var contentWidth = Math.Max(sliderRowWidth, slotRowWidth);
        
        // Height: titlebar + dropdown row + sliders + slot row
        var contentHeight = TitlebarHeight + 30;
        if (ingredientCount > 0)
        {
            contentHeight += 10 + (ingredientCount * RowHeight); // divider + sliders
            contentHeight += 15 + SlotSize; // gap + slot row
        }
        var contentBounds = ElementBounds.Fixed(0, 0, contentWidth, contentHeight);

        // Background bounds with padding, sized to fit children
        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(contentBounds);

        // Calculate offset to position right of firepit dialog
        var firepitWidth = FirepitDialogWidth;
        var dialogXOffset = (firepitWidth / 2);

        // Dialog bounds - positioned to the right of the firepit
        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(dialogXOffset, 0);

        // Build the UI - start below title bar
        var yOffset = TitlebarHeight;

        // Define element bounds
        var dropdownBounds = ElementBounds.Fixed(0, yOffset, DropdownWidth, 25);
        var inputBounds = ElementBounds.Fixed(DropdownWidth + 10, yOffset, InputWidth, 25);
        yOffset += 30;

        var alloyValues = alloys.Select(static (_, i) => i.ToString()).ToArray();
        var alloyNames = alloys.Select(GetAlloyDisplayName).ToArray();
        var selectedIndex = selectedAlloy is not null ? alloys.IndexOf(selectedAlloy) : 0;
        if (selectedIndex < 0) selectedIndex = 0;

        var composer = capi.Gui
            .CreateCompo($"{DialogKey}{BlockEntityPosition}", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get($"{ModId}:gui-alloycalculator-title"), OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddDropDown(alloyValues, alloyNames, selectedIndex, OnAlloySelected, dropdownBounds, "alloyDropdown")
            .AddNumberInput(inputBounds, OnTargetUnitsChanged, CairoFont.WhiteDetailText(), "targetUnits");

        // Add ingredient sliders if an alloy is selected
        if (selectedAlloy is not null && ingredientCount > 0)
        {
            // Create inventory for ingredient display slots
            ingredientInventory = new DummyInventory(capi, ingredientCount);

            // Add divider
            var dividerBounds = ElementBounds.Fixed(0, yOffset, contentWidth, 1);
            composer.AddInset(dividerBounds, 1, 0.5f);
            yOffset += 10;

            // Add sliders with amount labels
            for (var i = 0; i < ingredientCount; i++)
            {
                var ingredient = selectedAlloy.Ingredients[i];
                var ingredientName = GetIngredientDisplayName(ingredient);

                var labelBounds = ElementBounds.Fixed(0, yOffset, LabelWidth, RowHeight);
                var sliderBounds = ElementBounds.Fixed(LabelWidth, yOffset + 4, SliderWidth, 20);
                var amountBounds = ElementBounds.Fixed(LabelWidth + SliderWidth + 5, yOffset, AmountWidth, RowHeight);

                var sliderKey = $"slider_{i}";
                var amountKey = $"amount_{i}";
                var ingredientIndex = i;

                composer
                    .AddStaticText(ingredientName, CairoFont.WhiteSmallText(), labelBounds)
                    .AddSlider(value => OnSliderChanged(ingredientIndex, value), sliderBounds, sliderKey)
                    .AddDynamicText("", CairoFont.WhiteDetailText(), amountBounds, amountKey);

                yOffset += RowHeight;
            }

            // Add second divider before slots
            yOffset += 5;
            var divider2Bounds = ElementBounds.Fixed(0, yOffset, contentWidth, 1);
            composer.AddInset(divider2Bounds, 1, 0.5f);
            yOffset += 10;

            // Add item slots in a single row at the bottom
            var slotsWidth = ingredientCount * SlotSize;
            var slotsXOffset = (contentWidth - slotsWidth) / 2; // Center the slots
            var slotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, slotsXOffset, yOffset, ingredientCount, 1);
            var slotIndices = Enumerable.Range(0, ingredientCount).ToArray();
            composer.AddItemSlotGrid(ingredientInventory, null, ingredientCount, slotIndices, slotBounds, "ingredientSlots");
        }

        SingleComposer = composer.EndChildElements().Compose();

        // Set target units value
        var targetInput = SingleComposer?.GetNumberInput("targetUnits");
        targetInput?.SetValue(targetUnits.ToString());

        // Initialize slider values after composition
        if (selectedAlloy is not null)
        {
            InitializeSliderValues();
            UpdateResultsDisplay();
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
        if (selectedAlloy is null || SingleComposer is null || ingredientInventory is null) return;

        calculatedStacks.Clear();

        for (var i = 0; i < selectedAlloy.Ingredients.Length; i++)
        {
            var ingredient = selectedAlloy.Ingredients[i];
            var percent = sliderValues.TryGetValue(i, out var val) ? val : 0;
            var units = targetUnits * percent / 100.0;
            var nuggets = (int)Math.Ceiling(units / 5.0); // 1 nugget = 5 units, round up

            // Update amount text
            var amountText = SingleComposer.GetDynamicText($"amount_{i}");
            amountText?.SetNewText($"x{nuggets}");

            // Create ItemStack for metal bits with calculated amount
            if (nuggets > 0)
            {
                var stack = GetMetalBitStack(ingredient, nuggets);
                if (stack is not null)
                {
                    calculatedStacks[i] = stack;
                    ingredientInventory[i].Itemstack = stack;
                }
                else
                {
                    ingredientInventory[i].Itemstack = null;
                }
            }
            else
            {
                ingredientInventory[i].Itemstack = null;
            }

            ingredientInventory[i].MarkDirty();
        }
    }

    /// <summary>
    /// Gets a metal bit ItemStack for the given ingredient.
    /// </summary>
    private ItemStack? GetMetalBitStack(MetalAlloyIngredient ingredient, int stackSize)
    {
        // Extract metal name from ingredient code (e.g., "ingot-copper" -> "copper")
        var code = ingredient.Code?.Path;
        if (code is null) return null;

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
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);
        if (capi.Settings.Bool["immersiveMouseMode"])
        {
            SingleComposer.Bounds.absOffsetX = (SingleComposer.Bounds.OuterWidth / 2) + (FirepitDialogWidth / 2) - 5;
            SingleComposer.Bounds.absOffsetY = -(SingleComposer.Bounds.OuterHeight / 2) - TitlebarHeight;
        }
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