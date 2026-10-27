using System;
using System.Drawing;
using VanillaExpanded.HudOverlays.Configuration;
using VanillaExpanded.HudOverlays.Registration;
using VanillaExpanded.HudOverlays.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaExpanded.Shelter;

internal sealed class ShelterOverlay : IHudOverlay, IHudOverlayVisibilityTransition
{
    private readonly ShelterPresentation presentation;
    private readonly System.Func<BlockPos, Room?>? findRoom;
    private ICoreClientAPI? api;
    private RoomRegistry? rooms;
    private bool enabled = true, disposed, styleChanged;

    internal bool Sheltered { get; private set; }
    public HudOverlayVisibilityTransition Visibility => presentation.Visibility;

    public ShelterOverlay(ShelterPresentation? presentation = null, System.Func<BlockPos, Room?>? findRoom = null)
    {
        this.presentation = presentation ?? new();
        this.findRoom = findRoom;
    }

    public void BeginSession(ICoreClientAPI api)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(api);
        if (this.api != null) throw new InvalidOperationException("A shelter session is already bound.");
        this.api = api;
        if (findRoom == null) rooms = api.ModLoader.GetModSystem<RoomRegistry>();
    }

    public void SetStyle(HudIndicatorStyle style) => styleChanged |= presentation.SetStyle(style);

    public bool SetEnabled(bool value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        enabled = value;
        if (!value) HidePresentation();
        return value;
    }

    public bool IsApplicable()
    {
        var player = api?.World.Player;
        bool applicable = enabled && player?.Entity?.Alive == true
            && player.WorldData.CurrentGameMode is not (EnumGameMode.Creative or EnumGameMode.Spectator);
        if (!applicable) HidePresentation();
        return applicable;
    }

    public HudOverlayChange Refresh()
    {
        if (!IsApplicable()) return HudOverlayChange.None;
        var position = api!.World.Player.Entity.Pos.AsBlockPos;
        var room = findRoom != null ? findRoom(position) : rooms!.GetRoomForPosition(position);
        // Match native body-temperature protection, including roofed caves with exits.
        bool next = room != null && (room.ExitCount == 0 || room.SkylightCount < room.NonSkylightCount);
        if (next == Sheltered && !styleChanged) return HudOverlayChange.None;
        styleChanged = false;
        Sheltered = next;
        if (!next) presentation.Hide();
        return HudOverlayChange.Presentation | HudOverlayChange.Measurement;
    }

    public INativeView CreateView(ICoreClientAPI api, NativeViewRole role) => presentation.CreateView(api);
    public void BeginFrame(float deltaTime) { }
    public void Prepare(HudOverlayPreparationContext context)
    {
        if (Sheltered) presentation.Prepare(context);
    }
    public SizeF Measure() => presentation.Size;

    public void HidePresentation()
    {
        Sheltered = false;
        presentation.Hide();
    }

    public void EndSession()
    {
        api = null;
        rooms = null;
        Sheltered = false;
        presentation.Reset();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        EndSession();
        presentation.Dispose();
    }
}
