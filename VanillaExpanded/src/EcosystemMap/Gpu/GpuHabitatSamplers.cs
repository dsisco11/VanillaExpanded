using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.EcosystemMap.Gpu;

/// <summary>Completes linked sampler metadata, including integer declarations omitted by the native parser.</summary>
internal static class GpuHabitatSamplers
{
    internal const int Count = 2;
    private static readonly string[] names = { "regionalMap", "terrainMap" };

    #region Public API
    /// <summary>Registers actual linked sampler locations once after compilation, retaining normal engine texture binding.</summary>
    internal static void Register(IShaderProgram program, Func<string, int>? locate = null)
    {
        if (program is not ShaderProgramBase engine)
            throw new NotSupportedException("Ecosystem integer samplers require an engine shader program.");
        locate ??= name => GL.GetUniformLocation(program.ProgramId, name);
        var locations = new Dictionary<string, int>();
        foreach (var name in names)
        {
            // Query the linked program rather than assuming integer sampler declarations appear in the source parser's dictionary.
            int location = locate(name);
            if (location < 0) throw new InvalidOperationException($"Ecosystem shader is missing linked integer sampler '{name}'.");
            locations.Add(name, location);
        }
        foreach (var entry in locations) engine.uniformLocations[entry.Key] = entry.Value;
    }
    #endregion
}
