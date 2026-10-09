namespace VanillaExpanded.AutoStashing;

/// <summary>Distinguishes target availability, candidate existence and actual transfer success.</summary>
internal enum AutoStashOutcome
{
    Unavailable,
    NoCandidates,
    NoDestination,
    Success
}
