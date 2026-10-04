using System;

using OpenTK.Graphics.OpenGL4;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Registers linked buffer-sampler locations omitted by the engine's shader source parser.</summary>
internal static class ItemSlotIndicatorBufferSampler
{
    #region Public API
    /// <summary>Completes engine uniform metadata once after linking, retaining normal engine uniform submission.</summary>
    internal static void Register(IShaderProgram program, string name)
    {
        // The engine's source regex recognizes 2D/cube samplers but omits samplerBuffer. Query the
        // actual linked location at preparation, never during a simulation step or indicator draw.
        if (program is not ShaderProgramBase engineProgram)
            throw new InvalidOperationException("Buffer samplers require an engine shader program.");
        int location = GL.GetUniformLocation(program.ProgramId, name);
        if (location < 0)
            throw new InvalidOperationException($"Indicator shader is missing linked buffer sampler '{name}'.");
        engineProgram.uniformLocations[name] = location;
    }
    #endregion
}
