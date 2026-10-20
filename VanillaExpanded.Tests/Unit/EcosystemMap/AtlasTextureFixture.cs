using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Records atlas resource ownership and exact partial transfers without a graphics context.</summary>
internal sealed class AtlasTextureFixture : IHabitatAtlasTexture
{
    internal static int NextId;
    public int Id { get; } = Interlocked.Increment(ref NextId);
    internal bool Disposed { get; private set; }
    internal List<(Array Values, int Size, int X, int Y)> Uploads { get; } = new();
    /// <summary>Records the same packed buffer passed through the production atlas owner.</summary>
    public void Upload(Array values, int size, int x, int y)
    {
        Assert.False(Disposed);
        Uploads.Add((values, size, x, y));
    }
    /// <summary>Records page release and rejects double disposal.</summary>
    public void Dispose() { Assert.False(Disposed); Disposed = true; }
}
