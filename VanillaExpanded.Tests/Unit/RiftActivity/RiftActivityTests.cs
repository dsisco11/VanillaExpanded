using System.Drawing;
using HarmonyLib;
using Moq;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.RiftActivity;
using VanillaExpanded.Tests.Unit.BowAmmunition;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.RiftActivity;

/// <summary>Checks native packet mapping and cached presentation without graphics services.</summary>
[Collection("HudOverlayGeometry")]
public sealed class RiftActivityTests
{
    private static readonly AccessTools.FieldRef<ModSystemRiftWeather, CurrentPattern> Pattern =
        AccessTools.FieldRefAccess<ModSystemRiftWeather, CurrentPattern>("curPattern");

    #region Public API
    /// <summary>Unsynchronized, calm, disabled, and unsupported native state produces no icon.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("calm", null)]
    [InlineData("low", 0)]
    [InlineData("medium", 1)]
    [InlineData("high", 2)]
    [InlineData("veryhigh", 2)]
    [InlineData("apocalyptic", 3)]
    [InlineData("future-pattern", null)]
    public void ReadsNativePacket(string? code, int? expected)
    {
        var weather = new ModSystemRiftWeather();
        if (code != null) Pattern(weather) = new CurrentPattern { Code = code };
        Assert.Equal(expected, (int?)RiftActivitySource.Read(weather));
        weather.Enabled = false;
        Assert.Null(RiftActivitySource.Read(weather));
        Assert.Null(RiftActivitySource.Read(null));
    }

    /// <summary>Calm remains pollable and active categories select cached textures until visibility ends.</summary>
    [Fact]
    public void ActivityTransitionsAndCleanup()
    {
        var fixture = new BowAmmunitionSamplerTests.Fixture();
        fixture.Entity.Alive = true;
        var weather = new ModSystemRiftWeather();
        var loader = new Mock<IModLoader>();
        loader.Setup(value => value.GetModSystem<ModSystemRiftWeather>(true)).Returns(weather);
        fixture.Api.SetupGet(value => value.ModLoader).Returns(loader.Object);
        var renderer = new Mock<IRenderAPI>();
        int loads = 0;
        using var overlay = new RiftActivityOverlay(context =>
        {
            loads++;
            return Enumerable.Range(1, 4).Select(id => new LoadedTexture(context.Api)
                { TextureId = id, IgnoreUndisposed = true }).ToArray();
        });
        overlay.BeginSession(fixture.Api.Object);
        var preparation = new HudOverlayPreparationContext(fixture.Api.Object, 1, "en", 0);
        Assert.True(overlay.IsApplicable());
        overlay.Refresh(); overlay.Prepare(preparation);
        Assert.Equal(SizeF.Empty, overlay.Measure());
        Assert.Equal(0, loads);

        // Change the packet through each category and observe only mocked draw arguments.
        string[] categories = ["low", "medium", "high", "apocalyptic"];
        for (int index = 0; index < categories.Length; index++)
        {
            Pattern(weather) = new CurrentPattern { Code = categories[index] };
            Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
            overlay.Prepare(preparation);
            Assert.Equal(new SizeF(24, 24), overlay.Measure());
            Assert.Equal(HudOverlayChange.None, overlay.Refresh());
            renderer.Invocations.Clear();
            overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(), RectangleF.Empty, 0);
            var iconCall = Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50);
            Assert.Equal(index + 1, Assert.Single(renderer.Invocations, call => call.Method.Name == "RenderTexture" && (float)call.Arguments[5] == 50).Arguments[0]);
        }
        Assert.Equal(1, loads);
        Pattern(weather) = new CurrentPattern { Code = "calm" };
        Assert.NotEqual(HudOverlayChange.None, overlay.Refresh());
        Assert.True(overlay.IsApplicable());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        renderer.Invocations.Clear();
        overlay.Draw(renderer.Object, ElementBounds.Fixed(0, 0, 24, 24).WithEmptyParent(), RectangleF.Empty, 0);
        Assert.Empty(renderer.Invocations);

        Pattern(weather) = new CurrentPattern { Code = "low" };
        overlay.Refresh(); overlay.Prepare(preparation);
        overlay.SetEnabled(false);
        Assert.False(overlay.IsApplicable());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        overlay.SetEnabled(true);
        overlay.Refresh(); overlay.Prepare(preparation);
        fixture.Entity.Alive = false;
        Assert.False(overlay.IsApplicable());
        Assert.Equal(SizeF.Empty, overlay.Measure());
        fixture.Entity.Alive = true;
        overlay.Refresh(); overlay.Prepare(preparation);
        overlay.EndSession();
        Assert.False(overlay.IsApplicable());
        Assert.Equal(SizeF.Empty, overlay.Measure());
    }
    #endregion
}
