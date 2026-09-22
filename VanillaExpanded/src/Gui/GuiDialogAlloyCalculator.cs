using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using VanillaExpanded.AlloyCalculator;
using VanillaExpanded.ModSystems;
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
    private const int DefaultTargetUnits = 100;
    private const double TitlebarHeight = 20;
    private const double SlotSize = 40;
    private const double ButtonHeight = 25;
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
    
    // Cached handbook data for filtering
    private ItemStack[]? handbookStacks;
    private List<ItemStack>? smeltingContainers;
    private List<ItemStack>? smeltingFuels;
    private int maxFuelTemperature;
    private AlloyDepositSystem? depositSystem;
    private string? pendingDepositRequestId;
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
                var combustProps = stack.Collectible.CombustibleProps;
                if (combustProps?.BurnDuration is not null || combustProps?.BurnTemperature is not null)
                {
                    smeltingFuels.Add(stack);
                }
            }
        }

        handbookStacks = stacks.ToArray();

        // Calculate max fuel temperature
        maxFuelTemperature = smeltingFuels
            .Where(static f => f.Collectible.CombustibleProps?.BurnTemperature is not null)
            .Select(static f => f.Collectible.CombustibleProps!.BurnTemperature)
            .DefaultIfEmpty(0)
            .Max();
    }
    #endregion

    #region Dialog Composition
    private void ComposeDialog()
    {
        // Calculate number of ingredient rows
        var ingredientCount = selectedIngredients.Length;
        bool showRatioControls = selectedOption is not null
            && AlloyCalculatorLogic.ShouldShowRatioControls(selectedOption);

        // Define content bounds - this establishes the size of our dialog content
        // Width: either slider row or slot row, whichever is wider
        var sliderRowWidth = showRatioControls ? LabelWidth + SliderWidth : 0;
        var slotRowWidth = ingredientCount * SlotSize;
        var controlsWidth = DropdownWidth + 10 + InputWidth;
        var contentWidth = Math.Max(controlsWidth, Math.Max(sliderRowWidth, slotRowWidth));
        
        // Height: titlebar + dropdown row + sliders + slot row + button row
        var contentHeight = TitlebarHeight + 30;
        if (ingredientCount > 0)
        {
            if (showRatioControls)
            {
                contentHeight += ingredientCount * RowHeight;
            }
            contentHeight += 15 + SlotSize; // gap + slot row
            contentHeight += 10 + ButtonHeight; // gap + button
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
            .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-targetunits-tooltip"), CairoFont.WhiteDetailText(), 250, inputBounds.FlatCopy(), "targetUnitsTooltip");

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

            // Add deposit button
            yOffset += (int)SlotSize + 18;
            var buttonBounds = ElementBounds
                .Fixed(0, yOffset, 80, ButtonHeight)
                .WithParent(contentBounds)
                .WithAlignment(EnumDialogArea.CenterFixed);
            composer
                .AddSmallButton(Lang.Get($"{Constants.ModId}:gui-alloycalculator-deposit"), OnDepositButtonClicked, buttonBounds, EnumButtonStyle.Normal, "depositButton")
                .AddHoverText(Lang.Get($"{Constants.ModId}:gui-alloycalculator-deposit-tooltip"), CairoFont.WhiteDetailText(), 250, buttonBounds.FlatCopy(), "depositTooltip");
        }

        SingleComposer = composer.EndChildElements().Compose();

        // Set target units value
        var targetInput = SingleComposer?.GetNumberInput("targetUnits");
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

        for (var i = 0; i < selectedIngredients.Length; i++)
        {
            var ingredient = selectedIngredients[i];
            var percent = sliderValues.TryGetValue(i, out var val) ? val : 0;
            var units = targetUnits * percent / 100.0;
            var nuggets = (int)Math.Ceiling(units / 5.0); // 1 nugget = 5 units, round up

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
    }

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

    private void OnTitleBarClose()
    {
        TryClose();
    }

    private bool OnDepositButtonClicked()
    {
        DepositIngredientsIntoCrucible();
        return true;
    }
    #endregion

    #region Deposit Logic
    /// <summary>
    /// Requests an atomic, server-authoritative deposit of the calculated ingredients.
    /// </summary>
    private void DepositIngredientsIntoCrucible()
    {
        if (pendingDepositRequestId is not null || selectedOption is null) return;
        BlockEntityFirepit? firepit = capi.World.BlockAccessor
            .GetBlockEntity<BlockEntityFirepit>(BlockEntityPosition);
        if (firepit?.Inventory is not InventorySmelting inventory || inventory.CookingSlots.Length == 0) return;

        var ingredients = new List<(string Code, int Amount)>();
        for (int index = 0; index < selectedIngredients.Length; index++)
        {
            if (!calculatedStacks.TryGetValue(index, out ItemStack? targetStack)
                || targetStack.StackSize <= 0)
            {
                return;
            }

            ingredients.Add((selectedIngredients[index].Code.ToString(), targetStack.StackSize));
        }

        ingredients.Sort(static (left, right) => right.Amount.CompareTo(left.Amount));
        int[] allocations = AlloyCalculatorLogic.AllocateSlotsProportionally(
            ingredients.Select(static ingredient => ingredient.Amount).ToArray(),
            inventory.CookingSlots.Length);
        var slotIndices = new List<int>();
        var slotIngredientCodes = new List<string>();
        var slotAmounts = new List<int>();
        int slotIndex = 0;

        for (int ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
        {
            (string code, int amount) = ingredients[ingredientIndex];
            int allocatedSlots = allocations[ingredientIndex];
            int itemsPerSlot = amount / allocatedSlots;
            int remainder = amount % allocatedSlots;

            for (int offset = 0; offset < allocatedSlots; offset++, slotIndex++)
            {
                int slotAmount = itemsPerSlot + (offset < remainder ? 1 : 0);
                if (slotAmount <= 0) continue;

                slotIndices.Add(slotIndex);
                slotIngredientCodes.Add(code);
                slotAmounts.Add(slotAmount);
            }
        }

        string requestId = Guid.NewGuid().ToString("N");
        var request = new Packet_RequestAlloyDeposit
        {
            RequestId = requestId,
            Position = BlockEntityPosition.Copy(),
            AlloyCode = selectedOption.OutputCode.ToString(),
            SlotIndices = [.. slotIndices],
            SlotIngredientCodes = [.. slotIngredientCodes],
            SlotAmounts = [.. slotAmounts]
        };

        depositSystem ??= capi.ModLoader.GetModSystem<AlloyDepositSystem>();
        if (depositSystem?.RequestDeposit(request) != true) return;

        pendingDepositRequestId = requestId;
        SetDepositButtonEnabled(false);
    }

    private void OnDepositCompleted(Packet_AlloyDepositResult result)
    {
        if (result.RequestId != pendingDepositRequestId) return;

        pendingDepositRequestId = null;
        SetDepositButtonEnabled(true);

        if (result.ResultCode != AlloyDepositResultCode.Success)
        {
            string resultKey = result.ResultCode switch
            {
                AlloyDepositResultCode.InventoryClosed => "inventory-closed",
                AlloyDepositResultCode.InvalidRecipe => "invalid-recipe",
                AlloyDepositResultCode.InsufficientItems => "insufficient-items",
                AlloyDepositResultCode.InsufficientSpace => "insufficient-space",
                AlloyDepositResultCode.TransferFailed => "transfer-failed",
                _ => "invalid-request"
            };
            capi.TriggerIngameError(
                this,
                $"alloy-deposit-{resultKey}",
                Lang.Get($"{Constants.ModId}:gui-alloycalculator-deposit-{resultKey}"));
        }
    }

    private void SetDepositButtonEnabled(bool enabled)
    {
        GuiElementTextButton? button = SingleComposer?.GetButton("depositButton");
        if (button is not null)
        {
            button.Enabled = enabled;
        }
    }
    #endregion

    #region Dialog Lifecycle
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();

        depositSystem = capi.ModLoader.GetModSystem<AlloyDepositSystem>();
        depositSystem.DepositCompleted += OnDepositCompleted;

        MetalDepositOption? detectedOption = DetectOptionFromCrucible();
        if (detectedOption is not null)
        {
            OnAlloySelected(depositOptions.IndexOf(detectedOption).ToString(), true);
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

    private MetalDepositOption? DetectOptionFromCrucible()
    {
        BlockEntityFirepit? firepit = capi.World.BlockAccessor
            .GetBlockEntity<BlockEntityFirepit>(BlockEntityPosition);
        if (firepit?.Inventory is not InventorySmelting inventory) return null;

        ItemStack[] contents = [.. inventory.CookingSlots
            .Where(static slot => !slot.Empty)
            .Select(static slot => slot.Itemstack)
            .OfType<ItemStack>()];
        return AlloyCalculatorLogic.FindOptionForContents(
            contents,
            depositOptions,
            capi.GetMetalAlloys());
    }

    public override void OnGuiClosed()
    {
        if (depositSystem is not null)
        {
            depositSystem.DepositCompleted -= OnDepositCompleted;
        }

        pendingDepositRequestId = null;
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
        base.OnRenderGUI(deltaTime);
        if (capi.Settings.Bool["immersiveMouseMode"])
        {
            // Adjust position to account for firepit dialog width in immersive mode (positions the calculator dialog to the right of the firepit dialog)
            SingleComposer.Bounds.absOffsetX = (SingleComposer.Bounds.OuterWidth / 2) + (FirepitDialogWidth / 2) - 5;
            SingleComposer.Bounds.absOffsetY = 0;
        }
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