using System;
using System.Collections.Generic;
using VanillaExpanded.HudOverlays.Layout;
namespace VanillaExpanded.HudOverlays.Rendering;
/// <summary>Owns a session's native compositions and their passive visibility lifetime.</summary>
internal interface IHudOverlayHost : IDisposable
{
    Action? BeforeRender { get; set; }
    /// <summary>Synchronizes prepared native group hierarchies without sampling feature state.</summary>
    void Synchronize(IReadOnlyDictionary<string, HudOverlayGroupLayout> layouts);
    /// <summary>Opens through the native GUI registration boundary without focus.</summary>
    bool TryOpen(bool withFocus);
    /// <summary>Closes through the native GUI deregistration boundary.</summary>
    bool TryClose();
}
