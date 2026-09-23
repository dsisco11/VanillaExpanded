using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.RadialMenu;

/// <summary>Exposes the reusable client menu while its dialog owns interaction and graphics lifetime.</summary>
public sealed class RadialMenuSystem : ModSystem, IRadialMenu
{
    #region Lifecycle
    private ICoreClientAPI? capi;
    private RadialMenuDialog? dialog;
    private bool pendingSelectionRelease;

    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <inheritdoc />
    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        dialog = new RadialMenuDialog(api);
        api.Event.ReloadShader += ReloadShader;
        api.Event.LeaveWorld += Cancel;
        api.Event.PauseResume += OnPauseResume;
        api.Event.MouseUp += OnMouseUp;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (capi is not null)
        {
            capi.Event.ReloadShader -= ReloadShader;
            capi.Event.LeaveWorld -= Cancel;
            capi.Event.PauseResume -= OnPauseResume;
            capi.Event.MouseUp -= OnMouseUp;
        }
        dialog?.Dispose();
        dialog = null;
        capi = null;
        base.Dispose();
    }
    #endregion

    #region Public API
    /// <summary>Opens caller-supplied generic entries at fixed positions for one interaction.</summary>
    public bool Open(RadialMenuLayout layout, IEnumerable<RadialMenuEntry> entries, Action<string> selected, Action cancelled)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(cancelled);
        if (dialog is null) return false;
        if (pendingSelectionRelease)
        {
            if (capi?.Input.MouseButton.Left == true) return false;
            pendingSelectionRelease = false;
        }
        return dialog.Open(layout, entries, id =>
        {
            // Keep the release guard even if the caller closes this dialog in the callback.
            pendingSelectionRelease = true;
            selected(id);
        }, cancelled);
    }

    /// <summary>Refreshes entry content without moving wedges or uploading geometry.</summary>
    public void UpdateEntries(IEnumerable<RadialMenuEntry> entries) => dialog?.UpdateEntries(entries);

    /// <summary>Cancels an open menu on release, focus loss, feature disablement, or world exit.</summary>
    public void Cancel() => dialog?.Cancel();

    /// <summary>Gets whether the menu dialog currently owns input.</summary>
    public bool IsOpen => dialog?.IsOpened() == true;
    #endregion

    #region Resources
    /// <summary>Reloads the menu shader on an engine resource refresh.</summary>
    private bool ReloadShader() => dialog?.ReloadShader() ?? true;

    /// <summary>Releases input when the game pauses.</summary>
    private void OnPauseResume(bool isPaused)
    {
        if (isPaused) Cancel();
    }
    /// <summary>Consumes the matching release after a selection closes the dialog.</summary>
    private void OnMouseUp(MouseEvent args)
    {
        if (!pendingSelectionRelease || args.Button != EnumMouseButton.Left) return;
        args.Handled = true;
        pendingSelectionRelease = false;
    }
    #endregion
}



