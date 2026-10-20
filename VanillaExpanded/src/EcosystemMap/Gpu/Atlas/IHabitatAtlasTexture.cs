using System;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Graphics-thread page storage; separates allocation accounting tests from native GL execution.</summary>
internal interface IHabitatAtlasTexture : IDisposable
{
    int Id { get; }
    /// <summary>Uploads an exact packed square into its reserved pixel rectangle.</summary>
    void Upload(Array values, int size, int x, int y);
}
