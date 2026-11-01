using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace VanillaExpanded.RecipeBook;

internal sealed class RecipeFilterSet : IDisposable
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, RegisteredFilter> filters = new(StringComparer.Ordinal);
    private bool projecting;
    private bool disposed;

    internal Registration Register(string id, IRecipeFilter filter)
    {
        EnsureMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(filter);
        if (filters.ContainsKey(id))
            throw new ArgumentException("A crafting filter with this identity is already registered.", nameof(id));
        var registration = new Registration(this, id);
        filters.Add(id, new(registration, filter));
        return registration;
    }

    /// <summary>Eagerly filters one complete pass, preventing registration changes until enumeration finishes.</summary>
    internal ImmutableArray<CraftingRecipeSelection> Filter(IEnumerable<CraftingRecipeSelection> selections) =>
        Filter(selections, null);

    private ImmutableArray<CraftingRecipeSelection> Filter(IEnumerable<CraftingRecipeSelection> selections,
        Registration? excluded)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(selections);
        if (excluded != null && (!filters.TryGetValue(excluded.Id, out var current)
            || current.Registration != excluded))
            throw new InvalidOperationException("The crafting filter registration is no longer active.");
        projecting = true;
        try
        {
            var result = ImmutableArray.CreateBuilder<CraftingRecipeSelection>();
            foreach (var selection in selections)
            {
                ArgumentNullException.ThrowIfNull(selection);
                bool matches = true;
                foreach (var registered in filters.Values)
                {
                    if (registered.Registration == excluded) continue;
                    if (registered.Filter.Matches(selection)) continue;
                    matches = false;
                    break;
                }
                if (matches) result.Add(selection);
            }
            return result.ToImmutable();
        }
        finally { projecting = false; }
    }

    private void Replace(Registration registration, IRecipeFilter filter)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(filter);
        if (!filters.TryGetValue(registration.Id, out var registered) || registered.Registration != registration)
            throw new InvalidOperationException("The crafting filter registration is no longer active.");
        filters[registration.Id] = new(registration, filter);
    }

    private void Remove(Registration registration)
    {
        EnsureThread();
        if (!filters.TryGetValue(registration.Id, out var registered) || registered.Registration != registration) return;
        EnsureMutable();
        filters.Remove(registration.Id);
    }

    private void EnsureThread()
    {
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Crafting filters must be accessed on their owning client thread.");
    }

    private void EnsureMutable()
    {
        EnsureThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (projecting)
            throw new InvalidOperationException("Crafting filter registrations cannot change during projection.");
    }

    public void Dispose()
    {
        EnsureThread();
        if (disposed) return;
        EnsureMutable();
        filters.Clear();
        disposed = true;
    }

    private sealed record RegisteredFilter(Registration Registration, IRecipeFilter Filter);

    internal sealed class Registration : IDisposable
    {
        private readonly RecipeFilterSet owner;
        internal string Id { get; }

        internal Registration(RecipeFilterSet owner, string id)
        {
            this.owner = owner;
            Id = id;
        }

        internal void Replace(IRecipeFilter filter) => owner.Replace(this, filter);
        internal ImmutableArray<CraftingRecipeSelection> FilterOthers(IEnumerable<CraftingRecipeSelection> selections) =>
            owner.Filter(selections, this);
        public void Dispose() => owner.Remove(this);
    }
}
