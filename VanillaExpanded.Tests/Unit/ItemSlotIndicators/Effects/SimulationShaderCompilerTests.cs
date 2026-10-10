using VanillaExpanded.ItemSlotIndicators.Effects;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Effects;

/// <summary>Checks simulation compiler ownership guards before any graphics calls.</summary>
[Trait("Category", "Unit")]
public sealed class SimulationShaderCompilerTests
{
    #region Public API
    #region Compilation Contracts
    /// <summary>Invalid stage combinations fail before compilation, allocation, or mutation of engine shader tracking.</summary>
    [Fact]
    public void Compilation_RequiresVertexAndFragmentStagesWithoutGeometry()
    {
        var shader = new ShaderProgram();
        Assert.Throws<InvalidOperationException>(() => SimulationShaderCompiler.Compile(shader, ["next"]));
        shader.VertexShader = new Shader();
        Assert.Throws<InvalidOperationException>(() => SimulationShaderCompiler.Compile(shader, ["next"]));
        shader.FragmentShader = new Shader();
        shader.GeometryShader = new Shader();
        Assert.Throws<InvalidOperationException>(() => SimulationShaderCompiler.Compile(shader, ["next"]));
        Assert.Equal(0, shader.ProgramId);
        Assert.False(shader.Disposed);
    }

    /// <summary>Compiling an existing executable is rejected without discarding its handle or uniform metadata.</summary>
    [Fact]
    public void Compilation_RejectsExistingProgramWithoutMutatingIt()
    {
        var shader = new ShaderProgram { ProgramId = 123 };
        shader.uniformLocations["existing"] = 456;
        Assert.Throws<InvalidOperationException>(() => SimulationShaderCompiler.Compile(shader, ["next"]));
        Assert.Equal(123, shader.ProgramId);
        Assert.Equal(456, shader.uniformLocations["existing"]);
        Assert.False(shader.Disposed);
    }

    /// <summary>Missing feedback declarations cannot silently produce a raster-only simulation program.</summary>
    [Fact]
    public void Compilation_RejectsEmptyFeedbackDeclarationBeforeGpuWork()
    {
        var shader = new ShaderProgram { VertexShader = new Shader(), FragmentShader = new Shader() };
        Assert.Throws<ArgumentException>(() => SimulationShaderCompiler.Compile(shader, []));
        Assert.Equal(0, shader.ProgramId);
        Assert.False(shader.Disposed);
    }
    #endregion
    #endregion
}
