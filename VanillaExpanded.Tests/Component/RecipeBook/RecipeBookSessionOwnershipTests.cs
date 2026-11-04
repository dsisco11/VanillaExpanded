using System.Reflection;
using System.Text;
using Cairo;
using Moq;
using VanillaExpanded.Tests.Mocks;
using VanillaExpanded.ModSystems;
using VanillaExpanded.RecipeBook;
using VanillaExpanded.Tests.TestSupport.Gui;
using VanillaExpanded.Tests.TestSupport.RecipeBook;
using VanillaExpanded.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;
using System.Collections.Immutable;
using VanillaExpanded.Tests;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeBookLifecycleTestsTestSupport;
using static VanillaExpanded.Tests.TestSupport.RecipeBook.RecipeFilterSetTestsTestSupport;

namespace VanillaExpanded.Tests.Component.RecipeBook;

[Trait(TestCategories.TraitName, TestCategories.Component)]
[Collection("Recipe book native GUI")]
public sealed class RecipeBookSessionOwnershipTests
{
    [Fact]
    public void SessionFilterRegistrationsSurviveRecompositionAndAreReleasedWithTheDialog()
    {
        using var f = new MenuFixture(includeRecipes: true);
        f.Parent.TryOpen();
        f.Toggle();
        var menu = Assert.Single(f.Menus);
        var session = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(menu)!;
        var manager = session.Filters;
        using var registration = manager.Register("test:lifetime", new LifecycleFilterProvider());
        var previousComposer = menu.SingleComposer;
        f.ResizePanelSpace(previousComposer.Bounds.fixedWidth, previousComposer.Bounds.fixedHeight - 20);
        menu.TryOpen();
        Assert.NotSame(previousComposer, menu.SingleComposer);
        Assert.Same(manager, session.Filters);
        Assert.Single(manager.Filter(session.Rows));
        Assert.Throws<ArgumentException>(() => manager.Register("test:lifetime", new LifecycleFilterProvider()));

        f.Parent.TryClose();
        Assert.Empty(f.Menus);
        Assert.Throws<ObjectDisposedException>(() => manager.Filter(session.Rows));
        registration.Dispose();
        f.Parent.TryOpen();
        var reopened = Assert.Single(f.Menus);
        var newSession = (RecipeBookSession)typeof(GuiDialogRecipeBook).GetField("session",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
        Assert.NotSame(manager, newSession.Filters);
        using var newRegistration = newSession.Filters.Register("test:lifetime", new LifecycleFilterProvider());
        Assert.Single(newSession.Filters.Filter(newSession.Rows));
    }

    [Fact]
    public void SessionsOwnIndependentManagersAndReleaseThemOnTeardown()
    {
        var fixture = new AllocationFixture();
        var catalogue = fixture.Catalogue.Build();
        RecipeBookSession Session() => new(catalogue, fixture.Allocator,
            () => throw new InvalidOperationException("No inventory capture is needed."),
            (_, _) => throw new InvalidOperationException("No dispatch is needed."));
        using var first = Session();
        using var second = Session();
        var rows = Rows();
        using var registration = first.Filters.Register("feature", new Provider(_ => false));
        Assert.NotSame(first.Filters, second.Filters);
        Assert.Empty(first.Filters.Filter(rows));
        Assert.Equal(3, second.Filters.Filter(rows).Length);
        first.Dispose();
        first.Dispose();
        registration.Dispose();
        Assert.Throws<ObjectDisposedException>(() => first.Filters.Filter(rows));
        Assert.Equal(3, second.Filters.Filter(rows).Length);
    }
}
