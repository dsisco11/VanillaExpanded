using Moq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Presentation;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks the real presentation's upload/draw ordering and exception-safe acquisition scope.</summary>
public sealed class HabitatUploadPresentationTests
{
    #region Public API
    /// <summary>All ready resources share one upload lifetime ending before draw setup, including when acquisition throws.</summary>
    [Fact]
    public async Task UploadScopeEndsBeforeDrawingAndOnAcquisitionFailure()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 4, 4, 2));
        var calls = new List<string>();
        bool uploading = false, fail = false;
        int drawn = 0;
        var uploadScope = new Mock<IDisposable>();
        uploadScope.Setup(scope => scope.Dispose()).Callback(() => { Assert.True(uploading); uploading = false; calls.Add("upload-end"); });
        var drawScope = new Mock<IDisposable>();
        drawScope.Setup(scope => scope.Dispose()).Callback(() => calls.Add("draw-end"));
        var graphics = new Mock<IHabitatMapGraphics>();
        graphics.Setup(owner => owner.TryPrepare(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatUploadBudget>())).Returns(true);
        graphics.Setup(owner => owner.BeginUploads()).Returns(() =>
        { Assert.False(uploading); uploading = true; calls.Add("upload-begin"); return uploadScope.Object; });
        graphics.Setup(owner => owner.TryPrepare(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatUploadBudget>())).Callback(() =>
        { Assert.True(uploading); calls.Add("prepare"); if (fail) throw new InvalidOperationException("transfer failed"); }).Returns(true);
        graphics.Setup(owner => owner.Begin(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatRanges>(), It.IsAny<float[]>())).Returns(() =>
        { Assert.False(uploading); calls.Add("draw-begin"); return drawScope.Object; });
        graphics.Setup(owner => owner.Draw(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatMapTile>())).Callback(() =>
        { Assert.False(uploading); drawn++; calls.Add("draw"); });
        using var presentation = new HabitatMapPresentation(preparation, graphics.Object, error => throw error);
        var tiles = new[] { new HabitatMapTile(new(0, 0), 0, 0, 32, 32), new HabitatMapTile(new(1, 0), 32, 0, 32, 32) };
        var ranges = HabitatRanges.From(new HabitatRule("game:test", HabitatSpawnPath.Runtime, -20, 30, 0, 1, 0, 1, 0, 1, 0, 0, 2));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (drawn != tiles.Length)
        {
            drawn = 0; calls.Clear();
            presentation.Render(tiles, ranges, new float[16]);
            if (drawn != tiles.Length) await Task.Delay(1, timeout.Token);
        }
        Assert.Equal(new[] { "upload-begin", "prepare", "prepare", "upload-end", "draw-begin", "draw", "draw", "draw-end" }, calls);
        calls.Clear(); fail = true;
        Assert.Throws<InvalidOperationException>(() => presentation.Render(tiles, ranges, new float[16]));
        Assert.Equal(new[] { "upload-begin", "prepare", "upload-end" }, calls);
        Assert.False(uploading);
        // A failed frame does not poison the next acquisition/draw lifetime.
        calls.Clear(); fail = false;
        presentation.Render(tiles, ranges, new float[16]);
        Assert.Equal("draw-end", calls[^1]);
    }
    #endregion
}
