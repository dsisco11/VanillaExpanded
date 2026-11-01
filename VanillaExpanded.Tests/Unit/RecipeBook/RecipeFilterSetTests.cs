using System.Collections.Immutable;
using VanillaExpanded.RecipeBook;

namespace VanillaExpanded.Tests.Unit.RecipeBook;

[Trait("Category", "Unit")]
public sealed class RecipeFilterSetTests
{
    [Fact]
    public void ExcludingAnOwnedRegistrationPreservesEveryOtherFilterAndDoesNotMutateTheSet()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        using var first = manager.Register("first", new Provider(row => row.Output.Id == 0));
        using var second = manager.Register("second", new Provider(row => row.Output.Id == 1));
        Assert.Empty(manager.Filter(rows));
        Assert.Same(rows[1], Assert.Single(first.FilterOthers(rows)));
        Assert.Same(rows[0], Assert.Single(second.FilterOthers(rows)));
        Assert.Empty(manager.Filter(rows));
        Assert.Throws<ArgumentException>(() => manager.Register("first", new Provider(_ => true)));
        first.Replace(new Provider(row => row.Output.Id == 1));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
        Assert.Same(rows[1], Assert.Single(second.FilterOthers(rows)));
        first.Dispose();
        using var replacement = manager.Register("first", new Provider(_ => false));
        Assert.Throws<InvalidOperationException>(() => first.FilterOthers(rows));
        Assert.Same(rows[1], Assert.Single(replacement.FilterOthers(rows)));
    }

    [Fact]
    public void ExcludedProviderIsNotEvaluatedAndOtherProviderFailuresStillPropagate()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        var failure = new InvalidOperationException("Provider failure.");
        using var first = manager.Register("first", new Provider(_ => throw failure));
        Assert.Equal(rows.ToArray(), first.FilterOthers(rows).ToArray());
        using var second = manager.Register("second", new Provider(_ => throw failure));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => first.FilterOthers(rows)));
        second.Dispose();
        Assert.Equal(rows.ToArray(), first.FilterOthers(rows).ToArray());
    }

    [Fact]
    public void NoFiltersPreserveSelectionsAndInputOrder()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows().Reverse().ToArray();
        var filtered = manager.Filter(rows);
        Assert.Equal(rows.Length, filtered.Length);
        for (int i = 0; i < rows.Length; i++) Assert.Same(rows[i], filtered[i]);
        Assert.Empty(manager.Filter([]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProvidersIntersectIndependentlyOfRegistrationOrder(bool reverse)
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        var providers = new[] {
            new Provider(row => row.Output.Id <= 1),
            new Provider(row => row.Output.Id >= 1)
        };
        if (reverse) Array.Reverse(providers);
        for (int i = 0; i < providers.Length; i++) manager.Register("filter-" + i, providers[i]);

        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
    }

    [Fact]
    public void ProvidersReceiveTheExactEvaluatedSelectionAndMayShortCircuit()
    {
        using var manager = new RecipeFilterSet();
        var row = Rows()[0] with { Evaluation = CatalogueFixture.Full(), IsExplicit = true };
        using var reject = manager.Register("reject", new Provider(selection =>
        {
            Assert.Same(row, selection);
            Assert.True(selection.Evaluation.CanCraft);
            Assert.True(selection.IsExplicit);
            return false;
        }));
        using var later = manager.Register("later", new Provider(_ =>
            throw new InvalidOperationException("The earlier rejection can short-circuit.")));

        Assert.Empty(manager.Filter([row]));
    }

    [Fact]
    public void HandlesReplaceAndRemoveOnlyTheirOwnRegistrationsIncludingReusedIdentities()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        using var first = manager.Register("first", new Provider(row => row.Output.Id == 0));
        using var second = manager.Register("second", new Provider(row => row.Output.Id < 2));
        Assert.Same(rows[0], Assert.Single(manager.Filter(rows)));
        first.Replace(new Provider(row => row.Output.Id == 1));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
        first.Dispose();
        first.Dispose();
        Assert.Equal(new[] { 0, 1 }, manager.Filter(rows).Select(row => row.Output.Id));

        using var replacement = manager.Register("first", new Provider(row => row.Output.Id == 2));
        first.Dispose();
        Assert.Throws<InvalidOperationException>(() => first.Replace(new Provider(_ => true)));
        Assert.Empty(manager.Filter(rows));
        second.Dispose();
        Assert.Same(rows[2], Assert.Single(manager.Filter(rows)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void InvalidIdentityDoesNotChangeTheCollection(string? id)
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        manager.Register("valid", new Provider(row => row.Output.Id == 1));
        Assert.ThrowsAny<ArgumentException>(() => manager.Register(id!, new Provider(_ => false)));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
    }

    [Fact]
    public void DuplicateIdentityAndNullProviderFailBeforeMutation()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        using var registration = manager.Register("feature", new Provider(row => row.Output.Id == 1));
        Assert.Throws<ArgumentException>(() => manager.Register("feature", new Provider(_ => false)));
        Assert.Throws<ArgumentNullException>(() => manager.Register("other", null!));
        Assert.Throws<ArgumentNullException>(() => registration.Replace(null!));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
        using var other = manager.Register("other", new Provider(_ => true));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
    }

    [Fact]
    public void ProviderFailurePropagatesWithoutReturningPartialResultsOrDroppingTheProvider()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        var failure = new InvalidOperationException("Provider failure.");
        using var registration = manager.Register("feature", new Provider(row =>
            row.Output.Id == 0 ? true : throw failure));

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Filter(rows)));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Filter(rows)));
        registration.Replace(new Provider(row => row.Output.Id == 2));
        Assert.Same(rows[2], Assert.Single(manager.Filter(rows)));
    }

    [Theory]
    [InlineData("register")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("dispose")]
    [InlineData("reenter")]
    [InlineData("filter-others")]
    public void ProviderCannotChangeTheRegistryDuringAProjection(string operation)
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        using var criterion = manager.Register("criterion", new Provider(row => row.Output.Id == 0));
        using var mutator = manager.Register("mutator", new Provider(_ =>
        {
            switch (operation)
            {
                case "register": manager.Register("unexpected", new Provider(_ => false)); break;
                case "remove": criterion.Dispose(); break;
                case "replace": criterion.Replace(new Provider(_ => false)); break;
                case "dispose": manager.Dispose(); break;
                case "reenter": manager.Filter(rows); break;
                case "filter-others": criterion.FilterOthers(rows); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
            return true;
        }));
        Assert.Throws<InvalidOperationException>(() => manager.Filter(rows));
        mutator.Dispose();
        Assert.Same(rows[0], Assert.Single(manager.Filter(rows)));
        using var afterFailure = manager.Register("unexpected", new Provider(_ => true));
        criterion.Replace(new Provider(row => row.Output.Id == 1));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
    }

    [Fact]
    public void CompleteSourceEnumerationIsProtectedAgainstRegistrationChanges()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        IEnumerable<CraftingRecipeSelection> Source()
        {
            yield return rows[0];
            manager.Register("during-enumeration", new Provider(_ => false));
            yield return rows[1];
        }

        Assert.Throws<InvalidOperationException>(() => manager.Filter(Source()));
        using var registration = manager.Register("during-enumeration", new Provider(_ => true));
        Assert.Equal(3, manager.Filter(rows).Length);
    }

    [Fact]
    public void InvalidInputsAndSourceFailuresReleaseTheProjectionGuard()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        var failure = new InvalidOperationException("Enumeration failure.");
        IEnumerable<CraftingRecipeSelection> Source()
        {
            yield return rows[0];
            throw failure;
        }

        Assert.Throws<ArgumentNullException>(() => manager.Filter(null!));
        Assert.Throws<ArgumentNullException>(() => manager.Filter([null!]));
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => manager.Filter(Source())));
        using var registration = manager.Register("after-failure", new Provider(_ => true));
        Assert.Equal(3, manager.Filter(rows).Length);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("remove")]
    [InlineData("replace")]
    [InlineData("dispose")]
    [InlineData("filter")]
    [InlineData("filter-others")]
    public void RegistryAndProjectionRejectNonOwningThreadsWithoutMutation(string operation)
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        using var registration = manager.Register("feature", new Provider(row => row.Output.Id == 0));
        Exception? error = null;
        var worker = new Thread(() => error = Record.Exception(() =>
        {
            switch (operation)
            {
                case "register": manager.Register("worker", new Provider(_ => false)); break;
                case "remove": registration.Dispose(); break;
                case "replace": registration.Replace(new Provider(_ => false)); break;
                case "dispose": manager.Dispose(); break;
                case "filter": manager.Filter(rows); break;
                case "filter-others": registration.FilterOthers(rows); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(error);
        Assert.Same(rows[0], Assert.Single(manager.Filter(rows)));
        registration.Replace(new Provider(row => row.Output.Id == 1));
        Assert.Same(rows[1], Assert.Single(manager.Filter(rows)));
    }

    [Fact]
    public void DisposalIsIdempotentAndInvalidatesEveryRegistration()
    {
        using var manager = new RecipeFilterSet();
        var rows = Rows();
        using var registration = manager.Register("feature", new Provider(_ =>
            throw new InvalidOperationException("A disposed provider must not be evaluated.")));
        manager.Dispose();
        manager.Dispose();
        registration.Dispose();
        registration.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.Filter(rows));
        Assert.Throws<ObjectDisposedException>(() => manager.Register("feature", new Provider(_ => true)));
        Assert.Throws<ObjectDisposedException>(() => registration.Replace(new Provider(_ => true)));
        Assert.Throws<ObjectDisposedException>(() => registration.FilterOthers(rows));
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

    private static ImmutableArray<CraftingRecipeSelection> Rows()
    {
        var fixture = new CatalogueFixture();
        var view = new CraftingCatalogueView(fixture.Build(
            fixture.Recipe("a"), fixture.Recipe("b"), fixture.Recipe("c")));
        using var filters = new RecipeFilterSet();
        return view.GetRows(filters);
    }

    private sealed class Provider(Func<CraftingRecipeSelection, bool> matches) : IRecipeFilter
    {
        public bool Matches(CraftingRecipeSelection selection) => matches(selection);
    }
}
