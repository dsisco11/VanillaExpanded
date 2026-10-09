using System;
using System.Collections.Generic;

using Vintagestory.API.Client;

namespace VanillaExpanded.ItemSlotIndicators.Rendering;

/// <summary>Owns reusable effect meshes and a separate standard GUI rectangle layout.</summary>
internal sealed class ItemSlotIndicatorMeshCache(IItemSlotIndicatorResourceBackend backend) : IDisposable
{
    private readonly Dictionary<ItemSlotIndicatorMeshKey, (MeshRef? Mesh, string? Error)> entries = [];
    private MeshRef? rectangle;
    private string? rectangleError;
    private bool rectangleAttempted;

    #region Public API
    /// <summary>Gets the standard GUI quad, whose UV layout is distinct from effect geometry.</summary>
    internal MeshRef? Rectangle => rectangle is { Disposed: false } ? rectangle : null;

    /// <summary>Uploads effect geometry only at a preparation boundary, memoizing unavailable geometry too.</summary>
    internal string? Prepare(ItemSlotIndicatorMeshKey key)
    {
        if (entries.TryGetValue(key, out var existing)) return existing.Error;
        var result = Upload(ItemSlotIndicatorMeshGeometry.Build(key));
        entries.Add(key, result);
        return result.Error;
    }

    /// <summary>Prepares the independent rectangle quad without coupling it to any effect's shader availability.</summary>
    internal string? PrepareRectangle()
    {
        if (rectangleAttempted) return rectangleError;
        rectangleAttempted = true;
        (rectangle, rectangleError) = Upload(QuadMeshUtil.GetQuad());
        return rectangleError;
    }

    /// <summary>Returns a prepared live mesh without generating geometry or uploading.</summary>
    internal MeshRef? Get(ItemSlotIndicatorMeshKey key) =>
        entries.TryGetValue(key, out var entry) && entry.Mesh is { Disposed: false } ? entry.Mesh : null;

    /// <summary>Allows failed uploads to retry at an explicit reload boundary while retaining surviving meshes.</summary>
    internal void ForgetFailures()
    {
        // Shader reload cannot invalidate fixed-ABI geometry, so successful allocations remain shared.
        foreach (var key in new List<ItemSlotIndicatorMeshKey>(entries.Keys))
            if (entries[key].Mesh is null) entries.Remove(key);
        if (rectangle is null) rectangleAttempted = false;
    }

    /// <summary>Releases owned meshes once and clears partial initialization state.</summary>
    public void Dispose()
    {
        var previous = new List<(MeshRef? Mesh, string? Error)>(entries.Values);
        entries.Clear();
        MeshRef? previousRectangle = rectangle;
        rectangle = null;
        rectangleError = null;
        rectangleAttempted = false;
        foreach (var entry in previous)
            if (entry.Mesh is { Disposed: false }) entry.Mesh.Dispose();
        if (previousRectangle is { Disposed: false }) previousRectangle.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Releases a returned partial upload and converts graphics failure into cached unavailability.</summary>
    private (MeshRef? Mesh, string? Error) Upload(MeshData data)
    {
        MeshRef? mesh = null;
        try
        {
            mesh = backend.UploadMesh(data);
            if (mesh is null || mesh.Disposed || !mesh.Initialized)
                throw new InvalidOperationException("Engine mesh upload did not produce an initialized mesh.");
            return (mesh, null);
        }
        catch (Exception exception)
        {
            if (mesh is { Disposed: false }) mesh.Dispose();
            return (null, exception.Message);
        }
    }
    #endregion
}
