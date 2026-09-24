using System;
using VanillaExpanded.RadialMenu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VanillaExpanded.QuickTools;

/// <summary>Adapts client input and player lifecycle to the quick-tool menu coordinator.</summary>
internal sealed class QuickToolClientIntegration : IRenderer
{
    private const string HotkeyCode = "ve.quickTool";
    private readonly ICoreClientAPI api;
    private readonly QuickToolCandidateCache cache;
    private readonly QuickToolMenuController controller;
    private readonly long tickId;
    private readonly ActionConsumable<KeyCombination> hotkeyHandler;
    private bool disposed;
    private bool contextAvailable;

    /// <summary>Registers the unbound inventory hotkey and separates input polling from cache refreshes.</summary>
    internal QuickToolClientIntegration(ICoreClientAPI api, QuickToolClientOperations equipment)
    {
        this.api = api;
        contextAvailable = api.PlayerReadyFired;
        cache = new QuickToolCandidateCache(ScheduleRefresh, message => api.Logger.Warning(message), api.CollectibleTagRegistry);
        controller = new QuickToolMenuController(equipment, cache, api.ModLoader.GetModSystem<RadialMenuSystem>(),
            api.Event, IsReady,
            () => (api.World.Player?.InventoryManager, api.World.Player?.Entity?.LeftHandItemSlot),
            () => QuickToolBinding.From(api.Input.GetHotKeyByCode(HotkeyCode)?.CurrentMapping),
            IsDown, () => Vintagestory.Client.ScreenManager.Platform?.IsFocused == true,
            () => VanillaExpandedModSystem.Config.QuickToolSelectOnRelease,
            key => Lang.Get(Constants.ModId + ":" + key),
            message => api.TriggerIngameError(this, "quicktool", message),
            () => api.World.Player?.Entity);
        api.Input.RegisterHotKey(HotkeyCode, Lang.Get(Constants.ModId + ":hotkey-quick-tool"), GlKeys.Unknown, HotkeyType.InventoryHotkeys);
        hotkeyHandler = _ => controller.Press();
        api.Input.SetHotKeyHandler(HotkeyCode, hotkeyHandler);
        api.Event.KeyUp += OnKeyUp;
        api.Event.MouseUp += OnMouseUp;
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.PlayerEntitySpawn += OnPlayerSpawn;
        api.Event.PlayerEntityDespawn += OnPlayerDespawn;
        api.Event.AfterActiveSlotChanged += OnActiveSlotChanged;
        api.Event.PauseResume += OnPauseResume;
        api.Event.LeaveWorld += OnLeaveWorld;
        tickId = api.Event.RegisterGameTickListener(OnTick, 250);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "quick-tool-input");
    }

    #region Input
    /// <inheritdoc />
    public double RenderOrder => 0;
    /// <inheritdoc />
    public int RenderRange => int.MaxValue;
    /// <summary>Checks held keys and focus without resolving items or scanning inventory.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => controller.PollInput();

    /// <summary>Reads raw keys and supported mouse buttons even while the dialog captures keyboard events.</summary>
    private bool IsDown(int code)
    {
        if (code >= KeyCombination.MouseStart)
            return (code - KeyCombination.MouseStart) switch
            {
                0 => api.Input.MouseButton.Left,
                1 => api.Input.MouseButton.Middle,
                2 => api.Input.MouseButton.Right,
                _ => false
            };
        bool[] keys = api.Input.KeyboardKeyStateRaw;
        return code > 0 && code < keys.Length && keys[code];
    }

    /// <summary>Uses the release event before the engine has necessarily updated its raw array.</summary>
    private void OnKeyUp(KeyEvent args) => controller.PollInput(args.KeyCode);
    /// <summary>Maps mouse release events to the engine's hotkey key-code range.</summary>
    private void OnMouseUp(MouseEvent args) => controller.PollInput(KeyCombination.MouseStart + (int)args.Button);
    #endregion

    #region Context and lifecycle
    /// <summary>Requires an enabled feature and a living, initialized local player.</summary>
    private bool IsReady() => !disposed && contextAvailable && VanillaExpandedModSystem.Config.EnableQuickTools
        && api.PlayerReadyFired && api.World.Player?.Entity?.Alive == true;

    /// <summary>Defers one coalesced dirty burst until the native inventory operation has returned.</summary>
    private void ScheduleRefresh() => api.Event.EnqueueMainThreadTask(() =>
    {
        if (!disposed) controller.RefreshContext();
    }, "quick-tool-candidates");

    /// <summary>Checks inventory topology and pending candidate work independently of render frequency.</summary>
    private void OnTick(float deltaTime) => controller.RefreshContext();
    /// <summary>Binds inventories when the local world finishes loading.</summary>
    private void OnLevelFinalize()
    {
        contextAvailable = true;
        controller.RefreshContext();
    }
    /// <summary>Rebinds only for the local player, ignoring remote player spawn notifications.</summary>
    private void OnPlayerSpawn(IClientPlayer player)
    {
        if (player.PlayerUID != api.World.Player?.PlayerUID) return;
        contextAvailable = true;
        controller.RefreshContext();
    }
    /// <summary>Clears local history when the local player entity leaves the world.</summary>
    private void OnPlayerDespawn(IClientPlayer player)
    {
        if (player.PlayerUID != api.World.Player?.PlayerUID) return;
        contextAvailable = false;
        controller.ClearContext();
    }
    /// <summary>Cancels the visible interaction; equipment and cache owners process their own slot-change events.</summary>
    private void OnActiveSlotChanged(ActiveSlotChangeEventArgs args) => controller.CancelMenu();
    /// <summary>Closes the menu on pause without clearing restoration history.</summary>
    private void OnPauseResume(bool paused)
    {
        if (paused) controller.CancelMenu();
    }
    /// <summary>Releases all player and candidate references on world exit.</summary>
    private void OnLeaveWorld()
    {
        // Deferred cache tasks may run before PlayerReadyFired changes; prevent rebinding the departing world.
        contextAvailable = false;
        controller.ClearContext();
    }
    /// <summary>Applies feature availability while retaining the registered hotkey for re-enablement.</summary>
    internal void ApplyConfig()
    {
        if (IsReady()) controller.RefreshContext();
        else controller.ClearContext();
    }

    /// <summary>Releases every host subscription and all menu/cache state before the equipment owner is disposed.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterGameTickListener(tickId);
        api.Event.KeyUp -= OnKeyUp;
        api.Event.MouseUp -= OnMouseUp;
        api.Event.LevelFinalize -= OnLevelFinalize;
        api.Event.PlayerEntitySpawn -= OnPlayerSpawn;
        api.Event.PlayerEntityDespawn -= OnPlayerDespawn;
        api.Event.AfterActiveSlotChanged -= OnActiveSlotChanged;
        api.Event.PauseResume -= OnPauseResume;
        api.Event.LeaveWorld -= OnLeaveWorld;
        HotKey? hotkey = api.Input.GetHotKeyByCode(HotkeyCode);
        if (hotkey?.Handler == hotkeyHandler) hotkey.Handler = null;
        controller.Dispose();
        cache.Dispose();
    }
    #endregion
}
