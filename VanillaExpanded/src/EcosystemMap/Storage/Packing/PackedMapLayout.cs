namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Original logical sample geometry carried with a packed channel, without retaining or fabricating source arrays.</summary>
internal sealed record PackedMapLayout(int Size, int TopLeftPadding, int BottomRightPadding);
