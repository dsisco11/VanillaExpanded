namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>A view-valid upload receipt; callers recheck it before graphics-thread resource publication.</summary>
internal sealed record GpuHabitatReceipt(GpuHabitatBundle Bundle, object Scope);
