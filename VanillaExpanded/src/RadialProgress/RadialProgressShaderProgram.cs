using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.RadialProgress;

/// <summary>
/// Shader program for the radial progress bar with typed uniform accessors.
/// </summary>
public class RadialProgressShaderProgram : ShaderProgram
{
    /// <summary>
    /// Progress value from 0 (empty) to 1 (full).
    /// </summary>
    public float ProgressScalar
    {
        set => Uniform("progressScalar", value);
    }

    /// <summary>
    /// Outer radius of the ring, from 0 to 1 (1 = edge of inscribed circle).
    /// </summary>
    public float OuterRadius
    {
        set => Uniform("outerRadius", value);
    }

    /// <summary>
    /// Inner radius of the ring, from 0 to 1 (0 = solid disc, >0 = ring).
    /// </summary>
    public float InnerRadius
    {
        set => Uniform("innerRadius", value);
    }

    /// <summary>
    /// Tint color applied to the progress bar (RGBA).
    /// </summary>
    public Vec4f TintColor
    {
        set => Uniform("tintColor", value);
    }

    /// <summary>
    /// The projection matrix for transforming vertices.
    /// </summary>
    public float[] ProjectionMatrix
    {
        set => ((IShaderProgram)this).UniformMatrix("projectionMatrix", value);
    }

    /// <summary>
    /// The model-view matrix for transforming vertices.
    /// </summary>
    public float[] ModelViewMatrix
    {
        set => ((IShaderProgram)this).UniformMatrix("modelViewMatrix", value);
    }
}
