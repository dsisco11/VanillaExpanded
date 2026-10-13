using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.EnvironmentalTemperature;

/// <summary>Registers the mod-owned temperature gradient vertex and fragment stages.</summary>
internal sealed class EnvironmentalTemperatureScaleShaderProgram : ShaderProgram
{
    #region Public API
    /// <summary>Creates shader stages in the mod asset domain without compiling during construction.</summary>
    public EnvironmentalTemperatureScaleShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }
    #endregion
}
