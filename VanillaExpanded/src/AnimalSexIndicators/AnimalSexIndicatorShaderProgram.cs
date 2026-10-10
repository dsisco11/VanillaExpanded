using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.AnimalSexIndicators;

/// <summary>Defines the dedicated animal icon shader without standard-shader lighting or scene uniforms.</summary>
internal sealed class AnimalSexIndicatorShaderProgram : ShaderProgram
{
    #region Public API
    /// <summary>Creates the two mod-owned shader stages for registration and compilation.</summary>
    public AnimalSexIndicatorShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }
    #endregion
}
