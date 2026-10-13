using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.PlayerWetness;

/// <summary>Loads borrowed droplet outline and fill-mask assets through the engine texture cache.</summary>
internal static class PlayerWetnessIconTextures
{
    #region Public API
    /// <summary>Loads the aligned transparent PNG layers once per presentation binding.</summary>
    public static LoadedTexture[] Create(HudOverlayPreparationContext context)
    {
        string[] names = ["outline", "fill-mask"];
        var textures = new LoadedTexture[names.Length];
        for (int index = 0; index < names.Length; index++)
        {
            var texture = new LoadedTexture(context.Api);
            context.Api.Render.GetOrLoadTexture(new AssetLocation(Constants.ModId,
                "textures/hud/player-wetness/" + names[index] + ".png"), ref texture);
            // The cache owns GPU storage; these wrappers only borrow it for drawing.
            texture.IgnoreUndisposed = true;
            textures[index] = texture;
        }
        return textures;
    }
    #endregion
}
