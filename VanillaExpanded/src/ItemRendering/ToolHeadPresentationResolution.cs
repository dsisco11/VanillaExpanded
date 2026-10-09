namespace VanillaExpanded.ItemRendering;

/// <summary>Identifies whether dedicated presentation is available or the existing draw must be used.</summary>
internal enum ToolHeadPresentationStatus
{
    Missing,
    Invalid,
    Valid
}

/// <summary>Carries validated settings only when the dedicated asset configuration is usable.</summary>
internal readonly record struct ToolHeadPresentationResolution(
    ToolHeadPresentationStatus Status, ToolHeadPresentationProperties? Properties);
