namespace VanillaExpanded.AutoStashing;

/// <summary>Describes the completed operation without promising a planned quantity or durable storage.</summary>
internal readonly record struct AutoStashResult(int MovedQuantity, AutoStashOutcome Outcome, bool DirectMergeFailed = false);
