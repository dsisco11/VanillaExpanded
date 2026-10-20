namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>A source and presentation-lifetime receipt; callers recheck current membership before graphics-thread publication.</summary>
internal sealed record GpuHabitatReceipt(GpuHabitatBundle Bundle, object Scope);
