using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Presentation;

/// <summary>Current explored chunk placement in the map's existing scissor and framebuffer.</summary>
internal sealed record HabitatMapTile(EcosystemTileKey Key, float X, float Y, float Width, float Height);
