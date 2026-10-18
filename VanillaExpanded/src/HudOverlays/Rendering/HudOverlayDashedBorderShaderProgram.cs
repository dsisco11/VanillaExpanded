using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Defines the procedural dashed frame program and its typed GUI uniforms.</summary>
internal sealed class HudOverlayDashedBorderShaderProgram : ShaderProgram
{
    #region Public API
    /// <summary>Supplies the enclosing GUI projection.</summary>
    public float[] ProjectionMatrix { set => ((IShaderProgram)this).UniformMatrix("projectionMatrix", value); }
    /// <summary>Places the frame quad without modifying native matrix state.</summary>
    public float[] ModelViewMatrix { set => ((IShaderProgram)this).UniformMatrix("modelViewMatrix", value); }
    /// <summary>Supplies physical dimensions so dash spacing is independent of item size.</summary>
    public Vec2f FrameSize { set => ((IShaderProgram)this).Uniform("frameSize", value); }
    /// <summary>Applies the enclosing visibility opacity.</summary>
    public float Opacity { set => ((IShaderProgram)this).Uniform("opacity", value); }

    /// <summary>Creates the mod-owned stages for file registration.</summary>
    public HudOverlayDashedBorderShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }
    #endregion
}
