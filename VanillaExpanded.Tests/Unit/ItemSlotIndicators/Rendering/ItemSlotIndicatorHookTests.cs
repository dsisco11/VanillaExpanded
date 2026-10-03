using System.Reflection;

using Moq;

using VanillaExpanded.ItemSlotIndicators;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Checks that the GUI hook forwards every original item argument and preserves engine item exceptions.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorHookTests
{
    #region Public API
    /// <summary>The intercepted call forwards slot identity, jittered position, item depth/size/color/dt, and all optional flags.</summary>
    [Fact]
    public void OriginalItemArgumentsAndExceptions_ArePreserved()
    {
        var renderer = new Mock<IRenderAPI>();
        var slot = new ItemSlot(null);
        var method = typeof(ItemSlotIndicatorPatch).GetMethod("RenderItemstackWithIndicator", BindingFlags.Static | BindingFlags.NonPublic)!;
        object[] arguments = [renderer.Object, slot, 13.25, -7.5, 93.0, 0.7f, unchecked((int)0xab123456), 0.02f, false, true, false];
        method.Invoke(null, arguments);
        renderer.Verify(r => r.RenderItemstackToGui(slot, 13.25, -7.5, 93, 0.7f, unchecked((int)0xab123456), 0.02f, false, true, false), Times.Once);
        var failure = new InvalidOperationException("Engine item failure.");
        renderer.Setup(r => r.RenderItemstackToGui(slot, 13.25, -7.5, 93, 0.7f, unchecked((int)0xab123456), 0.02f, false, true, false)).Throws(failure);
        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, arguments));
        Assert.Same(failure, exception.InnerException);
    }
    #endregion
}
