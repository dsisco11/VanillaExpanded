using Moq;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaExpanded.SpawnDecal;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.SpawnDecal;

/// <summary>Exercises decal cleanup against real indexed OpenGL state without launching the game.</summary>
[Collection("ToolHeadGraphics")]
public sealed class SpawnDecalRenderStateGlTests
{
    #region Public API
    /// <summary>Restores revealage and other incoming state after a failed draw, preserving other attachments.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedDrawRestoresIncomingState(bool initiallyEnabled)
    {
        using var threadPolicy = new ThreadPolicy();
        using var window = new NativeWindow(new NativeWindowSettings
        {
            StartVisible = false, ClientSize = new Vector2i(16, 16), APIVersion = new Version(4, 0),
            Profile = ContextProfile.Core, Flags = ContextFlags.ForwardCompatible
        });
        window.Context.MakeCurrent();
        GL.LoadBindings(new GLFWBindingsContext());
        var render = new Mock<IRenderAPI>();
        var previous = new Mock<IShaderProgram>();
        var decal = new Mock<IShaderProgram>();
        IShaderProgram? current = previous.Object;
        render.SetupGet(r => r.CurrentActiveShader).Returns(() => current!);
        previous.Setup(s => s.Stop()).Callback(() => current = null);
        previous.Setup(s => s.Use()).Callback(() =>
        {
            current = previous.Object;
            // Engine program activation can replace a shadow texture before cleanup restores the binding.
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        });
        decal.Setup(s => s.Stop()).Callback(() => current = null);
        int[] units = [0, 2, 3, 4];
        int[] textures = new int[units.Length];
        GL.GenTextures(textures.Length, textures);
        try
        {
            for (int i = 0; i < units.Length; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + units[i]);
                GL.BindTexture(TextureTarget.Texture2D, textures[i]);
            }
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BlendEquationSeparate(0, BlendEquationMode.FuncSubtract, BlendEquationMode.FuncReverseSubtract);
            GL.BlendFuncSeparate(0, BlendingFactorSrc.DstColor, BlendingFactorDest.Zero,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            if (initiallyEnabled) GL.Enable(IndexedEnableCap.Blend, 0); else GL.Disable(IndexedEnableCap.Blend, 0);
            GL.Enable(IndexedEnableCap.Blend, 1);
            GL.BlendEquationSeparate(1, BlendEquationMode.Max, BlendEquationMode.Min);
            GL.BlendFuncSeparate(1, BlendingFactorSrc.One, BlendingFactorDest.One,
                BlendingFactorSrc.DstAlpha, BlendingFactorDest.OneMinusDstAlpha);
            GL.DepthMask(true);
            GL.Enable(EnableCap.CullFace);
            int[] attachment0 = ReadBlend(0), attachment1 = ReadBlend(1);

            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var state = new SpawnDecalRenderState(render.Object);
                Assert.Null(current);
                current = decal.Object;
                for (int i = 0; i < units.Length; i++)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + units[i]);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }
                GL.BlendEquation(0, BlendEquationMode.FuncAdd);
                GL.BlendFuncSeparate(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                    BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                if (initiallyEnabled) GL.Disable(IndexedEnableCap.Blend, 0); else GL.Enable(IndexedEnableCap.Blend, 0);
                GL.DepthMask(false);
                GL.Disable(EnableCap.CullFace);
                throw new InvalidOperationException("Draw failed");
            }));

            Assert.Same(previous.Object, current);
            previous.Verify(s => s.Stop(), Times.Once);
            previous.Verify(s => s.Use(), Times.Once);
            decal.Verify(s => s.Stop(), Times.Once);
            Assert.Equal(attachment0, ReadBlend(0));
            Assert.Equal(attachment1, ReadBlend(1));
            Assert.Equal(initiallyEnabled, GL.IsEnabled(IndexedEnableCap.Blend, 0));
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 1));
            Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            Assert.Equal((int)TextureUnit.Texture7, GL.GetInteger(GetPName.ActiveTexture));
            for (int i = 0; i < units.Length; i++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + units[i]);
                Assert.Equal(textures[i], GL.GetInteger(GetPName.TextureBinding2D));
            }
            Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.DeleteTextures(textures.Length, textures);
        }
    }
    #endregion

    #region Private
    /// <summary>Reads both blend equations and all source/destination factors for one draw buffer.</summary>
    private static int[] ReadBlend(int attachment)
    {
        GetPName[] names = [GetPName.BlendSrcRgb, GetPName.BlendDstRgb, GetPName.BlendSrcAlpha,
            GetPName.BlendDstAlpha, GetPName.BlendEquationRgb, GetPName.BlendEquationAlpha];
        var values = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
            GL.GetInteger((GetIndexedPName)names[i], attachment, out values[i]);
        return values;
    }

    /// <summary>Temporarily allows this serialized worker to own a hidden GLFW context.</summary>
    private sealed class ThreadPolicy : IDisposable
    {
        private readonly bool previous = GLFWProvider.CheckForMainThread;
        #region Public API
        /// <summary>Disables the application main-thread assertion for this fixture.</summary>
        public ThreadPolicy() => GLFWProvider.CheckForMainThread = false;
        /// <summary>Restores the enclosing application's thread policy.</summary>
        public void Dispose() => GLFWProvider.CheckForMainThread = previous;
        #endregion
    }
    #endregion
}
