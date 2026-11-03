using System.Drawing;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;

namespace VanillaExpanded.HudOverlays.Rendering.Native;

/// <summary>Pairs prepared artwork with explicit generated-versus-engine-owned storage.</summary>
internal readonly record struct TextureBinding(LoadedTexture Texture, bool Owned);

/// <summary>Loads artwork with its required SVG pixel canvas, or an empty size for non-SVG resources.</summary>
internal delegate TextureBinding TextureLoader(HudOverlayPreparationContext context, string path, Size svgSize);
