using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Contains periodic seconds, bounded angular camera motion, and a bounded upward eye-bob rate.</summary>
internal readonly record struct ItemSlotIndicatorFrameSnapshot(float TimeSeconds, Vector2 Motion, float CameraBob = 0);
