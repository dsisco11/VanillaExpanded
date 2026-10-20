using System.Reflection;
using System.Runtime.CompilerServices;
using VanillaExpanded.EcosystemMap.Presentation;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Checks initial native tab defaults and later recomposition without constructing a graphics context.</summary>
public sealed class EcosystemTabStateTests
{
    #region Public API
    /// <summary>Only this layer's tab follows its activation state; the initial all-active defaults cannot enable its button.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompositionUsesActualLayerStateAndLeavesOtherTabsAlone(bool active)
    {
        var dialog = (GuiDialogWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(GuiDialogWorldMap));
        var groups = new List<string> { "terrain", "vanillaexpandedecosystem", "ownedcreatures" };
        var states = new List<GuiTab> { new() { Active = true }, new() { Active = !active }, new() { Active = true } };
        typeof(GuiDialogWorldMap).GetField("tabnames", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, groups);
        typeof(GuiDialogWorldMap).GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, states);
        GuiTab[] widgetTabs = states.ToArray();
        EcosystemTabState.Synchronize(dialog, "vanillaexpandedecosystem", active);
        Assert.Equal(active, widgetTabs[1].Active);
        Assert.True(widgetTabs[0].Active);
        Assert.True(widgetTabs[2].Active);
        EcosystemTabState.Synchronize(dialog, "absent-group", !active);
        Assert.Equal(active, widgetTabs[1].Active);
    }
    #endregion
}
