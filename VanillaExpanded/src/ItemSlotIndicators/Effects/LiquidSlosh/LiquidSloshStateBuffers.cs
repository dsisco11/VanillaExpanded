using System;

using OpenTK.Graphics.OpenGL4;

namespace VanillaExpanded.ItemSlotIndicators.Effects.LiquidSlosh;

/// <summary>Owns the fixed ping-pong grid and its GPU views independently of program reload.</summary>
internal sealed class LiquidSloshStateBuffers : IDisposable
{
    /// <summary>Defines the fixed resolution shared by simulation and surface drawing.</summary>
    internal const int CellCount = 32;
    private readonly int[] buffers = new int[2], textures = new int[2];
    private readonly float[] zeroState = new float[CellCount * 2];
    private int readIndex;

    /// <summary>Gets the latest published GPU state view.</summary>
    internal int ReadTexture => textures[readIndex];
    /// <summary>Gets the next feedback destination, always distinct from the readable state.</summary>
    internal int WriteBuffer => buffers[1 - readIndex];
    /// <summary>Gets the dedicated solver feedback object.</summary>
    internal int FeedbackObject { get; private set; }
    /// <summary>Gets the empty point-grid vertex array indexed by gl_VertexID.</summary>
    internal int VertexArray { get; private set; }

    #region Public API
    /// <summary>Allocates fixed buffers/views once and releases partial allocations on failure.</summary>
    internal LiquidSloshStateBuffers()
    {
        try
        {
            FeedbackObject = GL.GenTransformFeedback();
            VertexArray = GL.GenVertexArray();
            GL.ActiveTexture(TextureUnit.Texture0);
            for (int index = 0; index < 2; index++)
            {
                buffers[index] = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.TextureBuffer, buffers[index]);
                GL.BufferData(BufferTarget.TextureBuffer, zeroState.Length * sizeof(float), zeroState, BufferUsageHint.DynamicCopy);
                textures[index] = GL.GenTexture();
                GL.BindTexture(TextureTarget.TextureBuffer, textures[index]);
                GL.TexBuffer(TextureBufferTarget.TextureBuffer, SizedInternalFormat.Rg32f, buffers[index]);
            }
        }
        catch { Dispose(); throw; }
        finally
        {
            GL.BindTexture(TextureTarget.TextureBuffer, 0);
            GL.BindBuffer(BufferTarget.TextureBuffer, 0);
        }
    }

    /// <summary>Publishes one completed transform-feedback step without copying or reading GPU memory.</summary>
    internal void Swap() => readIndex = 1 - readIndex;

    /// <summary>Flattens both states only on lifecycle discontinuities, never for each item draw.</summary>
    internal void Reset()
    {
        for (int index = 0; index < 2; index++)
        {
            GL.BindBuffer(BufferTarget.TextureBuffer, buffers[index]);
            GL.BufferSubData(BufferTarget.TextureBuffer, IntPtr.Zero, zeroState.Length * sizeof(float), zeroState);
        }
        GL.BindBuffer(BufferTarget.TextureBuffer, 0);
        readIndex = 0;
    }

    /// <summary>Releases each owned resource once, including partially initialized instances.</summary>
    public void Dispose()
    {
        for (int index = 0; index < 2; index++)
        {
            if (textures[index] != 0) GL.DeleteTexture(textures[index]);
            if (buffers[index] != 0) GL.DeleteBuffer(buffers[index]);
            textures[index] = buffers[index] = 0;
        }
        if (FeedbackObject != 0) GL.DeleteTransformFeedback(FeedbackObject);
        if (VertexArray != 0) GL.DeleteVertexArray(VertexArray);
        FeedbackObject = VertexArray = 0;
    }
    #endregion
}
