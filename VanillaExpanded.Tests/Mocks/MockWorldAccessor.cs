using Moq;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock wrapper for IWorldAccessor that encapsulates Moq setup for unit testing.
/// </summary>
public class MockWorldAccessor
{
    public Mock<IWorldAccessor> Mock { get; }
    public IWorldAccessor Object => Mock.Object;

    public MockBlockAccessor BlockAccessor { get; }
    public Mock<ILogger> Logger { get; }

    public MockWorldAccessor(EnumAppSide side = EnumAppSide.Server)
    {
        Mock = new Mock<IWorldAccessor>();
        BlockAccessor = new MockBlockAccessor();
        Logger = new Mock<ILogger>();

        SetupSide(side);
        SetupBlockAccessor();
        SetupLogger();
    }

    /// <summary>
    /// Creates a server-side world accessor.
    /// </summary>
    public static MockWorldAccessor ServerSide() => new(EnumAppSide.Server);

    /// <summary>
    /// Creates a client-side world accessor.
    /// </summary>
    public static MockWorldAccessor ClientSide() => new(EnumAppSide.Client);

    /// <summary>
    /// Registers a block entity at the specified position.
    /// </summary>
    public MockWorldAccessor WithBlockEntity(BlockPos pos, BlockEntity entity)
    {
        BlockAccessor.WithBlockEntity(pos, entity);
        return this;
    }

    /// <summary>
    /// Configures the mock to play sounds (verifiable).
    /// </summary>
    public MockWorldAccessor SetupPlaySound()
    {
        Mock.Setup(w => w.PlaySoundAt(
            It.IsAny<AssetLocation>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<IPlayer>(),
            It.IsAny<bool>(),
            It.IsAny<float>(),
            It.IsAny<float>()
        )).Verifiable();
        return this;
    }

    /// <summary>
    /// Verifies that PlaySoundAt was called.
    /// </summary>
    public void VerifyPlaySoundAtCalled(Times times)
    {
        Mock.Verify(w => w.PlaySoundAt(
            It.IsAny<AssetLocation>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<double>(),
            It.IsAny<IPlayer>(),
            It.IsAny<bool>(),
            It.IsAny<float>(),
            It.IsAny<float>()
        ), times);
    }

    private void SetupSide(EnumAppSide side)
    {
        Mock.Setup(w => w.Side).Returns(side);
    }

    private void SetupBlockAccessor()
    {
        Mock.Setup(w => w.BlockAccessor).Returns(BlockAccessor.Object);
    }

    private void SetupLogger()
    {
        Mock.Setup(w => w.Logger).Returns(Logger.Object);
    }
}
