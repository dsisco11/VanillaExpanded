using Moq;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Windowing.Desktop;
using OpenTK.Mathematics;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Serializes hidden context tests to keep graphics ownership on one worker.</summary>
[CollectionDefinition("ToolHeadGraphics",DisableParallelization=true)]
public sealed class ToolHeadGraphicsCollection;
/// <summary>Runs the production GL snapshot/restoration boundary in a hidden standalone context.</summary>
[Collection("ToolHeadGraphics")]
public sealed class ToolHeadRenderStateGlTests
{
    #region Public API
    /// <summary>Restores actual uniforms, texture/sampler bindings, flags, and matrix stacks after a failing draw.</summary>
    [Fact]
    public void HiddenContextRestoresActualGraphicsState()
    {
        using var threadPolicy = new ThreadPolicy();
        using var window = new NativeWindow(new NativeWindowSettings { StartVisible=false, ClientSize=new Vector2i(16,16), APIVersion=new Version(3,3), Profile=ContextProfile.Core, Flags=ContextFlags.ForwardCompatible });
        window.Context.MakeCurrent();
        GL.LoadBindings(new GLFWBindingsContext());
        int vertex=Compile(ShaderType.VertexShader,"#version 330 core\nvoid main(){gl_Position=vec4(0,0,0,1);}");
        int fragment=Compile(ShaderType.FragmentShader,"#version 330 core\nuniform float damageEffect; layout(std140) uniform Animation { vec4 animated; }; out vec4 c; void main(){c=vec4(damageEffect)+animated;}");
        int program=GL.CreateProgram();
        GL.AttachShader(program,vertex); GL.AttachShader(program,fragment); GL.LinkProgram(program);
        GL.GetProgram(program,GetProgramParameterName.LinkStatus,out int linked);
        Assert.Equal(1,linked);
        GL.UseProgram(program);
        int location=GL.GetUniformLocation(program,"damageEffect");
        GL.Uniform1(location,0.37f);
        var shader=new Mock<IShaderProgram>();
        shader.SetupGet(s=>s.ProgramId).Returns(program);
        shader.Setup(s=>s.HasUniform(It.IsAny<string>())).Returns<string>(name=>name=="damageEffect");
        var render=new Mock<IRenderAPI>();
        render.SetupGet(r=>r.CurrentActiveShader).Returns(shader.Object);
        render.Setup(r=>r.GetUniformLocation(program,It.IsAny<string>())).Returns<int,string>((_,name)=>GL.GetUniformLocation(program,name));
        var mv=new StackMatrix4(); mv.PushIdentity();
        var projection=new StackMatrix4(); projection.PushIdentity();
        render.SetupGet(r=>r.MvMatrix).Returns(mv); render.SetupGet(r=>r.PMatrix).Returns(projection);
        int texture=GL.GenTexture(), sampler=GL.GenSampler();
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D,texture); GL.BindSampler(0,sampler);
        GL.ActiveTexture(TextureUnit.Texture1);
        int block=GL.GetUniformBlockIndex(program,"Animation");
        int alignment=GL.GetInteger(GetPName.UniformBufferOffsetAlignment);
        int buffer=GL.GenBuffer(), genericBuffer=GL.GenBuffer();
        GL.BindBuffer(BufferTarget.UniformBuffer,buffer);
        GL.BufferData(BufferTarget.UniformBuffer,alignment*2,IntPtr.Zero,BufferUsageHint.DynamicDraw);
        GL.UniformBlockBinding(program,block,2);
        GL.BindBufferRange(BufferRangeTarget.UniformBuffer,2,buffer,(IntPtr)alignment,(IntPtr)16);
        GL.BindBuffer(BufferTarget.UniformBuffer,genericBuffer);
        GL.Enable(EnableCap.Blend); GL.Disable(EnableCap.CullFace); GL.DepthMask(false);
        try
        {
            Assert.Throws<InvalidOperationException>((Action)(()=>
            {
                using var state=new ToolHeadRenderState(render.Object,shader.Object);
                GL.Uniform1(location,0.99f);
                GL.UniformBlockBinding(program,block,3); GL.BindBufferBase(BufferRangeTarget.UniformBuffer,2,0);
                GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D,0); GL.BindSampler(0,0);
                GL.Disable(EnableCap.Blend); GL.Enable(EnableCap.CullFace); GL.DepthMask(true);
                mv.Push(); mv.Translate(100,200,0); projection.Top[0]=42;
                throw new InvalidOperationException();
            }));
            GL.GetUniform(program,location,out float value); Assert.Equal(0.37f,value);
            Assert.Equal((int)TextureUnit.Texture1,GL.GetInteger(GetPName.ActiveTexture));
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(texture,GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler,GL.GetInteger(GetPName.SamplerBinding));
            Assert.True(GL.IsEnabled(EnableCap.Blend)); Assert.False(GL.IsEnabled(EnableCap.CullFace));
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(1,mv.Count); Assert.Equal(0,mv.Top[12]); Assert.Equal(1,projection.Top[0]);
            GL.GetActiveUniformBlock(program,block,ActiveUniformBlockParameter.UniformBlockBinding,out int restoredPoint); Assert.Equal(2,restoredPoint);
            GL.GetInteger(GetIndexedPName.UniformBufferBinding,2,out int restoredBuffer); Assert.Equal(buffer,restoredBuffer);
            GL.GetInteger64((GetIndexedPName)0x8A29,2,out long start); Assert.Equal(alignment,start);
            GL.GetInteger64((GetIndexedPName)0x8A2A,2,out long length); Assert.Equal(16,length);
            Assert.Equal(genericBuffer,GL.GetInteger(GetPName.UniformBufferBinding));
            Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError,GL.GetError());
        }
        finally
        {
            GL.DeleteBuffer(buffer); GL.DeleteBuffer(genericBuffer);
            GL.DeleteSampler(sampler); GL.DeleteTexture(texture); GL.DeleteProgram(program); GL.DeleteShader(vertex); GL.DeleteShader(fragment);
        }
    }
    #endregion
    #region Private
    /// <summary>Temporarily permits a serialized test worker to own the standalone GLFW context.</summary>
    private sealed class ThreadPolicy : IDisposable
    {
        private readonly bool previous=GLFWProvider.CheckForMainThread;
        #region Public API
        /// <summary>Disables the application main-thread assertion for this fixture only.</summary>
        public ThreadPolicy() => GLFWProvider.CheckForMainThread=false;
        /// <summary>Restores the enclosing application's thread-check policy.</summary>
        public void Dispose() => GLFWProvider.CheckForMainThread=previous;
        #endregion
    }
    /// <summary>Compiles a minimal shader and reports driver compilation failure.</summary>
    private static int Compile(ShaderType type,string source)
    {
        int shader=GL.CreateShader(type); GL.ShaderSource(shader,source); GL.CompileShader(shader);
        GL.GetShader(shader,ShaderParameter.CompileStatus,out int compiled);
        Assert.True(compiled==1,GL.GetShaderInfoLog(shader));
        return shader;
    }
    #endregion
}





