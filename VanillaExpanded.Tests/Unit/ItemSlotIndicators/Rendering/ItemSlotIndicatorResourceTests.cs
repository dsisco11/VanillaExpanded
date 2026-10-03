using System.Numerics;

using Moq;

using VanillaExpanded.ItemSlotIndicators.Effects;
using VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Verifies sharing, explicit preparation, failure isolation, reload, and shutdown with simulated GPU handles.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorResourceTests
{
    #region Public API
    #region Sharing and Registration
    /// <summary>Identity variants share programs and geometry, while different geometry reuses only the program.</summary>
    [Fact]
    public void SharedVariants_CompileAndUploadOnlyOnceOutsideLookup()
    {
        using var context = new IndicatorResourceTestContext();
        var first = Effect("test:first");
        var variant = Effect("test:variant", parameters: new Vector4(1, 0, 0, 0));
        var dense = Effect("test:dense", segments: 64);
        context.Resources.Register(first);
        context.Resources.Register(first);
        context.Resources.Register(variant);
        context.Resources.Register(dense);
        Assert.Empty(context.Backend.Programs);
        Assert.Empty(context.Backend.Meshes);
        Assert.False(context.Resources.TryGet(first, out _, out _));
        context.Resources.Initialize();
        context.Resources.Initialize();

        Assert.True(context.Resources.TryGet(first, out var program, out var mesh));
        Assert.True(context.Resources.TryGet(variant, out var variantProgram, out var variantMesh));
        Assert.True(context.Resources.TryGet(dense, out var denseProgram, out var denseMesh));
        Assert.Same(program, variantProgram);
        Assert.Same(program, denseProgram);
        Assert.Same(mesh, variantMesh);
        Assert.NotSame(mesh, denseMesh);
        Assert.NotSame(mesh, context.Resources.Rectangle);
        Assert.Single(context.Backend.Programs);
        Assert.Equal(1, context.Backend.CompileCalls);
        Assert.Equal(3, context.Backend.Meshes.Count);
        Assert.NotNull(context.Backend.UploadedData[0].Uv);
        Assert.Null(context.Backend.UploadedData[1].Uv);
        for (int draw = 0; draw < 250; draw++) Assert.True(context.Resources.TryGet(first, out _, out _));
        Assert.Equal(1, context.Backend.CompileCalls);
        Assert.Equal(3, context.Backend.UploadedData.Count);
        context.Events.VerifyAdd(e => e.ReloadShader += It.IsAny<Vintagestory.API.Common.ActionBoolReturn>(), Times.Once);
    }

    /// <summary>Invalid identity and engine-name reuse fail before queued work or availability changes.</summary>
    [Fact]
    public void IncompatibleReuse_IsAtomicAndDistinctFromShaderFailure()
    {
        using var context = new IndicatorResourceTestContext();
        var original = Effect("test:effect");
        context.Resources.Register(original);
        context.Resources.Initialize();
        Assert.Throws<ArgumentException>(() => context.Resources.Register(Effect("test:effect", segments: 64)));
        Assert.Throws<ArgumentException>(() => context.Resources.Register(Effect("test:other", domain: "other")));
        Assert.Empty(context.Tasks);
        Assert.Empty(context.Backend.Failures);
        Assert.True(context.Resources.TryGet(original, out _, out _));
        Assert.False(context.Resources.TryGet(Effect("test:effect", segments: 64), out _, out _));
        Assert.Single(context.Backend.Programs);
    }

    /// <summary>A later registration remains plain until its queued boundary, even when its dependencies already exist.</summary>
    [Fact]
    public void LaterRegistrations_CoalesceAndDoNotPrepareDuringRegistration()
    {
        using var context = new IndicatorResourceTestContext();
        var original = Effect("test:original");
        var later = Effect("test:later");
        context.Resources.Register(original);
        context.Resources.Initialize();
        context.Resources.Register(later);
        context.Resources.Register(Effect("test:another"));
        Assert.False(context.Resources.TryGet(later, out _, out _));
        Assert.Single(context.Tasks);
        Assert.Single(context.Backend.Programs);
        context.RunNextTask();
        Assert.True(context.Resources.TryGet(later, out _, out _));
        Assert.Equal(1, context.Backend.CompileCalls);
        Assert.Equal(2, context.Backend.UploadedData.Count);
    }

    /// <summary>Registrations during preparation are processed in a subsequent snapshot without invalidating iteration.</summary>
    [Fact]
    public void RegistrationDuringPreparation_QueuesNextBoundary()
    {
        using var context = new IndicatorResourceTestContext();
        var later = Effect("test:later", shader: "later");
        context.Resources.Register(Effect("test:initial"));
        context.Backend.DuringValidation = () =>
        {
            context.Backend.DuringValidation = null;
            context.Resources.Register(later);
        };
        context.Resources.Initialize();
        Assert.False(context.Resources.TryGet(later, out _, out _));
        Assert.Single(context.Tasks);
        context.RunNextTask();
        Assert.True(context.Resources.TryGet(later, out _, out _));
        Assert.Equal(2, context.Backend.CompileCalls);
    }
    #endregion

    #region Failures and Retry
    /// <summary>Asset, compile, and linked-contract failures release partial programs and preserve unrelated resources.</summary>
    [Theory]
    [InlineData("registration")]
    [InlineData("compile")]
    [InlineData("validation")]
    public void ShaderFailure_IsIsolatedSharedAndRetriedOnlyOnReload(string failure)
    {
        using var context = new IndicatorResourceTestContext();
        string badName = ItemSlotIndicatorEffectDefinition.ShaderNamePrefix + "bad";
        var failures = failure switch
        {
            "registration" => context.Backend.RegistrationFailures,
            "compile" => context.Backend.CompileFailures,
            _ => context.Backend.ValidationFailures
        };
        failures.Add(badName);
        var bad = Effect("test:bad", shader: "bad");
        var variant = Effect("test:variant", shader: "bad");
        var good = Effect("test:good", shader: "good");
        context.Resources.Register(bad);
        context.Resources.Register(variant);
        context.Resources.Register(good);
        context.Resources.Initialize();
        var rectangle = context.Resources.Rectangle;
        Assert.False(context.Resources.TryGet(bad, out _, out _));
        Assert.False(context.Resources.TryGet(variant, out _, out _));
        Assert.True(context.Resources.TryGet(good, out var oldGood, out var goodMesh));
        Assert.Equal(2, context.Backend.Programs.Count);
        context.Backend.Programs[0].Verify(p => p.Dispose(), Times.Once);
        Assert.Equal(new[] { "test:bad", "test:variant" }, context.Backend.Failures.Select(f => f.Identity));

        failures.Clear();
        for (int draw = 0; draw < 250; draw++) Assert.False(context.Resources.TryGet(bad, out _, out _));
        context.Resources.Register(bad);
        Assert.Empty(context.Tasks);
        // A distinct registration sharing the failed program observes the same attempt, without recompilation.
        context.Resources.Register(Effect("test:third", shader: "bad"));
        context.RunNextTask();
        Assert.Equal(2, context.Backend.Programs.Count);
        Assert.Equal(3, context.Backend.Failures.Count);
        context.Reload();
        Assert.True(oldGood!.Disposed);
        Assert.True(context.Resources.TryGet(bad, out var newBad, out var badMesh));
        Assert.True(context.Resources.TryGet(variant, out var newVariant, out _));
        Assert.True(context.Resources.TryGet(good, out var newGood, out var newGoodMesh));
        Assert.Same(newBad, newVariant);
        Assert.NotSame(oldGood, newGood);
        Assert.Same(goodMesh, newGoodMesh);
        Assert.Same(goodMesh, badMesh);
        Assert.Same(rectangle, context.Resources.Rectangle);
        Assert.Equal(4, context.Backend.Programs.Count);
        Assert.Equal(2, context.Backend.Meshes.Count);
        context.Backend.Programs[0].Verify(p => p.Dispose(), Times.Once);
    }

    /// <summary>Mesh upload failures do not release a successful shared program or the separate rectangle quad.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MeshFailure_ReleasesPartialAllocationAndRetainsOtherGeometry(bool partial)
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var rectangle = context.Resources.Rectangle;
        context.Backend.FailNextUpload = !partial;
        context.Backend.ReturnPartialUpload = partial;
        var bad = Effect("test:bad", segments: 64);
        var good = Effect("test:good");
        context.Resources.Register(bad);
        context.Resources.Register(good);
        context.RunNextTask();
        Assert.False(context.Resources.TryGet(bad, out _, out _));
        Assert.True(context.Resources.TryGet(good, out _, out var goodMesh));
        Assert.Single(context.Backend.Programs);
        Assert.Single(context.Backend.Failures);
        Assert.Same(rectangle, context.Resources.Rectangle);
        if (partial) Assert.Equal(1, context.Backend.Meshes[1].DisposeCalls);
        for (int draw = 0; draw < 250; draw++) Assert.False(context.Resources.TryGet(bad, out _, out _));
        Assert.Equal(3, context.Backend.UploadedData.Count);
        context.Reload();
        Assert.True(context.Resources.TryGet(bad, out _, out _));
        Assert.True(context.Resources.TryGet(good, out _, out var newGoodMesh));
        Assert.Same(goodMesh, newGoodMesh);
        Assert.Same(rectangle, context.Resources.Rectangle);
        Assert.Equal(4, context.Backend.UploadedData.Count);
    }

    /// <summary>A failed rectangle allocation is not retried or relogged by later registrations and does not invalidate effects.</summary>
    [Fact]
    public void RectangleFailure_IsIndependentAndLoggedOncePerAttempt()
    {
        using var context = new IndicatorResourceTestContext();
        context.Backend.FailNextUpload = true;
        var effect = Effect("test:initial");
        context.Resources.Register(effect);
        context.Resources.Initialize();
        Assert.Null(context.Resources.Rectangle);
        Assert.True(context.Resources.TryGet(effect, out _, out _));
        context.Resources.Register(Effect("test:later"));
        context.RunNextTask();
        Assert.Single(context.Backend.Failures);
        Assert.Equal("rectangle", context.Backend.Failures[0].Identity);
        Assert.Equal(2, context.Backend.UploadedData.Count);
        context.Reload();
        Assert.NotNull(context.Resources.Rectangle);
        Assert.Equal(3, context.Backend.UploadedData.Count);
    }
    #endregion

    #region Reload and Disposal
    /// <summary>A failed reload cannot reuse an old successful program, and reports each failed effect once per attempt.</summary>
    [Fact]
    public void ReloadFailure_DropsOldHandlesAndPreservesUnrelatedResources()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = Effect("test:effect", shader: "affected");
        var good = Effect("test:good", shader: "unaffected");
        context.Resources.Register(effect);
        context.Resources.Register(good);
        context.Resources.Initialize();
        Assert.True(context.Resources.TryGet(effect, out var previous, out var previousMesh));
        var rectangle = context.Resources.Rectangle;
        context.Backend.CompileFailures.Add(effect.ShaderName);
        context.Reload();
        Assert.True(previous!.Disposed);
        Assert.False(context.Resources.TryGet(effect, out var failedProgram, out var failedMesh));
        Assert.Null(failedProgram);
        Assert.Null(failedMesh);
        Assert.True(context.Resources.TryGet(good, out _, out var goodMesh));
        Assert.Same(previousMesh, goodMesh);
        Assert.Same(rectangle, context.Resources.Rectangle);
        for (int draw = 0; draw < 250; draw++) Assert.False(context.Resources.TryGet(effect, out _, out _));
        Assert.Single(context.Backend.Failures);
        Assert.Equal(4, context.Backend.CompileCalls);
        context.Reload();
        Assert.False(context.Resources.TryGet(effect, out _, out _));
        Assert.Equal(2, context.Backend.Failures.Count);
        Assert.All(context.Backend.Failures, failure => Assert.Equal(effect.Id, failure.Identity));
        Assert.Equal(2, context.Backend.UploadedData.Count);
    }

    /// <summary>Repeated registry reload replaces only programs, using stable names and disposing each handle once.</summary>
    [Fact]
    public void RepeatedReloadAndShutdown_ReleaseOwnedResourcesExactlyOnce()
    {
        var context = new IndicatorResourceTestContext();
        var effect = Effect("test:effect");
        context.Resources.Register(effect);
        context.Resources.Initialize();
        Assert.True(context.Resources.TryGet(effect, out var oldProgram, out var oldMesh));
        for (int reload = 0; reload < 3; reload++) context.Reload();
        Assert.True(context.Resources.TryGet(effect, out var current, out var mesh));
        Assert.NotSame(oldProgram, current);
        Assert.Same(oldMesh, mesh);
        Assert.Equal(4, context.Backend.CompileCalls);
        Assert.Equal(2, context.Backend.Meshes.Count);
        Assert.All(context.Backend.RegisteredNames, name => Assert.Equal(effect.ShaderName, name));
        context.Resources.Register(Effect("test:pending", shader: "pending"));
        context.Dispose();
        context.Dispose();
        context.RunNextTask();
        context.Reload();
        Assert.False(context.Resources.TryGet(effect, out _, out _));
        Assert.Null(context.Resources.Rectangle);
        Assert.Equal(4, context.Backend.CompileCalls);
        Assert.All(context.Backend.Programs, program => program.Verify(p => p.Dispose(), Times.Once));
        Assert.All(context.Backend.Meshes, item => Assert.Equal(1, item.DisposeCalls));
        context.Events.VerifyRemove(e => e.ReloadShader -= It.IsAny<Vintagestory.API.Common.ActionBoolReturn>(), Times.Once);
        Assert.Throws<ObjectDisposedException>(() => context.Resources.Register(effect));
        Assert.Throws<ObjectDisposedException>(() => context.Resources.Initialize());
    }

    /// <summary>Explicit callbacks without preceding registry cleanup dispose formerly owned programs before replacement.</summary>
    [Fact]
    public void ReloadWithoutEngineDisposal_ReleasesPreviousProgram()
    {
        using var context = new IndicatorResourceTestContext();
        var effect = Effect("test:effect");
        context.Resources.Register(effect);
        context.Resources.Initialize();
        context.Events.Raise(e => e.ReloadShader += null);
        context.Backend.Programs[0].Verify(p => p.Dispose(), Times.Once);
        Assert.True(context.Resources.TryGet(effect, out _, out _));
        Assert.Equal(2, context.Backend.CompileCalls);
    }

    /// <summary>Cleanup before initialization does not subscribe callbacks or create resources.</summary>
    [Fact]
    public void PartialInitialization_CanBeDisposedRepeatedly()
    {
        var context = new IndicatorResourceTestContext();
        context.Resources.Register(Effect("test:effect"));
        context.Dispose();
        context.Dispose();
        Assert.Empty(context.Backend.Programs);
        Assert.Empty(context.Backend.Meshes);
        context.Events.VerifyAdd(e => e.ReloadShader += It.IsAny<Vintagestory.API.Common.ActionBoolReturn>(), Times.Never);
        context.Events.VerifyRemove(e => e.ReloadShader -= It.IsAny<Vintagestory.API.Common.ActionBoolReturn>(), Times.Never);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates distinct immutable appearances with explicitly controlled shared dependencies.</summary>
    private static ItemSlotIndicatorEffectDefinition Effect(string id, string shader = "shared", int segments = 16,
        Vector4 parameters = default, string domain = "test") =>
        new(id, domain, ItemSlotIndicatorEffectDefinition.ShaderNamePrefix + shader, segmentCount: segments, parameters: parameters);
    #endregion
}
