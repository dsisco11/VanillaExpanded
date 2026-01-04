using System;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.UnstableParticles;

/// <summary>
/// Block behavior that spawns crumbling dust particles from the bottom face of unstable rock blocks.
/// Particle frequency increases with block instability.
/// </summary>
internal sealed class BlockBehaviorUnstableParticles : BlockBehavior
{
    #region Constants
    public static string RegistryId => "UnstableParticles";

    /// <summary>
    /// Minimum instability value (0-1) before particles start spawning.
    /// </summary>
    private const double MinInstabilityThreshold = 0.48;

    /// <summary>
    /// Maximum spawn chance per tick at maximum instability.
    /// </summary>
    private const float MaxSpawnChance = 0.02f;

    /// <summary>
    /// How often to refresh cached instability values (in seconds).
    /// </summary>
    private const double CacheRefreshInterval = 2.5;

    /// <summary>
    /// How long before a cache entry expires and is removed (in seconds).
    /// </summary>
    private const double CacheExpirationTime = 30.0;
    #endregion

    #region Static Cache
    /// <summary>
    /// Shared cache of instability values per block position.
    /// Key: BlockPos, Value: (instability value, last check time in milliseconds)
    /// </summary>
    private static readonly Dictionary<BlockPos, (double instability, long lastCheckMs)> instabilityCache = [];

    private static readonly object cacheLock = new();
    private static long lastCleanupTimeMs;
    #endregion

    #region Fields
    private ICoreClientAPI? clientApi;
    private SimpleParticleProperties? dustParticles;
    private static readonly Random random = new();
    #endregion

    #region Initialization
    public BlockBehaviorUnstableParticles(Block block) : base(block)
    {
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is ICoreClientAPI capi)
        {
            clientApi = capi;
            InitializeParticleProperties();
        }
    }

    private void InitializeParticleProperties()
    {
        dustParticles = new SimpleParticleProperties(
            minQuantity: 1,
            maxQuantity: 2,
            color: ColorUtil.ToRgba(200, 200, 200, 255), // sligntly darker than block colors
            minPos: new Vec3d(.1, 0, .1),
            maxPos: new Vec3d(.9, .9, .9),
            minVelocity: new Vec3f(-0.075f, 0f, -0.075f),
            maxVelocity: new Vec3f(0.075f, 0f, 0.075f),
            lifeLength: 2f,
            gravityEffect: 2f,
            minSize: 0.3f,
            maxSize: 0.7f,
            model: EnumParticleModel.Cube            
        )
        {
            ColorByBlock = block,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEARREDUCE, 0.5f),
            //OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEARREDUCE, 150),
            WithTerrainCollision = true,
            addLifeLength = 0.3f,
            Bounciness = 0.25f
        };
    }
    #endregion

    #region Particle Tick Handlers
    public override bool ShouldReceiveClientParticleTicks(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ref EnumHandling handling)
    {
        handling = EnumHandling.Handled;
        try
        {
            CleanupExpiredCache(world.ElapsedMilliseconds);
        }
        catch (Exception e)
        {
            world.Logger.Error("Error during instability cache cleanup: {0}", e.Message);
        }
        return true;
    }

    public override void OnAsyncClientParticleTick(IAsyncParticleManager manager, BlockPos pos, float windAffectednessAtPos, float secondsTicking)
    {
        if (dustParticles is null || clientApi is null)
        {
            return;
        }

        long currentTimeMs = clientApi.World.ElapsedMilliseconds;
        double instability = GetCachedInstability(pos, currentTimeMs);
        if (instability < MinInstabilityThreshold)
        {
            return;
        }

        // Scale spawn chance based on instability (0.2 to 1.0 maps to 0 to MaxSpawnChance)
        double normalizedInstability = (instability - MinInstabilityThreshold) / (1.0 - MinInstabilityThreshold);
        double scaledInstability = Math.Pow(normalizedInstability, 2.0); // Non-linear scaling curve so higher instability has more impact
        float spawnChance = (float)(normalizedInstability * MaxSpawnChance);

        if (random.NextDouble() < spawnChance)
        {
            SpawnDustParticle(manager, pos);
        }
    }

    private void SpawnDustParticle(IAsyncParticleManager manager, BlockPos pos)
    {
        if (dustParticles is null)
        {
            return;
        }

        // Spawn from random position on bottom face of block
        dustParticles.MinPos.Set(pos.DownCopy());
        dustParticles.ColorByBlock = block;

        manager.Spawn(dustParticles);
    }
    #endregion

    #region Instability Cache
    private double GetCachedInstability(BlockPos pos, long currentTimeMs)
    {
        long cacheRefreshMs = (long)(CacheRefreshInterval * 1000);

        lock (cacheLock)
        {
            if (instabilityCache.TryGetValue(pos, out var cached))
            {
                if (currentTimeMs - cached.lastCheckMs < cacheRefreshMs)
                {
                    return cached.instability;
                }
            }

            // Need to refresh the cached value
            double instability = CalculateInstability(pos);
            instabilityCache[pos] = (instability, currentTimeMs);
            return instability;
        }
    }

    private double CalculateInstability(BlockPos pos)
    {
        if (clientApi is null)
        {
            return 0;
        }

        Block blockAtPos = clientApi.World.BlockAccessor.GetBlock(pos);
        if (blockAtPos is null)
        {
            return 0;
        }

        // Find the UnstableRock behavior on this block
        BlockBehaviorUnstableRock? unstableRockBehavior = blockAtPos.GetBehavior<BlockBehaviorUnstableRock>();
        if (unstableRockBehavior is null)
        {
            return 0;
        }

        try
        {
            return unstableRockBehavior.getInstability(pos);
        }
        catch
        {
            // If getInstability throws for any reason, return 0
            return 0;
        }
    }

    private static void CleanupExpiredCache(long currentTimeMs)
    {
        long cacheExpirationMs = (long)(CacheExpirationTime * 1000);
        List<BlockPos>? keysToRemove = null;

        lock (cacheLock)
        {
            // Only cleanup if enough time has passed since last cleanup
            if (currentTimeMs - lastCleanupTimeMs < cacheExpirationMs / 2)
            {
                return;
            }
            lastCleanupTimeMs = currentTimeMs;

            foreach (var kvp in instabilityCache)
            {
                if (currentTimeMs - kvp.Value.lastCheckMs > cacheExpirationMs)
                {
                    keysToRemove ??= [];
                    keysToRemove.Add(kvp.Key);
                }
            }

            if (keysToRemove is not null)
            {
                foreach (var key in keysToRemove)
                {
                    instabilityCache.Remove(key);
                }
            }
        }
    }

    /// <summary>
    /// Clears the instability cache. Called when the system is disposed.
    /// </summary>
    internal static void ClearCache()
    {
        lock (cacheLock)
        {
            instabilityCache.Clear();
        }
    }
    #endregion
}
