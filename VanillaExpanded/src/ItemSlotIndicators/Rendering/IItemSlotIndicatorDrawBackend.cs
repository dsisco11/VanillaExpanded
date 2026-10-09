using System;

using VanillaExpanded.ItemSlotIndicators.Animation;
using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Separates draw orchestration and fallback policy from the engine GUI and graphics-state adapter.</summary>
internal interface IItemSlotIndicatorDrawBackend : IDisposable
{
    #region Public API
    /// <summary>Gets whether the active engine GUI program and current GUI matrices support this draw.</summary>
    bool Supported { get; }
    /// <summary>Captures depth/cull state before applying the slot-grid indicator state; partial entry must be restorable.</summary>
    void Begin();
    /// <summary>Submits an effect using prepared resources and the current shared snapshot.</summary>
    void Effect(IShaderProgram program, MeshRef mesh, ItemSlotIndicatorDrawInput input,
        ItemSlotIndicatorEffectDefinition definition, ItemSlotIndicatorFrameSnapshot frame);
    /// <summary>Submits a texture-free ordinary rectangle using the host GUI program.</summary>
    void Rectangle(MeshRef mesh, ItemSlotIndicatorDrawInput input);
    /// <summary>Draws a rounded, shaded bar using the engine durability-bar composition helpers.</summary>
    void DurabilityBar(ItemSlotIndicatorDrawInput input, float guiScale);
    /// <summary>Restores the slot-grid GUI contract and inherited depth/cull state before fallback or engine item drawing.</summary>
    void Restore();
    /// <summary>Reports a host rectangle/state failure once without throwing into normal item rendering.</summary>
    void ReportFailure(Exception exception);
    #endregion
}
