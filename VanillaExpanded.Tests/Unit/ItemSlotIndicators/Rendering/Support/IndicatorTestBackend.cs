using Moq;

using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.ItemSlotIndicators.Rendering;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;

/// <summary>Simulates engine allocation, compilation, validation, and upload outcomes independently of a graphics context.</summary>
internal sealed class IndicatorTestBackend : IItemSlotIndicatorResourceBackend
{
    internal readonly List<Mock<IShaderProgram>> Programs = [];
    internal readonly List<IndicatorTestMesh> Meshes = [];
    internal readonly List<MeshData> UploadedData = [];
    internal readonly List<(string Identity, string Reason)> Failures = [];
    internal readonly HashSet<string> CompileFailures = [];
    internal readonly HashSet<string> RegistrationFailures = [];
    internal readonly HashSet<string> ValidationFailures = [];
    internal readonly List<string> RegisteredNames = [];
    internal Action? DuringValidation;
    internal bool FailNextUpload;
    internal bool ReturnPartialUpload;
    internal int CompileCalls;

    #region Public API
    /// <summary>Creates a tracked program with independent compile and disposal state.</summary>
    public IShaderProgram CreateProgram()
    {
        var program = new Mock<IShaderProgram>();
        bool disposed = false;
        program.SetupGet(p => p.Disposed).Returns(() => disposed);
        program.Setup(p => p.Dispose()).Callback(() => disposed = true);
        program.Setup(p => p.Compile()).Returns(() =>
        {
            CompileCalls++;
            return !CompileFailures.Contains(program.Object.PassName);
        });
        Programs.Add(program);
        return program.Object;
    }

    /// <summary>Records stable registration names and can fail after a program has been created.</summary>
    public void RegisterProgram(ItemSlotIndicatorEffectDefinition definition, IShaderProgram program)
    {
        RegisteredNames.Add(definition.ShaderName);
        Mock.Get(program).SetupGet(p => p.PassName).Returns(definition.ShaderName);
        if (RegistrationFailures.Contains(definition.ShaderName)) throw new InvalidOperationException("Asset loading failed.");
    }

    /// <summary>Simulates contract rejection and registration that arrives while an attempt is being processed.</summary>
    public void ValidateProgram(IShaderProgram program)
    {
        DuringValidation?.Invoke();
        if (ValidationFailures.Contains(program.PassName)) throw new InvalidOperationException("Linked ABI mismatch.");
    }

    /// <summary>Records successful and partial returned allocations or throws before allocating a handle.</summary>
    public MeshRef UploadMesh(MeshData mesh)
    {
        UploadedData.Add(mesh);
        if (FailNextUpload)
        {
            FailNextUpload = false;
            throw new InvalidOperationException("Upload failed.");
        }
        var result = new IndicatorTestMesh(!ReturnPartialUpload);
        ReturnPartialUpload = false;
        Meshes.Add(result);
        return result;
    }

    /// <summary>Records owner diagnostics for per-effect, per-attempt assertions.</summary>
    public void ReportFailure(string identity, string reason) => Failures.Add((identity, reason));
    #endregion
}
