using System.Threading;
using System.Threading.Tasks;
using VanillaExpanded.EcosystemMap.Storage;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>A bounded local-read job; it contains neither a species catalogue nor habitat comparison work.</summary>
internal sealed record GpuHabitatJob(EcosystemTileKey Key, CancellationTokenSource Scope,
    TaskCompletionSource<GpuHabitatReceipt?> Completion);
