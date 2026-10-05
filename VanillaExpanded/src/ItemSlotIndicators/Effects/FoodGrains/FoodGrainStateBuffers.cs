using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.ItemSlotIndicators.Effects.FoodGrains;

/// <summary>Owns interleaved particle ping-pong storage independently of shader reload.</summary>
internal sealed class FoodGrainStateBuffers : IDisposable
{
    /// <summary>Number of grains shared by all food-container indicators.</summary>
    internal const int ParticleCount = 384;
    private readonly int[] buffers = new int[2], textures = new int[2];
    private readonly float[] initial;
    private int readIndex;
    /// <summary>Gets the completed particle state as two RGBA texels per grain.</summary>
    internal int ReadTexture => textures[readIndex];
    /// <summary>Gets the distinct destination buffer for the next pass.</summary>
    internal int WriteBuffer => buffers[1 - readIndex];
    /// <summary>Gets the dedicated transform-feedback object.</summary>
    internal int FeedbackObject { get; private set; }
    /// <summary>Gets the vertex array indexed solely by gl_VertexID.</summary>
    internal int VertexArray { get; private set; }

    #region Public API
    /// <summary>Allocates fixed buffers once, releasing partial allocations on failure.</summary>
    internal FoodGrainStateBuffers(int particleCount = ParticleCount, float minimumRadius = 0.01f, float maximumRadius = 0.023f)
    {
        initial = CreateInitialState(particleCount, minimumRadius, maximumRadius);
        try
        {
            FeedbackObject = GL.GenTransformFeedback();
            VertexArray = GL.GenVertexArray();
            GL.ActiveTexture(TextureUnit.Texture0);
            for (int i = 0; i < 2; i++)
            {
                buffers[i] = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.TextureBuffer, buffers[i]);
                GL.BufferData(BufferTarget.TextureBuffer, initial.Length * sizeof(float), initial, BufferUsageHint.DynamicCopy);
                textures[i] = GL.GenTexture();
                GL.BindTexture(TextureTarget.TextureBuffer, textures[i]);
                GL.TexBuffer(TextureBufferTarget.TextureBuffer, SizedInternalFormat.Rgba32f, buffers[i]);
            }
        }
        catch { Dispose(); throw; }
        finally
        {
            GL.BindTexture(TextureTarget.TextureBuffer, 0);
            GL.BindBuffer(BufferTarget.TextureBuffer, 0);
        }
    }

    /// <summary>Publishes a completed pass without copying particle data.</summary>
    internal void Swap() => readIndex = 1 - readIndex;

    /// <summary>Restores deterministic nonoverlapping grains only at lifecycle discontinuities.</summary>
    internal void Reset()
    {
        for (int i = 0; i < 2; i++)
        {
            GL.BindBuffer(BufferTarget.TextureBuffer, buffers[i]);
            GL.BufferSubData(BufferTarget.TextureBuffer, IntPtr.Zero, initial.Length * sizeof(float), initial);
        }
        GL.BindBuffer(BufferTarget.TextureBuffer, 0);
        readIndex = 0;
    }

    /// <summary>Creates a deterministic irregular bed of varied, nonoverlapping grains at rest.</summary>
    internal static float[] CreateInitialState(int particleCount = ParticleCount, float minimumRadius = 0.01f, float maximumRadius = 0.023f)
    {
        if (particleCount is < 2 or > ParticleCount || !float.IsFinite(minimumRadius) || !float.IsFinite(maximumRadius)
            || minimumRadius <= 0 || maximumRadius < minimumRadius || maximumRadius > 0.035f)
            throw new ArgumentOutOfRangeException(nameof(particleCount), "Unsupported granular initialization settings.");
        var data = new float[particleCount * 8];
        uint random = 0x41C64E6D;
        float pileTop = 0;
        for (int i = 0; i < particleCount; i++)
        {
            int offset = i * 8;
            float radius = minimumRadius + (maximumRadius - minimumRadius) * NextUnit(ref random);
            float x = 0, y = 0;
            bool placed = false;
            // Rejection sampling removes lattice rows; broad radius variation also discourages crystallization.
            for (int attempt = 0; attempt < 20000 && !placed; attempt++)
            {
                x = radius + (1 - 2 * radius) * NextUnit(ref random);
                y = radius + (0.85f - 2 * radius) * NextUnit(ref random);
                placed = true;
                for (int j = 0; j < i; j++)
                {
                    float dx = x - data[j * 8], dy = y - data[j * 8 + 1];
                    float separation = radius + data[j * 8 + 6] + 0.0001f;
                    if (dx * dx + dy * dy < separation * separation) { placed = false; break; }
                }
            }
            if (!placed) throw new InvalidOperationException("Unable to initialize the irregular grain bed.");
            data[offset] = data[offset + 4] = x;
            data[offset + 1] = data[offset + 5] = y;
            data[offset + 6] = radius;
            pileTop = MathF.Max(pileTop, y + radius);
        }
        for (int i = 0; i < particleCount; i++) data[i * 8 + 7] = pileTop;
        return data;
    }

    /// <summary>Releases each owned handle once, including partial initialization.</summary>
    public void Dispose()
    {
        for (int i = 0; i < 2; i++)
        {
            if (textures[i] != 0) GL.DeleteTexture(textures[i]);
            if (buffers[i] != 0) GL.DeleteBuffer(buffers[i]);
            textures[i] = buffers[i] = 0;
        }
        if (FeedbackObject != 0) GL.DeleteTransformFeedback(FeedbackObject);
        if (VertexArray != 0) GL.DeleteVertexArray(VertexArray);
        FeedbackObject = VertexArray = 0;
    }
    #endregion

    #region Private
    /// <summary>Produces reproducible startup samples without per-frame randomness or shared random state.</summary>
    private static float NextUnit(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) / 16777216f;
    }
    #endregion
}
