using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Fakes;

/// <summary>
/// A fake Item implementation for unit testing.
/// Allows direct setting of Id and LightHsv fields.
/// </summary>
public class FakeItem : Item
{
    private readonly int _id;

    public FakeItem(int id, byte lightValue = 0)
    {
        _id = id;
        ItemId = id;
        LightHsv = new byte[] { 0, 0, lightValue };
    }

    public override int Id => _id;

    /// <summary>
    /// Creates a fake item that is a light source.
    /// </summary>
    public static FakeItem CreateLightSource(int id, byte brightness = 20)
    {
        return new FakeItem(id, brightness);
    }

    /// <summary>
    /// Creates a fake item that is not a light source.
    /// </summary>
    public static FakeItem CreateNonLightSource(int id)
    {
        return new FakeItem(id, 0);
    }
}
