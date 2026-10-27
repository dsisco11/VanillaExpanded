using System;
using Vintagestory.API.Client;
namespace VanillaExpanded.HudOverlays.Rendering;
/// <summary>Routes one native dynamic composer to its prepared group without owning overlay resources.</summary>
internal sealed class HudOverlayGroupElement(ICoreClientAPI api, ElementBounds bounds, Action<float> draw) : GuiElement(api, bounds)
{
    #region Public API
    /// <summary>Draws prepared group content in native composer order.</summary>
    public override void RenderInteractiveElements(float deltaTime) => draw(deltaTime);
    /// <summary>Leaves passive content presses available to native editing controls.</summary>
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args) { }
    #endregion
}
