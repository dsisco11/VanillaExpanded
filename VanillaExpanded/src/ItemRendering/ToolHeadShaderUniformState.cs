using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;

namespace VanillaExpanded.ItemRendering;

/// <summary>Snapshots shared GUI uniform values for exact nested and exceptional draw restoration.</summary>
internal sealed class ToolHeadShaderUniformState : IDisposable
{
    private readonly List<(string Name, bool Integer, float[] Values)> saved = new();
    private readonly Action<string, bool, float[]> restore;
    private bool disposed;

    #region Public API
    /// <summary>Reads only uniforms used by GUI item drawing and lifecycle preparation.</summary>
    internal ToolHeadShaderUniformState(IShaderProgram shader, Func<string, int, bool, float[]> read,
        Action<string, bool, float[]> restore)
    {
        this.restore = restore;
        foreach (string name in new[] { "normalShaded", "applyModelMat", "applyColor", "extraGlow", "tempGlowMode",
            "applyAnimation", "darkEdges", "transparentCenter", "tex2d", "tex2dOverlay" }) Save(shader, read, name, 1, true);
        foreach (string name in new[] { "noTexture", "alphaTest", "damageEffect", "overlayOpacity", "sepiaLevel" }) Save(shader, read, name, 1, false);
        foreach (string name in new[] { "rgbaIn", "rgbaGlowIn" }) Save(shader, read, name, 4, false);
        foreach (string name in new[] { "overlayTextureSize", "baseTextureSize", "baseUvOrigin" }) Save(shader, read, name, 2, false);
        Save(shader, read, "lightPosition", 3, false);
        foreach (string name in new[] { "modelMatrix", "modelViewMatrix", "projectionMatrix" }) Save(shader, read, name, 16, false);

    }

    /// <summary>Captures actual GL values; the engine keeps locations but no uniform-value cache.</summary>
    internal static ToolHeadShaderUniformState Capture(IRenderAPI render, IShaderProgram shader)
    {
        return new(shader, (name, count, integer) =>
        {
            int location = render.GetUniformLocation(shader.ProgramId, name);
            var values = new float[count];
            if (location < 0) return values; // Engine metadata also includes uniforms optimized out by the linker.
            if (integer)
            {
                var ints = new int[count];
                GL.GetUniform(shader.ProgramId, location, ints);
                for (int i = 0; i < count; i++) values[i] = ints[i];
            }
            else GL.GetUniform(shader.ProgramId, location, values);
            return values;
        }, (name, integer, values) =>
        {
            int location = render.GetUniformLocation(shader.ProgramId, name);
            if (location < 0) return;
            if (integer) GL.Uniform1(location, (int)values[0]);
            else if (values.Length == 16) GL.UniformMatrix4(location, 1, false, values);
            else if (values.Length == 4) GL.Uniform4(location, values[0], values[1], values[2], values[3]);
            else if (values.Length == 3) GL.Uniform3(location, values[0], values[1], values[2]);
            else if (values.Length == 2) GL.Uniform2(location, values[0], values[1]);
            else GL.Uniform1(location, values[0]);
        });
    }

    /// <summary>Restores the captured uniform values while their program is active.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var value in saved) restore(value.Name, value.Integer, value.Values);
    }
    #endregion

    #region Private
    /// <summary>Saves one supported uniform without retaining the backend's mutable array.</summary>
    private void Save(IShaderProgram shader, Func<string, int, bool, float[]> read, string name, int components, bool integer)
    {
        // Clone backend reads so later callbacks or nested frames cannot alias saved caller values.
        if (shader.HasUniform(name)) saved.Add((name, integer, (float[])read(name, components, integer).Clone()));
    }
    #endregion
}
