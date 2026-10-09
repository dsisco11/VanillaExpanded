using System.Numerics;
using System.Reflection;
using Moq;
using VanillaExpanded.ItemSlotIndicators;
using VanillaExpanded.ItemSlotIndicators.Rendering;
using VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering.Support;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.ItemSlotIndicators.Rendering;

/// <summary>Exercises the real slot hook to ensure inset item clipping cannot hide perimeter outlines.</summary>
[Trait("Category", "Unit")]
public sealed class ItemSlotIndicatorOutlineClipTests
{
    #region Public API
    /// <summary>The engine item draws inside its clip; outline submission follows PopScissor exactly once.</summary>
    [Fact]
    public void Outline_DrawsAfterItemScissorIsPopped()
    {
        using var context = new IndicatorResourceTestContext();
        context.Resources.Initialize();
        var order = new List<string>();
        var backend = new Mock<IItemSlotIndicatorDrawBackend>();
        backend.SetupGet(b => b.Supported).Returns(true);
        backend.Setup(b => b.Rectangle(It.IsAny<MeshRef>(), It.IsAny<ItemSlotIndicatorDrawInput>()))
            .Callback(() => order.Add("outline"));
        backend.Setup(b => b.Restore()).Callback(() => order.Add("restore"));
        using var draw = new ItemSlotIndicatorRenderer(context.Resources, backend.Object, () => 48, () => 1);
        var slot = new ItemSlot(null);
        var sample = new ItemSlotIndicator(0.5f, Vector4.One);
        var provider = new Mock<IItemSlotIndicatorProvider>();
        provider.Setup(p => p.TryGetIndicator(slot, out sample)).Returns(true);
        var system = new ItemSlotIndicatorSystem();
        system.Register(provider.Object, defaultStyle: ItemSlotIndicatorRenderingStyle.SlotOutline);
        var rendererProperty = typeof(ItemSlotIndicatorSystem).GetProperty("Renderer", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var activeProperty = typeof(ItemSlotIndicatorSystem).GetProperty("Active", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = activeProperty.GetValue(null);
        rendererProperty.SetValue(system, draw);
        activeProperty.SetValue(null, system);
        try
        {
            var render = new Mock<IRenderAPI>();
            render.Setup(r => r.RenderItemstackToGui(slot, 24, 24, 90, 48, -1, 0, true, false, true))
                .Callback(() => order.Add("item"));
            render.Setup(r => r.PopScissor()).Callback(() => order.Add("pop"));
            var itemHook = typeof(ItemSlotIndicatorPatch).GetMethod("RenderItemstackWithIndicator", BindingFlags.Static | BindingFlags.NonPublic)!;
            var popHook = typeof(ItemSlotIndicatorPatch).GetMethod("PopScissorWithOutline", BindingFlags.Static | BindingFlags.NonPublic)!;
            itemHook.Invoke(null, [render.Object, slot, 24d, 24d, 90d, 48f, -1, 0f, true, false, true]);
            Assert.Equal(new[] { "item" }, order);
            popHook.Invoke(null, [render.Object]);
            Assert.Equal(new[] { "item", "pop", "outline", "outline", "outline", "outline", "restore" }, order);
            popHook.Invoke(null, [render.Object]);
            Assert.Equal(4, order.Count(value => value == "outline"));
            Assert.Equal("pop", order[^1]);
            provider.Verify(p => p.TryGetIndicator(slot, out sample), Times.Once);
        }
        finally { activeProperty.SetValue(null, previous); }
    }
    #endregion
}
