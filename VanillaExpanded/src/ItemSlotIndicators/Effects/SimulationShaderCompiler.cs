using System;

using OpenTK.Graphics.OpenGL4;

using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ItemSlotIndicators.Effects;

/// <summary>Links indicator simulation programs with feedback declarations without modifying the engine linker.</summary>
internal static class SimulationShaderCompiler
{
    #region Public API
    /// <summary>Reuses engine stage compilation, owns program linking, and discovers uniforms from the final executable.</summary>
    /// <remarks>Failure disposes the program and its stages; callers must create a fresh instance before retrying.</remarks>
    internal static bool Compile(ShaderProgram program, string[] feedbackVaryings)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(feedbackVaryings);
        if (program.Disposed || program.ProgramId != 0)
            throw new InvalidOperationException("Simulation compilation requires a fresh shader program.");
        if (program.VertexShader is null || program.FragmentShader is null || program.GeometryShader is not null)
            throw new InvalidOperationException("Simulation compilation requires vertex and fragment stages only.");
        if (feedbackVaryings.Length == 0)
            throw new ArgumentException("Simulation compilation requires feedback outputs.", nameof(feedbackVaryings));

        try
        {
            // File registration has already supplied source, includes, and prefixes. Keep the engine's
            // version checks and stage diagnostics, but do not enter its indivisible create/link method.
            program.VertexShader.EnsureVersionSupported();
            program.FragmentShader.EnsureVersionSupported();
            if (!program.VertexShader.Compile() || !program.FragmentShader.Compile())
            {
                DisposeFailedProgram(program);
                return false;
            }

            program.ProgramId = GL.CreateProgram();
            if (program.ProgramId == 0) throw new InvalidOperationException("Could not create the simulation shader program.");
            GL.AttachShader(program.ProgramId, program.VertexShader.ShaderId);
            GL.AttachShader(program.ProgramId, program.FragmentShader.ShaderId);
            foreach (var attribute in program.attributes)
                GL.BindAttribLocation(program.ProgramId, attribute.Key, attribute.Value);
            GL.TransformFeedbackVaryings(program.ProgramId, feedbackVaryings.Length, feedbackVaryings,
                TransformFeedbackMode.InterleavedAttribs);
            GL.LinkProgram(program.ProgramId);
            GL.GetProgram(program.ProgramId, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0)
            {
                ScreenManager.Platform.Logger.Error("[VanillaExpanded] Link error in simulation shader {0}: {1}",
                    program.PassName, GL.GetProgramInfoLog(program.ProgramId));
                DisposeFailedProgram(program);
                return false;
            }

            // Query the linked executable rather than the engine's source regex, which omits
            // samplerBuffer. Locations are cached once, before typed contract validation or drawing.
            program.uniformLocations.Clear();
            GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveUniforms, out int count);
            for (int index = 0; index < count; index++)
            {
                string name = GL.GetActiveUniform(program.ProgramId, index, out _, out _);
                program.uniformLocations[name] = GL.GetUniformLocation(program.ProgramId, name);
            }
            return true;
        }
        catch
        {
            DisposeFailedProgram(program);
            throw;
        }
    }
    #endregion

    #region Private
    /// <summary>Releases even partially compiled or unattached stages before invoking engine program disposal.</summary>
    private static void DisposeFailedProgram(ShaderProgram program)
    {
        if (program.Disposed) return;
        // Engine Dispose assumes all stage handles are attached. A stage/compiler exception can
        // occur earlier, so delete our stage handles explicitly and suppress that detach path.
        if (program.VertexShader is { ShaderId: > 0 } vertex)
        {
            GL.DeleteShader(vertex.ShaderId);
            vertex.ShaderId = 0;
        }
        if (program.FragmentShader is { ShaderId: > 0 } fragment)
        {
            GL.DeleteShader(fragment.ShaderId);
            fragment.ShaderId = 0;
        }
        program.VertexShader = null;
        program.FragmentShader = null;
        program.uniformLocations.Clear();
        program.Dispose();
        program.ProgramId = 0;
    }
    #endregion
}
