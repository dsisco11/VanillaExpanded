using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.ApproximateTime;

/// <summary>Defines typed texture, geometry, and seasonal inputs for GPU clock background composition.</summary>
internal sealed class ApproximateTimeFaceShaderProgram : ShaderProgram
{
    /// <summary>Gets the engine file-program basename for the clock background stages.</summary>
    internal const string ShaderName = "approximate_time_face";
    private readonly float[] colors = new float[24 * 4];
    private readonly float[] samples = new float[24 * ApproximateTimeDayPhases.SamplesPerHour];

    #region Public API
    #region Texture and geometry inputs
    /// <summary>Binds the straight-alpha housing to its reserved sampler unit.</summary>
    public int HousingTextureId { set => BindTexture2D("housing", value, 0); }
    /// <summary>Binds the authored day scenery to its reserved sampler unit.</summary>
    public int DaySceneryTextureId { set => BindTexture2D("dayScenery", value, 1); }
    /// <summary>Binds the authored night scenery to its reserved sampler unit.</summary>
    public int NightSceneryTextureId { set => BindTexture2D("nightScenery", value, 2); }
    /// <summary>Supplies the aperture center, radius, and canvas size in authored pixels.</summary>
    public Vec4f FaceGeometry { set => ((IShaderProgram)this).Uniform("faceGeometry", value); }
    /// <summary>Supplies the angular inset on each side of an hourly segment in radians.</summary>
    public float SegmentInset { set => ((IShaderProgram)this).Uniform("segmentInset", value); }
    #endregion

    #region Construction and solar distribution
    /// <summary>Creates the mod-owned vertex and fragment stages for engine file registration.</summary>
    public ApproximateTimeFaceShaderProgram(ICoreClientAPI api)
    {
        AssetDomain = Constants.ModId;
        VertexShader = (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader = (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }

    /// <summary>Packs hourly tints and five-minute scenery classifications into the shader's uniform arrays.</summary>
    public void SetDistribution(ApproximateTimeDayPhases phases)
    {
        bool neutral = ReferenceEquals(phases, ApproximateTimeDayPhases.Neutral);
        for (int hour = 0; hour < 24; hour++)
        {
            for (int channel = 0; channel < 3; channel++)
                colors[hour * 4 + channel] = neutral ? (channel == 2 ? .17f : .18f) : (float)phases.Channel(hour, channel);
            colors[hour * 4 + 3] = 1;
            // Pack four classifications per vec4 without decoding or compositing texture pixels.
            for (int sample = 0; sample < ApproximateTimeDayPhases.SamplesPerHour; sample++)
                samples[hour * ApproximateTimeDayPhases.SamplesPerHour + sample] =
                    phases.AllowsScenery(hour, sample, false) ? 1 : phases.AllowsScenery(hour, sample, true) ? 2 : 0;
        }
        ((IShaderProgram)this).Uniforms4("hourColors", 24, colors);
        ((IShaderProgram)this).Uniforms4("solarSamples", samples.Length / 4, samples);
    }
    #endregion
    #endregion
}
