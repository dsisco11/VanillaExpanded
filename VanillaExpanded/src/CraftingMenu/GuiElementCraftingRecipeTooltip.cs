using System;
using Cairo;
using Vintagestory.API.Client;

namespace VanillaExpanded.CraftingMenu;

internal sealed class GuiElementCraftingRecipeTooltip(ICoreClientAPI capi)
    : GuiElement(capi, ElementBounds.Fixed(0, 0, 0, 0))
{
    private CraftingRecipeEntry? recipe;
    private string? unsupportedReason;
    private bool visible;
    private bool disposed;
    internal GuiComposer? Popup { get; private set; }
    public override double DrawOrder => 0.9;

    internal void Inspect(CraftingRecipeSelection? selection)
    {
        visible = selection != null;
        if (selection == null) return;
        string? reason = selection.Evaluation.UnsupportedReason;
        if (ReferenceEquals(recipe, selection.Recipe) && unsupportedReason == reason && Popup != null) return;
        Popup?.Dispose();
        Popup = null;
        double width = Math.Min(300, api.Gui.WindowBounds.InnerWidth / scaled(1) - 16);
        var popupBounds = ElementBounds.Fixed(0, 0, width, 1).WithFixedPadding(8).WithEmptyParent();
        var title = new GuiElementRichtext(api, [CraftingMenuTooltip.CreateTitle(api, selection)],
            ElementBounds.Fixed(0, 0, width, 1).WithParent(popupBounds));
        title.BeforeCalcBounds();
        double y = title.Bounds.fixedHeight + 6;
        GuiElementRichtext? description = null;
        double? dividerY = null;
        var descriptionComponents = CraftingMenuTooltip.CreateDescription(api, selection);
        if (descriptionComponents.Length > 0)
        {
            var panelBounds = ElementBounds.Fixed(0, y, width - 6, 1)
                .WithFixedPadding(3).WithParent(popupBounds);
            description = new GuiElementRichtext(api, descriptionComponents,
                ElementBounds.Fixed(0, 0, width - 6, 1).WithParent(panelBounds));
            description.BeforeCalcBounds();
            panelBounds.fixedHeight = description.Bounds.fixedHeight;
            panelBounds.MarkDirtyRecursive();
            panelBounds.CalcWorldBounds();
            y += panelBounds.OuterHeight / scaled(1) + 6;
            if (selection.Recipe.Recipe.ResolvedIngredients != null)
            {
                dividerY = y;
                y += 7;
            }
        }
        var text = new GuiElementRichtext(api, CraftingMenuTooltip.CreateRecipe(api, selection, width),
            ElementBounds.Fixed(0, y, width, 1).WithParent(popupBounds));
        text.BeforeCalcBounds();
        popupBounds.fixedHeight = y + text.Bounds.fixedHeight;
        popupBounds.MarkDirtyRecursive();
        var backgroundBounds = ElementBounds.Fill
            .WithFixedAlignmentOffset(-popupBounds.fixedPaddingX, -popupBounds.fixedPaddingY);
        Popup = api.Gui.CreateCompo("vanillaexpanded-crafting-tooltip", popupBounds)
            .AddStaticElement(new GuiElementCustomDraw(api, backgroundBounds, DrawItemTooltipBackground), "background");
        Popup.AddInteractiveElement(title, "title");
        if (description != null)
            Popup.BeginChildElements(description.Bounds.ParentBounds)
                .AddInteractiveElement(description, "description")
                .EndChildElements();
        if (dividerY != null)
            Popup.AddStaticElement(new GuiElementInset(api,
                ElementBounds.Fixed(0, dividerY.Value, width, 1).WithParent(popupBounds),
                1, 0.5f), "divider");
        Popup.AddInteractiveElement(text, "recipe").Compose();
        recipe = selection.Recipe;
        unsupportedReason = reason;
    }

    private static void DrawItemTooltipBackground(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        // GuiElementItemstackInfo has no reusable background renderer; retain its native frame styling here.
        var color = GuiStyle.DialogStrongBgColor;
        ctx.SetSourceRGBA(color);
        RoundRectangle(ctx, bounds.bgDrawX, bounds.bgDrawY, bounds.OuterWidth, bounds.OuterHeight, GuiStyle.DialogBGRadius);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(GuiStyle.DialogLightBgColor[0] * 1.4, color[1] * 1.4, color[2] * 1.4, 1);
        ctx.LineWidth = 3 * 1.75;
        ctx.StrokePreserve();
        surface.BlurFull(8.2);
        ctx.SetSourceRGBA(color[0] / 2, color[1] / 2, color[2] / 2, color[3]);
        ctx.Stroke();
    }

    public override bool IsPositionInside(int posX, int posY) => false;

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!visible || Popup == null) return;
        var window = api.Gui.WindowBounds;
        var bounds = Popup.Bounds;
        bounds.fixedX = Math.Clamp(api.Input.MouseX + scaled(10), window.absX,
            Math.Max(window.absX, window.absX + window.InnerWidth - bounds.OuterWidth)) / scaled(1);
        bounds.fixedY = Math.Clamp(api.Input.MouseY + scaled(15), window.absY,
            Math.Max(window.absY, window.absY + window.InnerHeight - bounds.OuterHeight)) / scaled(1);
        bounds.MarkDirtyRecursive();
        bounds.CalcWorldBounds();
        // Native recipe cells use a fixed local Z, so lift the entire popup together.
        api.Render.GlPushMatrix();
        try
        {
            api.Render.GlTranslate(0, 0, 1000);
            Popup.Render(deltaTime);
        }
        finally { api.Render.GlPopMatrix(); }
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Popup?.Dispose();
        Popup = null;
        base.Dispose();
    }
}
