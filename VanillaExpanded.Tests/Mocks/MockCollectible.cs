using Moq;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// Factory methods for creating mock CollectibleObjects for testing.
/// </summary>
public static class MockCollectible
{
    /// <summary>
    /// Creates a mock CollectibleObject with the specified ID and optional light value.
    /// </summary>
    /// <param name="id">The collectible ID.</param>
    /// <param name="lightValue">The V (brightness) component of LightHsv. 0 = no light, >0 = light source.</param>
    public static Mock<CollectibleObject> Create(int id, byte lightValue = 0)
    {
        var mock = new Mock<CollectibleObject>();
        mock.Setup(c => c.Id).Returns(id);
        mock.Setup(c => c.LightHsv).Returns(new byte[] { 0, 0, lightValue });
        return mock;
    }

    /// <summary>
    /// Creates a mock CollectibleObject that is a light source.
    /// </summary>
    /// <param name="id">The collectible ID.</param>
    /// <param name="brightness">The brightness value (1-31).</param>
    public static Mock<CollectibleObject> CreateLightSource(int id, byte brightness = 20)
    {
        return Create(id, brightness);
    }

    /// <summary>
    /// Creates a mock CollectibleObject that is not a light source.
    /// </summary>
    public static Mock<CollectibleObject> CreateNonLightSource(int id)
    {
        return Create(id, 0);
    }
}
