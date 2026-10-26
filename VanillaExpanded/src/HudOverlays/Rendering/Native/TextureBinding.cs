using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Pairs prepared artwork with explicit generated-versus-engine-owned storage.</summary>
internal readonly record struct TextureBinding(LoadedTexture Texture, bool Owned);
