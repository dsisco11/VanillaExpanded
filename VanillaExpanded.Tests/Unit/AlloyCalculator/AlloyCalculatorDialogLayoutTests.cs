using VanillaExpanded.Gui;

using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

[Trait("Category", "Unit")]
[Collection(nameof(AlloyCalculatorDialogLayoutCollection))]
public sealed class AlloyCalculatorDialogLayoutTests : IDisposable
{
    private readonly float previousGuiScale = RuntimeEnv.GUIScale;

    public AlloyCalculatorDialogLayoutTests()
    {
        RuntimeEnv.GUIScale = 1.5f;
    }

    public void Dispose()
    {
        RuntimeEnv.GUIScale = previousGuiScale;
    }

    [Theory]
    [InlineData(false, false, 0, 38, 48, 64)]
    [InlineData(false, false, 30, 38, 48, 34)]
    [InlineData(true, false, 45, 0, 48, 19)]
    [InlineData(false, true, 0, 38, 0, 64)]
    [InlineData(true, true, 64, 0, 0, 0)]
    [InlineData(false, true, 80, 38, 0, -16)]
    public void OptionalSections_CollapseEmptySlotsAndUseMeasuredStatusHeight(
        bool showWarning, bool showFuelSlot, double statusHeight,
        double expectedWarningReduction, double expectedSlotReduction, double expectedStatusReduction)
    {
        var reductions = GuiDialogAlloyCalculator.GetOptionalSectionReductions(
            showWarning, showFuelSlot, statusHeight);

        Assert.Equal(expectedWarningReduction, reductions.WarningReduction);
        Assert.Equal(expectedSlotReduction, reductions.SlotReduction);
        Assert.Equal(expectedStatusReduction, reductions.StatusReduction);
        Assert.Equal(showFuelSlot ? 48 : 0, 48 - reductions.SlotReduction);
        Assert.Equal(statusHeight, 64 - reductions.StatusReduction);
    }

    [Theory]
    [InlineData(300, 550)]
    [InlineData(550, 300)]
    [InlineData(500, 462)]
    [InlineData(500, 436)]
    [InlineData(500, 398)]
    public void Resizing_MaintainsTopAndMovesBottom(double initialHeight, double resizedHeight)
    {
        ElementBounds screen = ElementBounds.Fixed(0, 0, 1920, 1080)
            .WithParent(ElementBounds.Empty);
        screen.CalcWorldBounds();
        ElementBounds firepit = ElementBounds.Fixed(50, 175, 300, 400).WithParent(screen);
        firepit.CalcWorldBounds();
        ElementBounds dialog = GuiDialogAlloyCalculator.CreateDialogBounds(firepit).WithParent(screen);
        ElementBounds content = ElementBounds.Fixed(0, 0, 270, initialHeight).WithParent(dialog);
        dialog.WithChildren(content);
        dialog.CalcWorldBounds();
        double top = dialog.absY;
        double bottom = top + dialog.OuterHeight;
        double left = dialog.absX;
        Assert.Equal(firepit.absY, top, 6);

        content.fixedHeight = resizedHeight;
        dialog.MarkDirtyRecursive();
        dialog.CalcWorldBounds();

        Assert.Equal(top, dialog.absY, 6);
        Assert.Equal(left, dialog.absX, 6);
        Assert.Equal((resizedHeight - initialHeight) * RuntimeEnv.GUIScale,
            dialog.absY + dialog.OuterHeight - bottom, 6);

        ElementBounds recomposed = GuiDialogAlloyCalculator.CreateDialogBounds(firepit).WithParent(screen);
        recomposed.WithChildren(ElementBounds.Fixed(0, 0, 270, resizedHeight).WithParent(recomposed));
        recomposed.CalcWorldBounds();
        Assert.Equal(top, recomposed.absY, 6);

        content.fixedHeight = initialHeight;
        dialog.MarkDirtyRecursive();
        dialog.CalcWorldBounds();
        Assert.Equal(top, dialog.absY, 6);
        Assert.Equal(bottom, dialog.absY + dialog.OuterHeight, 6);
    }

    [Theory]
    [InlineData(300, 550)]
    [InlineData(550, 300)]
    public void ImmersiveAlignment_FollowsFirepitScreenTopDespiteHeightAndAnchorChanges(
        double initialHeight, double resizedHeight)
    {
        ElementBounds screen = ElementBounds.Fixed(0, 0, 1920, 1080)
            .WithParent(ElementBounds.Empty);
        screen.CalcWorldBounds();
        ElementBounds firepit = ElementBounds.Fixed(50, 175, 300, 400).WithParent(screen);
        firepit.CalcWorldBounds();
        ElementBounds dialog = ElementBounds.Fixed(0, 0, 270, initialHeight).WithParent(screen);
        dialog.CalcWorldBounds();

        GuiDialogAlloyCalculator.AlignImmersiveDialog(dialog, firepit, projectedAnchorY: 800);
        Assert.Equal(firepit.renderY, dialog.renderY, 6);
        double top = dialog.renderY;
        double bottom = top + dialog.OuterHeight;

        dialog.fixedHeight = resizedHeight;
        dialog.MarkDirtyRecursive();
        dialog.CalcWorldBounds();
        GuiDialogAlloyCalculator.AlignImmersiveDialog(dialog, firepit, projectedAnchorY: 800);
        Assert.Equal(top, dialog.renderY, 6);
        Assert.Equal((resizedHeight - initialHeight) * RuntimeEnv.GUIScale,
            dialog.renderY + dialog.OuterHeight - bottom, 6);

        firepit.absFixedY = 120;
        firepit.absOffsetY = 17;
        firepit.renderOffsetY = 9;
        GuiDialogAlloyCalculator.AlignImmersiveDialog(dialog, firepit, projectedAnchorY: 650);
        Assert.Equal(firepit.renderY, dialog.renderY, 6);
        GuiDialogAlloyCalculator.AlignImmersiveDialog(dialog, firepit, projectedAnchorY: 650);
        Assert.Equal(firepit.renderY, dialog.renderY, 6);
    }
}

[CollectionDefinition(nameof(AlloyCalculatorDialogLayoutCollection), DisableParallelization = true)]
public sealed class AlloyCalculatorDialogLayoutCollection;
