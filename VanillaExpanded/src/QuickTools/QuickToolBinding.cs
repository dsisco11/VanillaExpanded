using System;
using Vintagestory.API.Client;

namespace VanillaExpanded.QuickTools;

/// <summary>Snapshots a hold binding so rebinding and release checks use explicit physical inputs.</summary>
internal readonly record struct QuickToolBinding(int Primary, int Secondary, bool Ctrl, bool Alt, bool Shift, bool OnKeyUp)
{
    #region Binding snapshot
    /// <summary>Copies the mutable game mapping before starting an interaction.</summary>
    internal static QuickToolBinding From(KeyCombination? mapping)
        => mapping is null ? default : new(mapping.KeyCode, mapping.SecondKeyCode ?? 0, mapping.Ctrl, mapping.Alt, mapping.Shift, mapping.OnKeyUp);

    /// <summary>Rejects unbound, release-only, and ambiguous selection-button or unobservable mouse mappings.</summary>
    internal bool Supported => !OnKeyUp && SupportedKey(Primary) && (Secondary == 0 || SupportedKey(Secondary));

    /// <summary>Allows ordinary keys and the two nonselection mouse buttons with public held-state access.</summary>
    private static bool SupportedKey(int code)
        => code > (int)GlKeys.Unknown && code < KeyCombination.MouseStart
            || code == KeyCombination.MouseStart + 1 || code == KeyCombination.MouseStart + 2;
    #endregion

    #region Physical state
    /// <summary>Checks the opening combination, including required modifiers, without inspecting inventory.</summary>
    internal bool IsHeld(Func<int, bool> down)
        => Supported && down(Primary) && (Secondary == 0 || down(Secondary))
            && (!Ctrl || down((int)GlKeys.ControlLeft) || down((int)GlKeys.ControlRight))
            && (!Alt || down((int)GlKeys.AltLeft) || down((int)GlKeys.AltRight))
            && (!Shift || down((int)GlKeys.ShiftLeft) || down((int)GlKeys.ShiftRight));

    /// <summary>Requires all activation keys to be released before permitting another press.</summary>
    internal bool AnyActivationDown(Func<int, bool> down)
        => Primary > 0 && down(Primary) || Secondary > 0 && down(Secondary);
    #endregion
}
