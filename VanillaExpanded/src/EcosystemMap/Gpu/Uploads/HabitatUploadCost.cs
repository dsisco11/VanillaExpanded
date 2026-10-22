namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Separates CPU-array transfer bytes from new GPU page storage and their native image operations.</summary>
internal readonly record struct HabitatUploadCost(long TransferBytes, long AllocationBytes, int Transfers, int Allocations)
{
    internal long TotalBytes => checked(TransferBytes + AllocationBytes);
    internal int Operations => checked(Transfers + Allocations);
    /// <summary>Combines the regional and terrain work required to make one receipt drawable.</summary>
    public static HabitatUploadCost operator +(HabitatUploadCost a, HabitatUploadCost b) => new(
        checked(a.TransferBytes + b.TransferBytes), checked(a.AllocationBytes + b.AllocationBytes),
        checked(a.Transfers + b.Transfers), checked(a.Allocations + b.Allocations));
}
