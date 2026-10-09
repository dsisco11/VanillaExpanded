using VanillaExpanded.AutoStashing;
using VanillaExpanded.RadialProgress;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Exposes production state read-only while keeping every transition in the actual gesture entry points.</summary>
internal sealed class GestureBlockBehavior : BlockBehaviorAutoStashable
{
    public EStashingState State => stashingState;
    public IRadialProgressBar? CurrentProgress => progressBar;

    #region Public API
    /// <summary>Creates the actual target-specific production behavior without pre-seeding gesture state.</summary>
    public GestureBlockBehavior(Block block, IProgressSystemProvider progressProvider)
        : base(block, progressProvider) { }
    #endregion
}
