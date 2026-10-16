using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Loads registered instrument layers and converts their authored coordinates to native screen pixels.</summary>
internal static class WorkshopGaugeArtwork
{
    #region Public API
    /// <summary>Borrows engine-owned textures with consistent HUD filtering.</summary>
    public static LoadedTexture[] Load(HudOverlayPreparationContext context, string instrument, params string[] names)
    {
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(context.Api);
            context.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                $"textures/hud/workshop-gauges/{instrument}/{names[index]}.png"), ref texture);
            HudOverlayTextureFiltering.Prepare(texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }

    /// <summary>Maps a registered subregion through the complete instrument canvas, preserving transparent margins.</summary>
    public static RectangleF Map(RectangleF screen, SizeF canvas, RectangleF region) => new(
        screen.X + region.X * screen.Width / canvas.Width,
        screen.Y + region.Y * screen.Height / canvas.Height,
        region.Width * screen.Width / canvas.Width, region.Height * screen.Height / canvas.Height);
    #endregion
}
