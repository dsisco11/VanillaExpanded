using Moq;

using VanillaExpanded.ItemSlotIndicators.Rendering;

using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;

/// <summary>Supplies explicit deferred preparation and engine-style reload callbacks for resource tests.</summary>
internal sealed class IndicatorResourceTestContext : IDisposable
{
    internal readonly Mock<IClientEventAPI> Events = new();
    internal readonly IndicatorTestBackend Backend = new();
    internal readonly Queue<Action> Tasks = [];
    internal readonly ItemSlotIndicatorResources Resources;

    #region Public API
    /// <summary>Creates a resource owner with observable event subscription and queued main-thread tasks.</summary>
    internal IndicatorResourceTestContext()
    {
        Events.Setup(e => e.EnqueueMainThreadTask(It.IsAny<Action>(), It.IsAny<string>()))
            .Callback<Action, string>((task, _) => Tasks.Enqueue(task));
        Resources = new ItemSlotIndicatorResources(Events.Object, Backend);
    }

    /// <summary>Runs one explicit registration preparation boundary.</summary>
    internal void RunNextTask() => Tasks.Dequeue()();

    /// <summary>Simulates registry disposal before invoking the public engine reload event.</summary>
    internal void Reload()
    {
        foreach (var program in Backend.Programs)
            if (!program.Object.Disposed) program.Object.Dispose();
        Events.Raise(e => e.ReloadShader += null);
    }

    /// <summary>Releases the tested client owner.</summary>
    public void Dispose() => Resources.Dispose();
    #endregion
}
