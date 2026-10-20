using Moq;
using VanillaExpanded.HudOverlays.Interaction;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Enters explicit native editing for fixtures that exercise movable HUD surfaces.</summary>
internal static class HudOverlayEditFixture
{
    #region Public API
    /// <summary>Opens a real cursor dialog and exposes it through the fixture's native GUI lists.</summary>
    public static HudOverlayEditDialog Open(Mock<ICoreClientAPI> api, Mock<IGuiAPI> gui, List<GuiDialog>? opened = null)
    {
        opened ??= new List<GuiDialog>();
        gui.SetupGet(value => value.OpenedGuis).Returns(opened);
        gui.SetupGet(value => value.LoadedGuis).Returns(gui.Object.LoadedGuis ?? new List<GuiDialog>());
        var dialog = new HudOverlayEditDialog(api.Object);
        dialog.TryOpen(false);
        opened.Add(dialog);
        return dialog;
    }
    #endregion
}
