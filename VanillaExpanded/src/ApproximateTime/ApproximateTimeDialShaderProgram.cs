using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Registers the mod-owned time dial vertex and fragment stages.</summary>
internal sealed class ApproximateTimeDialShaderProgram : ShaderProgram
{
    #region Public API
    /// <summary>Creates shader stages in the mod asset domain without compiling during construction.</summary>
    public ApproximateTimeDialShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }
    #endregion
}
