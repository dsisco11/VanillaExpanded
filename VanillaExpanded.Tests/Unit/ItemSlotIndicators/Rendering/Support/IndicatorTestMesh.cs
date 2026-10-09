using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;

/// <summary>Records mesh lifecycle without invoking OpenGL.</summary>
internal sealed class IndicatorTestMesh(bool initialized = true) : MeshRef
{
    /// <summary>Gets whether the simulated upload completed.</summary>
    public override bool Initialized => initialized;
    /// <summary>Gets the number of ownership-release calls.</summary>
    internal int DisposeCalls { get; private set; }

    #region Public API
    /// <summary>Marks the mesh disposed while recording duplicate release attempts.</summary>
    public override void Dispose()
    {
        DisposeCalls++;
        base.Dispose();
    }
    #endregion
}
