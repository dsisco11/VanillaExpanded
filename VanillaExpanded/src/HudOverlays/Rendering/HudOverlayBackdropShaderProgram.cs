using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.HudOverlays.Rendering;

/// <summary>Defines the texture-free status icon backdrop shader and its GUI matrix uniforms.</summary>
internal sealed class HudOverlayBackdropShaderProgram : ShaderProgram
{
    /// <summary>Supplies the enclosing GUI projection without changing native matrix state.</summary>
    public float[] ProjectionMatrix { set => ((IShaderProgram)this).UniformMatrix("projectionMatrix", value); }
    /// <summary>Positions the procedural quad in the current GUI model-view space.</summary>
    public float[] ModelViewMatrix { set => ((IShaderProgram)this).UniformMatrix("modelViewMatrix", value); }
    /// <summary>Applies the owning indicator's visibility transition to its backdrop.</summary>
    public float Opacity { set => ((IShaderProgram)this).Uniform("opacity", value); }
    #region Public API
    /// <summary>Creates the mod-owned vertex and fragment stages for file registration.</summary>
    public HudOverlayBackdropShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }
    #endregion
}
