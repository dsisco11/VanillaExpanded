using System;
using System.Collections.Generic;

using OpenTK.Graphics.OpenGL4;

using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.CrucibleIndicators;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Adapts resource preparation to engine shader compilation and mesh upload on the graphics thread.</summary>
internal sealed class ItemSlotIndicatorResourceBackend(ICoreClientAPI api) : IItemSlotIndicatorResourceBackend
{
    private static readonly Dictionary<string, ActiveUniformType> uniformTypes = new()
    {
        ["projectionMatrix"] = ActiveUniformType.FloatMat4, ["modelViewMatrix"] = ActiveUniformType.FloatMat4,
        ["slotBounds"] = ActiveUniformType.FloatVec4, ["fill"] = ActiveUniformType.Float,
        ["resourceFill"] = ActiveUniformType.Float,
        ["color"] = ActiveUniformType.FloatVec4, ["timeSeconds"] = ActiveUniformType.Float,
        ["motion"] = ActiveUniformType.FloatVec2, ["effectParameters"] = ActiveUniformType.FloatVec4,
        ["cameraBob"] = ActiveUniformType.Float,
        ["segmentCount"] = ActiveUniformType.Int
    };
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
        bool liquid = program.PassName == LiquidSloshIndicatorEffect.Definition.ShaderName
            || program.PassName == CrucibleIndicatorEffect.Molten.ShaderName;
        bool food = program.PassName == FoodGrainIndicatorEffect.Definition.ShaderName;
        bool grains = food || program.PassName == CrucibleIndicatorEffect.Solid.ShaderName;
        if (liquid) ItemSlotIndicatorBufferSampler.Register(program, "liquidSurface");
        if (grains) ItemSlotIndicatorBufferSampler.Register(program, "grainState");
        foreach (string uniform in new[] { "projectionMatrix", "modelViewMatrix", "slotBounds", "fill" })
            if (!program.HasUniform(uniform))
                throw new InvalidOperationException($"Indicator shader is missing required uniform '{uniform}'.");
        // ABI one supplies only vec3 position at location zero; built-in simulations own their buffer samplers.
        GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveAttributes, out int count);
        if (count != 1)
            throw new InvalidOperationException("Indicator shader requires exactly one active position attribute.");
        string attribute = GL.GetActiveAttrib(program.ProgramId, 0, out int size, out ActiveAttribType type);
        if (size != 1 || type != ActiveAttribType.FloatVec3 || GL.GetAttribLocation(program.ProgramId, attribute) != 0)
            throw new InvalidOperationException("Indicator position must be a vec3 at attribute location zero.");
        if (program.GeometryShader is not null || program.UBOs.Count != 0)
            throw new InvalidOperationException("Indicator programs cannot use geometry stages or uniform buffers.");
        if (grains && !program.HasUniform("grainState"))
            throw new InvalidOperationException("Grain drawing requires shared particle state.");
        if (grains && !program.HasUniform("resourceFill"))
            throw new InvalidOperationException("Grain drawing requires the unmapped contents fraction.");
        if (food && !program.HasUniform("foodPalette"))
            throw new InvalidOperationException("Grain drawing requires food particle colors.");
        if (grains && !food && !program.HasUniform("metalPalette"))
            throw new InvalidOperationException("Solid crucible drawing requires ingredient particle colors.");
        if (liquid && (!program.HasUniform("liquidSurface") || !program.HasUniform("surfaceCellCount")
            || !program.HasUniform("segmentCount")))
            throw new InvalidOperationException("Liquid drawing requires shared surface inputs and mesh subdivision count.");
        GL.GetProgram(program.ProgramId, GetProgramParameterName.ActiveUniforms, out int uniforms);
        for (int index = 0; index < uniforms; index++)
        {
            string name = GL.GetActiveUniform(program.ProgramId, index, out int uniformSize, out ActiveUniformType uniformType);
            if ((food && name == "foodPalette[0]") || (grains && !food && name == "metalPalette[0]"))
            {
                if (uniformType != ActiveUniformType.FloatVec4 || uniformSize != ItemSlotIndicatorParticlePalette.ColorCount)
                    throw new InvalidOperationException("Food palette must contain sixteen vec4 colors.");
                continue;
            }
            if (liquid && name == "liquidSurface" && uniformType == ActiveUniformType.SamplerBuffer && uniformSize == 1)
                continue;
            if (grains && name == "grainState" && uniformType == ActiveUniformType.SamplerBuffer && uniformSize == 1)
                continue;
            if (liquid && name == "surfaceCellCount" && (uniformType != ActiveUniformType.Int || uniformSize != 1))
                throw new InvalidOperationException("Liquid surface cell count must be one integer.");
            if (uniformTypes.TryGetValue(name, out var expected) && (uniformType != expected || uniformSize != 1))
                throw new InvalidOperationException($"Indicator uniform '{name}' has an incompatible ABI type.");
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
