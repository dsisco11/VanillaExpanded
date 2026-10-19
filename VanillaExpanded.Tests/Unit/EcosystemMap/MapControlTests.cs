using System.Reflection;
using System.Runtime.CompilerServices;
using Moq;
using VanillaExpanded.EcosystemMap.Presentation;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Exercises native composer identity and mode transitions without composing textures or opening the game.</summary>
public sealed class MapControlTests
{
    #region Public API
    /// <summary>Active full-map controls reject HUD mode, closed dialogs and replaced composers while disposing only their entry.</summary>
    [Fact]
    public void NativeComposerEligibilityAndDetachmentFollowCurrentDialog()
    {
        var api = Mock.Of<ICoreClientAPI>();
        var dialog = (GuiDialogWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(GuiDialogWorldMap));
        dialog.Composers = new GuiDialog.DlgComposers(dialog);
        GuiComposer full = Composer(api), panel = Composer(api), replacement = Composer(api);
        var map = (GuiElementMap)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementMap));
        map.Bounds = ElementBounds.Fixed(0, 0, 256, 256);
        full.AddInteractiveElement(map, "mapElem");
        dialog.SingleComposer = full;
        Set(typeof(GuiDialog), dialog, "opened", true);
        Set(typeof(GuiDialogWorldMap), dialog, "dialogType", EnumDialogType.Dialog);
        var controls = new EcosystemMapControls(api, new(), "test");
        Set(typeof(EcosystemMapControls), controls, "dialog", dialog);
        Set(typeof(EcosystemMapControls), controls, "composer", panel);
        Set(typeof(EcosystemMapControls), controls, "fullMapComposer", full);
        dialog.Composers["worldmap-layer-test"] = panel;
        Assert.True(controls.Refresh(true));
        Assert.True(panel.Enabled);
        Assert.True(controls.IsSurface(map));
        Assert.False(controls.IsSurface((GuiElementMap)RuntimeHelpers.GetUninitializedObject(typeof(GuiElementMap))));
        Assert.False(controls.Refresh(false));
        Assert.False(panel.Enabled);
        Set(typeof(GuiDialogWorldMap), dialog, "dialogType", EnumDialogType.HUD);
        Assert.False(controls.Refresh(true));
        Set(typeof(GuiDialogWorldMap), dialog, "dialogType", EnumDialogType.Dialog);
        Set(typeof(GuiDialog), dialog, "opened", false);
        Assert.False(controls.Refresh(true));
        Set(typeof(GuiDialog), dialog, "opened", true);
        dialog.SingleComposer = replacement;
        Assert.False(controls.Refresh(true));
        controls.Dispose(); controls.Dispose();
        Assert.Null(dialog.Composers["worldmap-layer-test"]);
        dialog.Composers["worldmap-layer-test"] = replacement;
        MapComposerOwnership.Detach(dialog.Composers, "worldmap-layer-test", panel);
        Assert.Same(replacement, dialog.Composers["worldmap-layer-test"]);
    }
    #endregion

    #region Private
    /// <summary>Uses the native constructor without Compose, so no texture allocation or graphics API is required.</summary>
    private static GuiComposer Composer(ICoreClientAPI api) => (GuiComposer)Activator.CreateInstance(typeof(GuiComposer),
        BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { api, ElementBounds.Empty, "test" }, null)!;
    /// <summary>Sets inspected native lifecycle state rather than launching a dialog in a test.</summary>
    private static void Set(Type owner, object instance, string name, object value) =>
        owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
    #endregion
}
