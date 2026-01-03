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
    private const double MinInstabilityThreshold = 0.2;

    /// <summary>
    /// Maximum spawn chance per tick at maximum instability.
    /// </summary>
    private const float MaxSpawnChance = 0.08f;

    /// <summary>
    /// How often to refresh cached instability values (in seconds).
    /// </summary>
    private const double CacheRefreshInterval = 2.5;

    /// <summary>
    /// How long before a cache entry expires and is removed (in seconds).
    /// </summary>
    private const double CacheExpirationTime = 30.0;

    /// <summary>
    /// How often to run cache cleanup (in ticks, approximately).
    /// </summary>
    private const int CacheCleanupInterval = 500;
    #endregion

    #region Static Cache
    /// <summary>
    /// Shared cache of instability values per block position.
    /// Key: BlockPos, Value: (instability value, last check time in milliseconds)
    /// </summary>
    private static readonly Dictionary<BlockPos, (double instability, long lastCheckMs)> instabilityCache = [];

    private static int cleanupCounter;
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
            color: ColorUtil.ToRgba(180, 120, 100, 80), // Brownish-gray dust
            minPos: new Vec3d(),
            maxPos: new Vec3d(),
            minVelocity: new Vec3f(-0.05f, -0.15f, -0.05f),
            maxVelocity: new Vec3f(0.05f, -0.05f, 0.05f),
            lifeLength: 1.2f,
            gravityEffect: 0.4f,
            minSize: 0.05f,
            maxSize: 0.15f,
            model: EnumParticleModel.Cube
        )
        {
            ColorByBlock = block,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEARREDUCE, 0.3f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEARREDUCE, 150)
        };
    }
    #endregion

    #region Particle Tick Handlers
    public override bool ShouldReceiveClientParticleTicks(IWorldAccessor world, IPlayer byPlayer, BlockPos pos, ref EnumHandling handling)
    {
        handling = EnumHandling.PassThrough;
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
        float spawnChance = (float)(normalizedInstability * MaxSpawnChance);

        if (random.NextDouble() < spawnChance)
        {
            SpawnDustParticle(manager, pos);
        }

        // Periodic cache cleanup
        cleanupCounter++;
        if (cleanupCounter >= CacheCleanupInterval)
        {
            cleanupCounter = 0;
            CleanupExpiredCache(currentTimeMs);
        }
    }

    private void SpawnDustParticle(IAsyncParticleManager manager, BlockPos pos)
    {
        if (dustParticles is null)
        {
            return;
        }

        // Spawn from random position on bottom face of block
        double offsetX = random.NextDouble() * 0.8 + 0.1; // 0.1 to 0.9 within block
        double offsetZ = random.NextDouble() * 0.8 + 0.1;

        dustParticles.MinPos.Set(pos.X + offsetX, pos.Y, pos.Z + offsetZ);
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
            cleanupCounter = 0;
        }
    }
    #endregion
}
