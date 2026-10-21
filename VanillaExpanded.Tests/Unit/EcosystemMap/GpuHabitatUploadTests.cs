using OpenTK.Graphics.OpenGL4;
using VanillaExpanded.EcosystemMap.Gpu;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks lazy batch state ownership, exact caller restoration and operation counts without executing GL.</summary>
public sealed class GpuHabitatUploadTests
{
    #region Public API
    #region Shared state
    /// <summary>Any number of resource operations shares six queries and one complete state restoration.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    public void ActiveBatchCapturesAndRestoresOnce(int operations)
    {
        var api = new RecordingUploadApi();
        var expected = api.Values.ToDictionary();
        var uploads = new GpuHabitatUploads(api);
        using (uploads.Begin())
        {
            for (int i = 0; i < operations; i++)
            {
                uploads.EnsureActive();
                Assert.Equal(0, api.Values[GetPName.PixelUnpackBufferBinding]);
                Assert.Equal(1, api.Values[GetPName.UnpackAlignment]);
                Assert.Equal(0, api.Values[GetPName.UnpackRowLength]);
                Assert.Equal(0, api.Values[GetPName.UnpackSkipRows]);
                Assert.Equal(0, api.Values[GetPName.UnpackSkipPixels]);
                api.Values[GetPName.TextureBinding2D] = 100 + i;
            }
            Assert.Equal(6, api.Queries);
            Assert.Equal(4, api.Stores);
            Assert.Equal(1, api.Buffers);
            Assert.Equal(0, api.Textures);
        }
        Assert.All(expected, value => Assert.Equal(value.Value, api.Values[value.Key]));
        Assert.Equal(6, api.Queries); Assert.Equal(8, api.Stores);
        Assert.Equal(2, api.Buffers); Assert.Equal(1, api.Textures);
    }
    /// <summary>Empty and cache-only scopes perform no queries, changes or restoration calls.</summary>
    [Fact]
    public void UnusedBatchesLeaveAllUploadStateUntouched()
    {
        var api = new RecordingUploadApi();
        var uploads = new GpuHabitatUploads(api);
        using (uploads.Begin()) { }
        using (uploads.Begin()) { }
        Assert.Equal(0, api.Queries + api.Stores + api.Buffers + api.Textures);
    }
    /// <summary>Allocation/transfer failures restore state and permit a fresh following batch.</summary>
    [Fact]
    public void OperationFailureRestoresAndDoesNotPoisonLaterScopes()
    {
        var api = new RecordingUploadApi();
        var expected = api.Values.ToDictionary();
        var uploads = new GpuHabitatUploads(api);
        Action failingTransfer = () =>
        {
            using var batch = uploads.Begin();
            uploads.EnsureActive();
            api.Values[GetPName.TextureBinding2D] = 999;
            throw new InvalidOperationException("simulated allocation or transfer failure");
        };
        Assert.Throws<InvalidOperationException>(failingTransfer);
        Assert.All(expected, value => Assert.Equal(value.Value, api.Values[value.Key]));
        using (uploads.Begin()) uploads.EnsureActive();
        Assert.Equal(12, api.Queries); Assert.Equal(2, api.Textures);
    }
    #endregion
    #region Scope safety
    /// <summary>Failed setup rolls back its captured state before any native texture allocation can happen.</summary>
    [Fact]
    public void SetupFailureRestoresWithoutEnteringNativeAllocation()
    {
        var api = new RecordingUploadApi { FailOnce = PixelStoreParameter.UnpackRowLength };
        var expected = api.Values.ToDictionary();
        var uploads = new GpuHabitatUploads(api);
        using (uploads.Begin())
            Assert.Throws<InvalidOperationException>(() => new GpuHabitatTexture(32, 32, false, uploads));
        Assert.All(expected, value => Assert.Equal(value.Value, api.Values[value.Key]));
        Assert.Equal(6, api.Queries); Assert.Equal(1, api.Textures);
        using (uploads.Begin()) uploads.EnsureActive();
        Assert.Equal(12, api.Queries);
    }
    /// <summary>Unscoped allocations and nested batches fail before changing GL ownership.</summary>
    [Fact]
    public void ScopeIdentityRejectsNestingAndStaleDisposal()
    {
        var api = new RecordingUploadApi();
        var uploads = new GpuHabitatUploads(api);
        Assert.Throws<InvalidOperationException>(() => new GpuHabitatTexture(1, 1, true, uploads));
        var first = uploads.Begin();
        Assert.Throws<InvalidOperationException>(() => uploads.Begin());
        uploads.EnsureActive(); first.Dispose();
        using var second = uploads.Begin();
        first.Dispose();
        uploads.EnsureActive();
        Assert.Equal(12, api.Queries);
    }
    /// <summary>A foreign thread cannot begin, establish or restore the creating graphics thread's batch.</summary>
    [Fact]
    public void ForeignThreadCannotDisposeAnActiveBatch()
    {
        var api = new RecordingUploadApi();
        var uploads = new GpuHabitatUploads(api);
        var batch = uploads.Begin(); uploads.EnsureActive();
        Exception? error = null;
        var worker = new Thread(() => error = Record.Exception(() => batch.Dispose()));
        worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(0, api.Textures);
        batch.Dispose();
        Assert.Equal(1, api.Textures);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Records meaningful nondefault caller values and supports one setup failure for rollback verification.</summary>
    private sealed class RecordingUploadApi : IHabitatUploadApi
    {
        internal readonly Dictionary<GetPName, int> Values = new()
        {
            [GetPName.TextureBinding2D] = 23, [GetPName.UnpackAlignment] = 8,
            [GetPName.UnpackRowLength] = 65, [GetPName.UnpackSkipRows] = 3,
            [GetPName.UnpackSkipPixels] = 4, [GetPName.PixelUnpackBufferBinding] = 7
        };
        internal int Queries, Stores, Buffers, Textures;
        internal PixelStoreParameter? FailOnce;
        /// <summary>Counts each actual state query made by the production scope.</summary>
        public int GetInteger(GetPName name) { Queries++; return Values[name]; }
        /// <summary>Maps transfer parameters to their recorded query values and optionally fails once during setup.</summary>
        public void PixelStore(PixelStoreParameter name, int value)
        {
            Stores++;
            if (FailOnce == name) { FailOnce = null; throw new InvalidOperationException("setup failure"); }
            Values[name switch
            {
                PixelStoreParameter.UnpackAlignment => GetPName.UnpackAlignment,
                PixelStoreParameter.UnpackRowLength => GetPName.UnpackRowLength,
                PixelStoreParameter.UnpackSkipRows => GetPName.UnpackSkipRows,
                PixelStoreParameter.UnpackSkipPixels => GetPName.UnpackSkipPixels,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            }] = value;
        }
        /// <summary>Records buffer setup/restoration without accessing a graphics context.</summary>
        public void BindUnpackBuffer(int value) { Buffers++; Values[GetPName.PixelUnpackBufferBinding] = value; }
        /// <summary>Records the caller's texture binding restoration.</summary>
        public void BindTexture(int value) { Textures++; Values[GetPName.TextureBinding2D] = value; }
    }
    #endregion
}
