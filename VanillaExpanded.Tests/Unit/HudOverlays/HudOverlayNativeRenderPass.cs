using System.Reflection;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Simulates the native compositor's passive artwork and foreground menu callbacks.</summary>
internal static class HudOverlayNativeRenderPass
{
    #region Public API
    /// <summary>Draws the owned passive dialog before its separate input and popup owner.</summary>
    public static void Render(HudOverlayHost host, float deltaTime)
    {
        var artwork = (GuiDialog)typeof(HudOverlayHost).GetField("artworkDialog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        artwork.OnRenderGUI(deltaTime);
        host.OnRenderGUI(deltaTime);
    }
    #endregion
}
