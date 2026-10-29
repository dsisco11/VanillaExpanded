using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.CraftingMenu;

internal interface ICraftingIconShaderProgram : IShaderProgram
{
    float[] ProjectionMatrix { set; }
    float[] ModelViewMatrix { set; }
    Vector4 TileBounds { set; }
    Vector4 TextureBounds { set; }
    CraftingTileStatus RecipeState { set; }
    bool HasIcon { set; }
    int IconTexture { set; }
}

internal sealed class CraftingIconShaderProgram : ShaderProgram, ICraftingIconShaderProgram
{
    public float[] ProjectionMatrix
    {
        set => ((IShaderProgram)this).UniformMatrix("projectionMatrix", value);
    }

    public float[] ModelViewMatrix
    {
        set => ((IShaderProgram)this).UniformMatrix("modelViewMatrix", value);
    }

    public Vector4 TileBounds
    {
        set => Uniform("tileBounds", value.X, value.Y, value.Z, value.W);
    }

    public Vector4 TextureBounds
    {
        set => Uniform("textureBounds", value.X, value.Y, value.Z, value.W);
    }

    public CraftingTileStatus RecipeState
    {
        set => Uniform("recipeState", (int)value);
    }

    public bool HasIcon
    {
        set => Uniform("hasIcon", value ? 1 : 0);
    }

    public int IconTexture
    {
        set => BindTexture2D("iconTexture", value, 0);
    }

    internal CraftingIconShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }
}
