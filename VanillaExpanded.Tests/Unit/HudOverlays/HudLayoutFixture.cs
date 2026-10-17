using System.Drawing;
using Moq;
using VanillaExpanded.HudOverlays.Anchoring;
using VanillaExpanded.HudOverlays.Interaction;
using VanillaExpanded.HudOverlays.Registration;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Supplies observable native settings and mirrors for layout behavior tests.</summary>
internal sealed class HudLayoutFixture : IDisposable
{
    public readonly Mock<ICoreClientAPI> Api = new();
    public readonly Mock<IGuiAPI> Gui = new();
    public readonly Mock<ISettingsClass<string>> Strings = new();
    public readonly Dictionary<string, string> Values = new();
    public readonly Dictionary<string, Vec2i> Positions = new();
    public readonly HudOverlayRegistry Registry = new();
    public int Writes;
    #region Public API
    /// <summary>Connects a dictionary-backed native string setting and independent coordinate mirrors.</summary>
    public HudLayoutFixture()
    {
        Api.SetupGet(value => value.Gui).Returns(Gui.Object);
        BindSettings(Api, Strings, Values, () => Writes++);
        Gui.Setup(value => value.GetDialogPosition(It.IsAny<string>())).Returns((string key) => Positions.GetValueOrDefault(key)!);
        Gui.Setup(value => value.SetDialogPosition(It.IsAny<string>(), It.IsAny<Vec2i>()))
            .Callback<string, Vec2i>((key, position) => { if (position == null) Positions.Remove(key); else Positions[key] = position; });
    }
    /// <summary>Adds stable registration metadata without creating presentation resources.</summary>
    public HudOverlayRegistration Add(string id, string group = "test", string anchor = "screen")
    {
        if (!Registry.GetGroups().Any(value => value.Id == group))
            Registry.RegisterGroup(new HudOverlayGroup(group, new HudOverlayPlacement(anchor, HudOverlayPoint.LeftTop, HudOverlayPoint.LeftTop)));
        var registration = new HudOverlayRegistration(id, Mock.Of<IHudOverlay>(), () => true, group);
        Registry.Register(registration);
        return registration;
    }
    /// <summary>Shares an explicit native string-setting store with other integration fixtures.</summary>
    public static void BindSettings(Mock<ICoreClientAPI> api, Mock<ISettingsClass<string>> strings, Dictionary<string, string> values, Action? written = null)
    {
        var settings = new Mock<ISettings>();
        settings.SetupGet(value => value.String).Returns(strings.Object);
        api.SetupGet(value => value.Settings).Returns(settings.Object);
        strings.Setup(value => value.Get(It.IsAny<string>(), It.IsAny<string>())).Returns((string key, string fallback) => values.GetValueOrDefault(key, fallback)!);
        strings.SetupSet(value => value[It.IsAny<string>()] = It.IsAny<string>())
            .Callback<string, string>((key, value) => { values[key] = value; written?.Invoke(); });
    }
    /// <summary>Releases registrations after borrowed interaction state is no longer used.</summary>
    public void Dispose() => Registry.Dispose();
    #endregion
}
