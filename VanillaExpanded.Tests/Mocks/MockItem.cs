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

    /// <summary>
    /// Creates a mock item that is valid bloomery fuel (charcoal-like).
    /// Fuel requires BurnTemperature >= 1200 and BurnDuration > 30.
    /// </summary>
    public static MockItem CreateBloomeryFuel(int id, ICoreAPI? api = null)
    {
        var item = new MockItem(id, 0, api);
        item.CombustibleProps = new CombustibleProperties
        {
            BurnTemperature = 1300, // Above 1200 threshold
            BurnDuration = 60,      // Above 30 threshold
        };
        return item;
    }

    /// <summary>
    /// Creates a mock item that is valid bloomery ore (iron ore-like).
    /// Ore requires SmeltedStack != null and MeltingPoint between 1000-1500.
    /// </summary>
    public static MockItem CreateBloomeryOre(int id, int smeltedRatio = 1, ICoreAPI? api = null)
    {
        var item = new MockItem(id, 0, api);
        item.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 1200,    // Between 1000 (MinTemp) and 1500 (MaxTemp)
            SmeltedRatio = smeltedRatio,
            SmeltedStack = new JsonItemStack { Type = EnumItemClass.Item, Code = new AssetLocation("game:ironbloom") }
        };
        return item;
    }

    /// <summary>
    /// Creates a mock item that is NOT valid for bloomery (no combustible properties).
    /// </summary>
    public static MockItem CreateNonCombustible(int id, ICoreAPI? api = null)
    {
        return new MockItem(id, 0, api);
    }

    /// <summary>
    /// Creates a mock item with combustible properties that don't meet bloomery requirements.
    /// (e.g., cooking item with low melting point)
    /// </summary>
    public static MockItem CreateLowTempCombustible(int id, ICoreAPI? api = null)
    {
        var item = new MockItem(id, 0, api);
        item.CombustibleProps = new CombustibleProperties
        {
            MeltingPoint = 200,     // Too low for bloomery (needs >= 1000)
            SmeltedStack = new JsonItemStack { Type = EnumItemClass.Item, Code = new AssetLocation("game:cookedfood") }
        };
        return item;
    }
}
