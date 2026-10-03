using System.Numerics;

namespace VanillaExpanded.ItemSlotIndicators.Animation;

/// <summary>Contains shared periodic seconds in [0,64) and bounded rightward/upward camera-motion inputs.</summary>
internal readonly record struct ItemSlotIndicatorFrameSnapshot(float TimeSeconds, Vector2 Motion);
