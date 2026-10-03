using System;
using System.Collections.Generic;

using VanillaExpanded.ItemSlotIndicators.Effects;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Coordinates effect registration, explicit preparation, shader reload, and client resource lifetime.</summary>
/// <remarks>Construction, registration, preparation, lookup, and disposal belong to the client graphics thread.</remarks>
internal sealed class ItemSlotIndicatorResources : IDisposable
{
    private readonly IClientEventAPI events;
    private readonly IItemSlotIndicatorResourceBackend backend;
    private readonly ItemSlotIndicatorProgramCache programs;
    private readonly ItemSlotIndicatorMeshCache meshes;
    private readonly int graphicsThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<string, ItemSlotIndicatorEffectDefinition> definitions = [];
    private readonly Dictionary<string, (IShaderProgram Program, MeshRef Mesh)> available = [];
    private readonly List<ItemSlotIndicatorEffectDefinition> pending = [];
    private bool initialized;
    private bool preparationQueued;
    private bool disposed;
    private bool rectangleFailureReported;

    #region Public API
    #region Construction and Lifetime
    /// <summary>Creates client caches without allocating GPU resources or subscribing events.</summary>
    internal ItemSlotIndicatorResources(IClientEventAPI events, IItemSlotIndicatorResourceBackend backend)
    {
        this.events = events;
        this.backend = backend;
        programs = new ItemSlotIndicatorProgramCache(backend);
        meshes = new ItemSlotIndicatorMeshCache(backend);
    }

    /// <summary>Subscribes reload once and prepares the initial definitions and rectangle at the graphics boundary.</summary>
    internal void Initialize()
    {
        EnsureGraphicsThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (initialized) return;
        events.ReloadShader += Reload;
        initialized = true;
        PreparePending();
    }

    /// <summary>Unsubscribes and releases owned handles; queued preparation becomes harmless after shutdown.</summary>
    public void Dispose()
    {
        EnsureGraphicsThread();
        if (disposed) return;
        disposed = true;
        if (initialized) events.ReloadShader -= Reload;
        initialized = false;
        preparationQueued = false;
        pending.Clear();
        available.Clear();
        definitions.Clear();
        programs.Dispose();
        meshes.Dispose();
    }
    #endregion

    #region Registration and Lookup
    /// <summary>Validates identity before mutation and queues new definitions; registration never touches the GPU.</summary>
    internal void Register(ItemSlotIndicatorEffectDefinition definition)
    {
        EnsureGraphicsThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(definition);
        foreach (var existing in definitions.Values) existing.ValidateCompatibility(definition);
        if (!definitions.TryAdd(definition.Id, definition)) return;
        pending.Add(definition);
        if (initialized && !preparationQueued)
        {
            preparationQueued = true;
            events.EnqueueMainThreadTask(PreparePending, "itemslotindicator-resources");
        }
    }

    /// <summary>Gets the independent prepared GUI rectangle mesh.</summary>
    internal MeshRef? Rectangle => disposed ? null : meshes.Rectangle;

    /// <summary>Resolves only previously prepared live resources; queued and failed effects remain unavailable.</summary>
    internal bool TryGet(ItemSlotIndicatorEffectDefinition definition, out IShaderProgram? program, out MeshRef? mesh)
    {
        program = null;
        mesh = null;
        if (disposed || !definitions.TryGetValue(definition.Id, out var registered) || registered != definition
            || !available.TryGetValue(definition.Id, out var resource) || resource.Program.Disposed || resource.Mesh.Disposed)
            return false;
        program = resource.Program;
        mesh = resource.Mesh;
        return true;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Loads a stable registration snapshot, leaving registrations made during preparation for a later boundary.</summary>
    private void PreparePending()
    {
        EnsureGraphicsThread();
        preparationQueued = false;
        if (disposed) return;
        var snapshot = pending.ToArray();
        pending.Clear();
        string? rectangleFailure = meshes.PrepareRectangle();
        if (rectangleFailure is not null && !rectangleFailureReported)
        {
            rectangleFailureReported = true;
            backend.ReportFailure("rectangle", rectangleFailure);
        }
        Prepare(snapshot);
    }

    /// <summary>Invalidates all program references before recompilation and preserves fixed-layout meshes.</summary>
    private bool Reload()
    {
        EnsureGraphicsThread();
        if (disposed) return true;
        available.Clear();
        programs.Dispose();
        meshes.ForgetFailures();
        rectangleFailureReported = false;
        pending.Clear();
        pending.AddRange(definitions.Values);
        // The engine has already disposed registry programs; no stale handle can serve as a fallback.
        PreparePending();
        return available.Count == definitions.Count && meshes.Rectangle is not null;
    }

    /// <summary>Prepares each definition independently, sharing program/geometry attempts and reporting one failure per effect.</summary>
    private void Prepare(ItemSlotIndicatorEffectDefinition[] snapshot)
    {
        foreach (var definition in snapshot)
        {
            if (disposed) return;
            string? error = programs.Prepare(definition);
            if (error is null) error = meshes.Prepare(ItemSlotIndicatorMeshKey.From(definition));
            if (error is not null)
            {
                backend.ReportFailure(definition.Id, error);
                continue;
            }
            var program = programs.Get(definition);
            var mesh = meshes.Get(ItemSlotIndicatorMeshKey.From(definition));
            if (program is not null && mesh is not null) available[definition.Id] = (program, mesh);
        }
    }

    /// <summary>Rejects resource lifecycle calls outside the thread that owns the client graphics context.</summary>
    private void EnsureGraphicsThread()
    {
        if (Environment.CurrentManagedThreadId != graphicsThread)
            throw new InvalidOperationException("Indicator resources must be managed on the client graphics thread.");
    }
    #endregion
}
