using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Defines the shared pause-menu or explicit-dialog gate for every HUD editing surface.</summary>
internal static class HudOverlayEditing
{
    private static readonly Type? PauseMenuType = typeof(ShaderProgram).Assembly.GetType("Vintagestory.Client.NoObf.GuiDialogEscapeMenu");
    #region Public API
    /// <summary>Requires an explicit editing source and a visible HUD with a free pointer.</summary>
    public static bool IsActive(ICoreClientAPI api) => !api.HideGuis && api.Input?.MouseGrabbed == false
        && (api.Gui.OpenedGuis?.Any(dialog => dialog.IsOpened()
            && (IsPauseMenu(dialog) || dialog is HudOverlayEditDialog)) ?? false);

    /// <summary>Identifies the native escape menu independently of whether a multiplayer world can pause.</summary>
    public static bool IsPauseMenu(GuiDialog dialog) => PauseMenuType?.IsInstanceOfType(dialog) == true;

    /// <summary>Preserves other modal owners while allowing targeted HUD input outside the pause menu's bounds.</summary>
    public static bool BlocksAllInput(GuiDialog dialog) => !IsPauseMenu(dialog) && dialog.CaptureAllInputs();
    #endregion
}
