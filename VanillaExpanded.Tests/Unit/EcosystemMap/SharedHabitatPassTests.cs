using Moq;
using VanillaExpanded.EcosystemMap.Catalogue;
using VanillaExpanded.EcosystemMap.Gpu;
using VanillaExpanded.EcosystemMap.Presentation;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks real preparation and presentation pass ordering without creating an external graphics context.</summary>
public sealed class SharedHabitatPassTests
{
    #region Public API
    /// <summary>All uploads precede one shared pass, with one cleanup even when a tile draw throws.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleTilesShareOneExceptionSafePass(bool failDraw)
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 4, 4, 2));
        var events = new List<string>();
        var graphics = new Mock<IHabitatMapGraphics>();
        var lifetime = new Mock<IDisposable>();
        lifetime.Setup(value => value.Dispose()).Callback(() => events.Add("end"));
        graphics.Setup(value => value.Prepare(It.IsAny<GpuHabitatReceipt>())).Callback(() => events.Add("upload"));
        graphics.Setup(value => value.Begin(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatRanges>(), It.IsAny<float[]>()))
            .Callback(() => events.Add("begin")).Returns(lifetime.Object);
        bool warmed = false;
        graphics.Setup(value => value.Draw(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatMapTile>())).Callback(() =>
        {
            events.Add("draw");
            if (warmed && failDraw) throw new InvalidOperationException("draw failed");
        });
        using var presentation = new HabitatMapPresentation(preparation, graphics.Object, error => throw error);
        var view = new[] { new HabitatMapTile(new(0, 0), 0, 0, 32, 32), new HabitatMapTile(new(1, 0), 32, 0, 32, 32) };
        var ranges = HabitatRanges.From(new HabitatRule("game:test", HabitatSpawnPath.Runtime, -20, 30, 0, 1, 0, 1, 0, 1, 0, 0, 2));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        do
        {
            events.Clear();
            presentation.Render(view, ranges, new float[16]);
            if (events.Count(value => value == "draw") == 2) break;
            await Task.Delay(1, timeout.Token);
        } while (true);
        warmed = true;
        events.Clear();
        if (failDraw) Assert.Throws<InvalidOperationException>(() => presentation.Render(view, ranges, new float[16]));
        else presentation.Render(view, ranges, new float[16]);
        Assert.Equal(failDraw ? new[] { "upload", "upload", "begin", "draw", "end" }
            : new[] { "upload", "upload", "begin", "draw", "draw", "end" }, events);
        events.Clear();
        presentation.Render(Array.Empty<HabitatMapTile>(), ranges, new float[16]);
        Assert.Empty(events);
    }

    /// <summary>Source revocation during acquisition cannot open a pass or publish stale textures.</summary>
    [Fact]
    public async Task AcquisitionInvalidationSkipsThePass()
    {
        using var fixture = new StorageFixture();
        await using var storage = fixture.Open();
        using var preparation = new GpuHabitatPreparation(storage, new(2, 4, 4, 2));
        var graphics = new Mock<IHabitatMapGraphics>();
        bool acquired = false;
        graphics.Setup(value => value.Prepare(It.IsAny<GpuHabitatReceipt>())).Callback(() => { acquired = true; preparation.Reset(); });
        using var presentation = new HabitatMapPresentation(preparation, graphics.Object, error => throw error);
        var view = new[] { new HabitatMapTile(default, 0, 0, 32, 32) };
        var ranges = HabitatRanges.From(new HabitatRule("game:test", HabitatSpawnPath.Runtime, -20, 30, 0, 1, 0, 1, 0, 1, 0, 0, 2));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!acquired)
        {
            presentation.Render(view, ranges, new float[16]);
            await Task.Delay(1, timeout.Token);
        }
        graphics.Verify(value => value.Begin(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatRanges>(), It.IsAny<float[]>()), Times.Never);
        graphics.Verify(value => value.Draw(It.IsAny<GpuHabitatReceipt>(), It.IsAny<HabitatMapTile>()), Times.Never);
    }
    #endregion
}
