using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Mocks;

/// <summary>
/// A mock Item implementation for unit testing.
/// Allows direct setting of Id and LightHsv fields.
/// </summary>
public class MockItem : Item
{
    private readonly int _id;

    public MockItem(int id, byte lightValue = 0, ICoreAPI? api = null)
    {
        _id = id;
        ItemId = id;
        LightHsv = new byte[] { 0, 0, lightValue };

        // Set the API if provided (needed for CollectibleObject.Equals to work)
        if (api is not null)
        {
            this.api = api;
        }
    }

    public override int Id => _id;

    /// <summary>
    /// Sets the API on this MockItem.
    /// Required for operations that compare item stacks (like inventory transfers).
    /// </summary>
    public void SetApi(ICoreAPI api)
    {
        this.api = api;
    }

    /// <summary>
    /// Creates a mock item that is a light source.
    /// </summary>
    public static MockItem CreateLightSource(int id, byte brightness = 20, ICoreAPI? api = null)
    {
        return new MockItem(id, brightness, api);
    }

    /// <summary>
    /// Creates a mock item that is not a light source.
    /// </summary>
    public static MockItem CreateNonLightSource(int id, ICoreAPI? api = null)
    {
        return new MockItem(id, 0, api);
    }
}
