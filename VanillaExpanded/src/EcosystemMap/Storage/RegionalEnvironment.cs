namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Independent regional inputs; null components represent unavailable data or omitted components in a partial update.</summary>
internal sealed record RegionalEnvironment(EcosystemRegionKey Key, EnvironmentalMap? Climate = null,
    EnvironmentalMap? Forest = null, EnvironmentalMap? Shrubs = null)
{
    internal const int Version = 1;
    internal EnvironmentalCompleteness Completeness =>
        (Climate != null ? EnvironmentalCompleteness.Climate : 0)
        | (Forest != null ? EnvironmentalCompleteness.Forest : 0)
        | (Shrubs != null ? EnvironmentalCompleteness.Shrubs : 0);
}
