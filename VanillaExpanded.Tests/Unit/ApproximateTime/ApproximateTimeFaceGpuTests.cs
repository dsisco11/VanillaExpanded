using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaExpanded.ApproximateTime;

namespace VanillaExpanded.Tests.Unit.ApproximateTime;

/// <summary>Checks actual file shaders against synthetic textures without launching the game.</summary>
[Trait("Category", "GPU")]
public sealed class ApproximateTimeFaceGpuTests
{
    #region Public API
    /// <summary>Solar masks select the correct scene, preserve orientation, and premultiply transparent backing.</summary>
    [ClockGpuFact]
    public void FileShaderRespectsSolarCoverageAndPremultipliedBacking()
    {
        GLFWProvider.CheckForMainThread = false;
        using var window = new NativeWindow(new NativeWindowSettings
        {
            StartVisible = false, StartFocused = false, ClientSize = new Vector2i(1, 1),
            APIVersion = new Version(3, 3), Profile = ContextProfile.Core, Flags = ContextFlags.ForwardCompatible
        });
        window.Context.MakeCurrent();
        GL.LoadBindings(new GLFWBindingsContext());
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "VanillaExpanded", "assets"))) root = root.Parent;
        Assert.NotNull(root);
        string directory = Path.Combine(root.FullName, "VanillaExpanded", "assets", "vanillaexpanded", "shaders");
        int vertex = Compile(ShaderType.VertexShader, File.ReadAllText(Path.Combine(directory, "approximate_time_face.vsh")));
        int fragment = Compile(ShaderType.FragmentShader, File.ReadAllText(Path.Combine(directory, "approximate_time_face.fsh")));
        int program = GL.CreateProgram(), vao = GL.GenVertexArray(), buffer = GL.GenBuffer(), framebuffer = GL.GenFramebuffer();
        var textures = new List<int>();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked == 1, GL.GetProgramInfoLog(program));
            GL.UseProgram(program); GL.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            float[] positions = { -1, -1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, 1, 1, 0, -1, 1, 0 };
            GL.BufferData(BufferTarget.ArrayBuffer, positions.Length * sizeof(float), positions, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0);
            // Distinct opaque scenes make opposite-phase leakage observable without depending on authored art.
            float[][] pixels = { new[] { .4f, .2f, .1f, .5f }, new[] { 1f, 0, 0, 1 }, new[] { 0f, 0, 1, 1 } };
            string[] names = { "housing", "dayScenery", "nightScenery" };
            for (int index = 0; index < 3; index++)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + index);
                int texture = GL.GenTexture(); textures.Add(texture); GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, pixels[index]);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                GL.Uniform1(GL.GetUniformLocation(program, names[index]), index);
            }
            GL.ActiveTexture(TextureUnit.Texture3);
            int output = GL.GenTexture(); textures.Add(output); GL.BindTexture(TextureTarget.Texture2D, output);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, 512, 512, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, output, 0);
            Assert.Equal(FramebufferErrorCode.FramebufferComplete, GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
            GL.Viewport(0, 0, 512, 512); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.DepthTest);
            GL.Uniform4(GL.GetUniformLocation(program, "faceGeometry"), 256f, 250f, 174f, 512f);
            GL.Uniform1(GL.GetUniformLocation(program, "segmentInset"), .002f);
            var colors = new float[96];
            for (int hour = 0; hour < 24; hour++) { colors[hour * 4 + 1] = 1; colors[hour * 4 + 3] = 1; }
            GL.Uniform4(GL.GetUniformLocation(program, "hourColors"), 24, colors);
            Draw(program, ApproximateTimeDayPhases.Sample(0, _ => .5f)!);
            AssertPixel(256, 150, 1, 0, 0, 1); AssertPixel(256, 350, 1, 0, 0, 1);
            AssertPixel(8, 8, .2f, .1f, .05f, .5f);
            Draw(program, ApproximateTimeDayPhases.Sample(0, _ => -.5f)!);
            AssertPixel(256, 150, 0, 0, 1, 1); AssertPixel(256, 350, 0, 0, 1, 1);
            Draw(program, ApproximateTimeDayPhases.Neutral);
            AssertPixel(256, 150, 0, 1, 0, 1);
            Draw(program, ApproximateTimeDayPhases.Sample(0, _ => -.05f)!);
            AssertPixel(256, 350, 0, 1, 0, 1);
            Draw(program, ApproximateTimeDayPhases.Sample(0, day => .4f + (float)Math.Cos((day - .5) * 2 * Math.PI))!);
            AssertPixel(159, 224, 1, 0, 0, 1);
            Draw(program, ApproximateTimeDayPhases.Sample(0, day => -.4f + (float)Math.Cos((day - .5) * 2 * Math.PI))!);
            AssertPixel(159, 224, 0, 0, 1, 1);
            AssertPixel(256, 150, 1, 0, 0, 1); AssertPixel(256, 350, 0, 0, 1, 1);
            Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.UseProgram(0); GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.DeleteFramebuffer(framebuffer); foreach (int texture in textures) GL.DeleteTexture(texture);
            GL.DeleteBuffer(buffer); GL.DeleteVertexArray(vao); GL.DeleteProgram(program);
            GL.DeleteShader(vertex); GL.DeleteShader(fragment);
        }
    }
    #endregion

    #region Private
    /// <summary>Compiles a production shader and reports driver diagnostics directly.</summary>
    private static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        Assert.True(compiled == 1, GL.GetShaderInfoLog(shader));
        return shader;
    }

    /// <summary>Uploads the domain mask and draws the complete cached background.</summary>
    private static void Draw(int program, ApproximateTimeDayPhases phases)
    {
        var samples = new float[288];
        for (int hour = 0; hour < 24; hour++)
        for (int sample = 0; sample < 12; sample++)
            samples[hour * 12 + sample] = phases.AllowsScenery(hour, sample, false) ? 1 : phases.AllowsScenery(hour, sample, true) ? 2 : 0;
        GL.Uniform4(GL.GetUniformLocation(program, "solarSamples"), 72, samples);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
    }

    /// <summary>Reads a framebuffer pixel and compares all premultiplied channels with a small numeric tolerance.</summary>
    private static void AssertPixel(int x, int y, params float[] expected)
    {
        var actual = new float[4]; GL.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.Float, actual);
        for (int channel = 0; channel < 4; channel++)
            Assert.InRange(actual[channel], expected[channel] - .01f, expected[channel] + .01f);
    }
    #endregion
}
