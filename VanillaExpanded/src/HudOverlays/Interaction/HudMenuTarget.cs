using System;
using VanillaExpanded.HudOverlays.Registration;

namespace VanillaExpanded.HudOverlays.Interaction;

/// <summary>Borrows a stable registration and live effective-container permission without owning feature lifetime.</summary>
internal sealed record HudMenuTarget(HudOverlayRegistration Registration, Func<bool> IsAvailable,
    Func<bool> IsLocked, Action<bool> SetLocked, bool CanChangeLock = true);
