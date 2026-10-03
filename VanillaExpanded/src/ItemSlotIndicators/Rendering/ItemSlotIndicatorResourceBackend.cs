using System;

using OpenTK.Graphics.OpenGL4;

using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Adapts resource preparation to engine shader compilation and mesh upload on the graphics thread.</summary>
internal sealed class ItemSlotIndicatorResourceBackend(ICoreClientAPI api) : IItemSlotIndicatorResourceBackend
{
    #region Public API
    /// <summary>Allocates through the engine, leaving registration and partial cleanup to the program owner.</summary>
    public IShaderProgram CreateProgram() => api.Shader.NewShaderProgram();

    /// <summary>Loads the declared file stages without replacing another live program owned by this cache.</summary>
    public void RegisterProgram(ItemSlotIndicatorEffectDefinition definition, IShaderProgram program)
    {
        program.AssetDomain = definition.ShaderAssetDomain;
        program.VertexShader = api.Shader.NewShader(EnumShaderType.VertexShader);
        program.FragmentShader = api.Shader.NewShader(EnumShaderType.FragmentShader);
        api.Shader.RegisterFileShaderProgram(definition.ShaderName, program);
    }

    /// <summary>Checks required linked inputs; optional animation inputs may legitimately be optimized out.</summary>
    public void ValidateProgram(IShaderProgram program)
    {
        if (program.Disposed || program.LoadError || program.ProgramId <= 0)
            throw new InvalidOperationException("Indicator shader did not produce a live linked program.");
        foreach (string uniform in new[] { "projectionMatrix", "modelViewMatrix", "slotBounds", "fill" })
            if (!program.HasUniform(uniform))
                throw new InvalidOperationException($"Indicator shader is missing required uniform '{uniform}'.");
        // ABI one supplies only vec3 position at location zero, never generated geometry or resource bindings.
        GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveAttributes, out int count);
        if (count != 1)
            throw new InvalidOperationException("Indicator shader requires exactly one active position attribute.");
        string attribute = GL.GetActiveAttrib(program.ProgramId, 0, out int size, out ActiveAttribType type);
        if (size != 1 || type != ActiveAttribType.FloatVec3 || GL.GetAttribLocation(program.ProgramId, attribute) != 0)
            throw new InvalidOperationException("Indicator position must be a vec3 at attribute location zero.");
        if (program.GeometryShader is not null || program.UBOs.Count != 0)
            throw new InvalidOperationException("Indicator programs cannot use geometry stages or uniform buffers.");
        GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveUniforms, out int uniforms);
        for (int index = 0; index < uniforms; index++)
        {
            GL.GetActiveUniform(program.ProgramId, index, out _, out ActiveUniformType uniformType);
            if (uniformType.ToString().Contains("Sampler", StringComparison.Ordinal)
                || uniformType.ToString().Contains("Image", StringComparison.Ordinal))
                throw new InvalidOperationException("Indicator programs cannot use samplers or image bindings.");
        }
    }

    /// <summary>Uploads one fixed mesh through the engine's allocation boundary.</summary>
    public MeshRef UploadMesh(MeshData mesh) => api.Render.UploadMesh(mesh);

    /// <summary>Reports availability without changing selection or throwing into the subsequent item draw.</summary>
    public void ReportFailure(string identity, string reason) =>
        api.Logger.Warning("[VanillaExpanded] Indicator resource '{0}' unavailable: {1}", identity, reason);
    #endregion
}
