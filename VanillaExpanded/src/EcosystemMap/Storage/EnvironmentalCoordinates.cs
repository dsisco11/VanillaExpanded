namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Identifies a regional environmental record independently of map rendering and pixel storage.</summary>
internal readonly record struct EcosystemRegionKey(int X, int Z, int Dimension = 0);

/// <summary>Identifies one map-chunk elevation record, including its dimension.</summary>
internal readonly record struct EcosystemTileKey(int X, int Z, int Dimension = 0);

/// <summary>States which validated persistent inputs are present; missing components remain explicitly unavailable.</summary>
[System.Flags]
internal enum EnvironmentalCompleteness { None = 0, Climate = 1, Forest = 2, Shrubs = 4, Elevation = 8, Ocean = 16 }
