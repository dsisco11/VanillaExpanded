using System;
using Cairo;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;

namespace VanillaExpanded.RecipeBook;

internal sealed class GuiElementRecipeBookBackground(ICoreClientAPI api, ElementBounds bounds) : GuiElement(api, bounds)
{
    private LoadedTexture shadow = new(api);
    private bool disposed;

    public override bool IsPositionInside(int x, int y) => false;

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        int radius = GuiControlDrawing.Pixels(RecipeBookVisualMetrics.ShadowRadius, scaled(1));
        int offset = GuiControlDrawing.Pixels(RecipeBookVisualMetrics.ShadowOffset, scaled(1));
        using (var image = new ImageSurface(Format.Argb32, Bounds.OuterWidthInt + 2 * radius,
            Bounds.OuterHeightInt + 2 * radius + offset))
        using (var context = new Context(image))
        {
            context.SetSourceRGBA(0, 0, 0, RecipeBookVisualMetrics.ShadowOpacity);
            context.Rectangle(radius, radius + offset, Bounds.OuterWidth, Bounds.OuterHeight);
            context.Fill();
            image.BlurFull(radius);
            generateTexture(image, ref shadow, linearMag: false);
        }
        GuiControlDrawing.Frame(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight,
            GuiControlPalette.DialogBottom, GuiControlPalette.DialogOutline, scaled(1));
        int stroke = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.Outline, scaled(1)));
        GuiControlDrawing.Gradient(ctx, Bounds.drawX + stroke, Bounds.drawY + stroke,
            Bounds.OuterWidth - 2 * stroke, Bounds.OuterHeight - 2 * stroke,
            GuiControlPalette.DialogTop, GuiControlPalette.DialogBottom);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        double radius = scaled(RecipeBookVisualMetrics.ShadowRadius);
        api.Render.Render2DTexturePremultipliedAlpha(shadow.TextureId, Bounds.renderX - radius,
            Bounds.renderY - radius, shadow.Width, shadow.Height, 49);
    }

    internal static void DrawList(Context ctx, ImageSurface surface, ElementBounds bounds) =>
        GuiControlDrawing.Frame(ctx, bounds.drawX, bounds.drawY, bounds.OuterWidth, bounds.OuterHeight,
            GuiControlPalette.ListFill, GuiControlPalette.ListOutline,
            scaled(RecipeBookVisualMetrics.ListOutline));

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        shadow.Dispose();
        base.Dispose();
    }
}

internal sealed class GuiElementRecipeBookTitleBar(ICoreClientAPI api, string text, GuiComposer composer,
    Action closed, ElementBounds bounds)
    : GuiElementDialogTitleBar(api, text, composer, closed,
        CairoFont.WhiteSmallText().WithFontSize(RecipeBookVisualMetrics.TitleFontSize).WithWeight(FontWeight.Bold)
            .WithColor(GuiControlPalette.Color(GuiControlPalette.Text)), bounds)
{
    public override void ComposeTextElements(Context ctx, ImageSurface surface)
    {
        // Initialize native movement, close hit regions and hover resources without its embossed chrome.
        using (var scratch = new ImageSurface(Format.Argb32, surface.Width, surface.Height))
        using (var native = new Context(scratch))
            base.ComposeTextElements(native, scratch);
        int stroke = Math.Max(1, GuiControlDrawing.Pixels(GuiControlMetrics.Outline, scaled(1)));
        GuiControlDrawing.Frame(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight,
            GuiControlPalette.TitleBottom, GuiControlPalette.DialogOutline, scaled(1));
        GuiControlDrawing.Gradient(ctx, Bounds.drawX + stroke, Bounds.drawY + stroke,
            Bounds.OuterWidth - 2 * stroke, Bounds.OuterHeight - 2 * stroke,
            GuiControlPalette.TitleTop, GuiControlPalette.TitleBottom);
        Font.SetupContext(ctx);
        DrawTextLineAt(ctx, text, scaled(GuiStyle.ElementToDialogPadding),
            (Bounds.InnerHeight - Font.GetFontExtents().Height) / 2);
        double cross = scaled(unscaledCloseIconSize);
        double menu = scaled(unscaledCloseIconSize + RecipeBookVisualMetrics.NativeMenuExpansion);
        ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.Text));
        api.Gui.Icons.DrawCross(ctx, Bounds.drawX + Bounds.OuterWidth - cross
            - scaled(RecipeBookVisualMetrics.NativeCloseRightInset),
            Bounds.drawY + scaled(RecipeBookVisualMetrics.NativeCloseTop),
            scaled(RecipeBookVisualMetrics.NativeCrossStroke), cross);
        api.Gui.Icons.Drawmenuicon_svg(ctx, (int)(Bounds.drawX + Bounds.OuterWidth - cross - menu
            - scaled(RecipeBookVisualMetrics.NativeTitleRightSpacing)),
            (int)(Bounds.drawY + scaled(RecipeBookVisualMetrics.NativeMenuTop)), (int)menu, (int)menu,
            GuiControlPalette.Color(GuiControlPalette.Text));
        ctx.SetSourceRGBA(GuiControlPalette.Color(GuiControlPalette.TitleSeparator));
        ctx.Rectangle(Bounds.drawX + stroke, Bounds.drawY + Bounds.OuterHeight - stroke,
            Bounds.OuterWidth - 2 * stroke, stroke);
        ctx.Fill();
    }
}
