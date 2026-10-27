using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Borrows registered instrument artwork from the engine texture cache.</summary>
internal static class WorkshopGaugeArtwork
{
    #region Public API
    /// <summary>Borrows engine-owned textures with consistent HUD filtering.</summary>
    public static LoadedTexture[] Load(HudOverlayPreparationContext context, params string[] names)
    {
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(context.Api);
            context.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                $"textures/hud/workshop-gauges/{names[index]}.png"), ref texture);
            HudOverlayTextureFiltering.Prepare(texture);
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }

    #endregion
}
