using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock Item implementation for unit testing.
/// Allows direct setting of Id and LightHsv fields.
/// </summary>
public class MockItem : Item
{
    private readonly int _id;

    public MockItem(int id, byte lightValue = 0)
    {
        _id = id;
        ItemId = id;
        LightHsv = new byte[] { 0, 0, lightValue };
    }

    public override int Id => _id;

    /// <summary>
    /// Creates a mock item that is a light source.
    /// </summary>
    public static MockItem CreateLightSource(int id, byte brightness = 20)
    {
        return new MockItem(id, brightness);
    }

    /// <summary>
    /// Creates a mock item that is not a light source.
    /// </summary>
    public static MockItem CreateNonLightSource(int id)
    {
        return new MockItem(id, 0);
    }
}
