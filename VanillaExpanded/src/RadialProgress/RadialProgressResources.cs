using System;
using System.Collections.Generic;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RadialProgress;

/// <summary>
/// Manages shared resources for radial progress bar rendering.
/// Uses reference counting to handle multiple renderer instances.
/// </summary>
internal static class RadialProgressResources
{
    #region Constants
    private const string ShaderName = "radial_progress";
    private const int StartOffsetPrecision = 100; // 0.01 precision for cache key
    #endregion

    #region Fields
    private static ICoreClientAPI? capi;
    private static MeshRef? quadMeshRef;
    private static readonly Dictionary<(int startOffsetKey, bool clockwise), RadialProgressShaderProgram> shaderCache = new();
    private static int referenceCount;
    private static readonly object lockObj = new();
    #endregion

    #region Properties
    /// <summary>
    /// The quad mesh reference for rendering.
    /// </summary>
    public static MeshRef? QuadMesh => quadMeshRef;

    /// <summary>
    /// Whether resources are currently initialized.
    /// </summary>
    public static bool IsInitialized => capi is not null && quadMeshRef is not null;
    #endregion

    #region Public Methods
    /// <summary>
    /// Initializes shared resources. Safe to call multiple times; uses reference counting.
    /// </summary>
    /// <param name="api">The client API.</param>
    /// <returns>True if initialization succeeded or resources were already initialized.</returns>
    public static bool Initialize(ICoreClientAPI api)
    {
        lock (lockObj)
        {
            // If already initialized, just increment reference count
            if (IsInitialized)
            {
                referenceCount++;
                return true;
            }

            // Not initialized - set up resources
            capi = api;

            try
            {
                // Create the quad mesh (vertices from -1 to 1, UV computed in shader)
                var quadData = QuadMeshUtil.GetQuad();
                quadMeshRef = api.Render.UploadMesh(quadData);

                // Only increment reference count after successful initialization
                referenceCount = 1;

                api.Logger.Debug("[VanillaExpanded] RadialProgressResources initialized successfully.");
                return true;
            }
            catch (Exception ex)
            {
                api.Logger.Error("[VanillaExpanded] Failed to initialize RadialProgressResources: {0}", ex.Message);
                Cleanup();
                return false;
            }
        }
    }

    /// <summary>
    /// Gets or creates a shader program for the specified start offset and direction.
    /// </summary>
    /// <param name="startOffset01">Start angle offset in [0,1] range (0 = +X axis, 0.25 = +Y axis).</param>
    /// <param name="clockwise">True for clockwise fill direction, false for counter-clockwise.</param>
    /// <returns>The shader program, or null if compilation failed.</returns>
    public static RadialProgressShaderProgram? GetOrCreateShader(float startOffset01, bool clockwise)
    {
        if (capi is null)
        {
            return null;
        }

        int startOffsetKey = (int)MathF.Round(startOffset01 * StartOffsetPrecision);
        var cacheKey = (startOffsetKey, clockwise);

        lock (lockObj)
        {
            if (shaderCache.TryGetValue(cacheKey, out var cachedShader))
            {
                return cachedShader;
            }

            var shader = CompileShader(startOffset01, clockwise, cacheKey);
            if (shader is not null)
            {
                shaderCache[cacheKey] = shader;
            }

            return shader;
        }
    }

    /// <summary>
    /// Reloads all cached shaders. Call this from the ReloadShader event.
    /// </summary>
    /// <returns>True if all shaders recompiled successfully.</returns>
    public static bool ReloadAllShaders()
    {
        if (capi is null)
        {
            return false;
        }

        lock (lockObj)
        {
            bool allSuccess = true;

            // Store keys to iterate (can't modify collection during enumeration)
            var keys = new List<(int startOffsetKey, bool clockwise)>(shaderCache.Keys);

            foreach (var key in keys)
            {
                // Dispose old shader
                if (shaderCache.TryGetValue(key, out var oldShader))
                {
                    oldShader.Dispose();
                }

                // Recompile with original parameters
                float startOffset01 = key.startOffsetKey / (float)StartOffsetPrecision;
                var newShader = CompileShader(startOffset01, key.clockwise, key);

                if (newShader is not null)
                {
                    shaderCache[key] = newShader;
                }
                else
                {
                    shaderCache.Remove(key);
                    allSuccess = false;
                }
            }

            return allSuccess;
        }
    }

    /// <summary>
    /// Called when a renderer is disposed. Resources are NOT deallocated here;
    /// they persist until <see cref="ForceCleanup"/> is called by the ModSystem.
    /// </summary>
    public static void Release()
    {
        // No-op: Resources persist until ModSystem disposal.
        // Reference counting is only used for debugging/tracking.
        lock (lockObj)
        {
            if (referenceCount > 0)
            {
                referenceCount--;
            }
        }
    }

    /// <summary>
    /// Forces immediate cleanup of all resources. Called by ModSystem.Dispose().
    /// </summary>
    public static void ForceCleanup()
    {
        lock (lockObj)
        {
            Cleanup();
            referenceCount = 0;
        }
    }
    #endregion

    #region Private Methods
    private static RadialProgressShaderProgram? CompileShader(float startOffset01, bool clockwise, (int, bool) cacheKey)
    {
        if (capi is null)
        {
            return null;
        }

        try
        {
            var prog = new RadialProgressShaderProgram();
            prog.VertexShader = (Vintagestory.Client.NoObf.Shader)capi.Shader.NewShader(EnumShaderType.VertexShader);
            prog.FragmentShader = (Vintagestory.Client.NoObf.Shader)capi.Shader.NewShader(EnumShaderType.FragmentShader);

            // Inject #defines for compile-time constants
            string prefixCode = string.Format(
                CultureInfo.InvariantCulture,
                "#define START_OFFSET {0:F6}\n#define CLOCKWISE {1}\n",
                startOffset01,
                clockwise ? 1 : 0
            );

            prog.FragmentShader.PrefixCode = prefixCode;

            // Use domain for asset loading
            prog.AssetDomain = "vanillaexpanded";

            // Register as file shader (loads from assets/vanillaexpanded/shaders/)
            capi.Shader.RegisterFileShaderProgram(ShaderName, prog);

            if (!prog.Compile())
            {
                capi.Logger.Error(
                    "[VanillaExpanded] Failed to compile radial progress shader (offset={0}, clockwise={1})",
                    startOffset01,
                    clockwise
                );
                prog.Dispose();
                return null;
            }

            capi.Logger.Debug(
                "[VanillaExpanded] Compiled radial progress shader variant (offset={0}, clockwise={1})",
                startOffset01,
                clockwise
            );

            return prog;
        }
        catch (Exception ex)
        {
            capi.Logger.Error("[VanillaExpanded] Exception compiling radial progress shader: {0}", ex.Message);
            return null;
        }
    }

    private static void Cleanup()
    {
        // Dispose all cached shaders
        foreach (var shader in shaderCache.Values)
        {
            shader.Dispose();
        }
        shaderCache.Clear();

        // Dispose mesh
        if (quadMeshRef is not null)
        {
            capi?.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi = null;
    }
    #endregion
}
