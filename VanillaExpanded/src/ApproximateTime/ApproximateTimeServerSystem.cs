using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Maintains server-owned sky-exposure age independently of client HUD visibility.</summary>
internal sealed class ApproximateTimeServerSystem : ModSystem
{
    private ICoreServerAPI? api;
    private long? tickId;
    private double? lastHours;

    #region Public API
    /// <summary>Loads persistent observation tracking on servers only.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;
    /// <summary>Registers one bounded skylight sampling pass for online players.</summary>
    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        // The server calendar is not initialized during mod startup; the first valid tick sets the baseline.
        lastHours = null;
        tickId = api.Event.RegisterGameTickListener(OnTick, 1000);
    }
    /// <summary>Unregisters tracking without deleting persisted player attributes.</summary>
    public override void Dispose()
    {
        if (api != null && tickId.HasValue) api.Event.UnregisterGameTickListener(tickId.Value);
        tickId = null;
        lastHours = null;
        api = null;
        base.Dispose();
    }
    #endregion
    #region Private
    /// <summary>Advances each living online player's saved counter using elapsed calendar hours.</summary>
    private void OnTick(float deltaTime)
    {
        if (api == null) return;
        var calendar = api.World.Calendar;
        if (calendar == null) return;
        double hours = calendar.TotalHours;
        if (!double.IsFinite(hours)) return;
        // A reversed calendar contributes no elapsed exposure; saved uncertainty remains intact.
        double elapsed = Math.Max(0, hours - (lastHours ?? hours));
        lastHours = hours;
        foreach (var player in api.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity?.Alive != true) continue;
            // Any local skylight is a valid calibration cue, including nighttime sky exposure.
            bool sunlight = api.World.BlockAccessor.GetLightLevel(entity.Pos.AsBlockPos, EnumLightLevelType.OnlySunLight) > 0;
            ApproximateTimeState.Update(entity, elapsed, sunlight);
        }
    }
    #endregion
}
