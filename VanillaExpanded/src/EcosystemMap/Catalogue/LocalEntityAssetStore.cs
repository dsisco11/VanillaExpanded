using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaExpanded.EcosystemMap.Catalogue;

/// <summary>A private in-memory asset manager implementing the vanilla patcher's asset boundary without exposing live assets.</summary>
internal sealed class LocalEntityAssetStore : IAssetManager
{
    private readonly Dictionary<AssetLocation, IAsset> assets = new();
    public Dictionary<AssetLocation, IAsset> AllAssets => new(assets);
    public List<IAssetOrigin> Origins => new();

    #region Public API
    #region Snapshot and lookup
    /// <summary>Copies both the location and bytes so vanilla patch writes cannot reach the original content.</summary>
    internal void Copy(IAsset source)
    {
        var copy = new Asset(source.Location.Clone()) { Data = (byte[])source.Data.Clone() };
        assets[copy.Location] = copy;
    }

    /// <summary>Checks whether a private definition exists.</summary>
    public bool Exists(AssetLocation location) => assets.ContainsKey(location);
    /// <summary>Returns an already-loaded private asset, never falling back to the live manager or disk.</summary>
    public IAsset TryGet(AssetLocation location, bool loadAsset = true) => assets.TryGetValue(location, out var asset) ? asset : null!;
    /// <summary>Returns a private asset or rejects missing content.</summary>
    public IAsset Get(AssetLocation location) => TryGet(location) ?? throw new KeyNotFoundException(location.ToString());
    /// <summary>Deserializes a private asset using the normal asset implementation.</summary>
    public T Get<T>(AssetLocation location) => Get(location).ToObject<T>();
    /// <summary>Returns a new list of matching private assets while preserving patch enumeration order.</summary>
    public List<IAsset> GetMany(string pathBegins, string? domain = null, bool loadAsset = true) =>
        assets.Where(pair => pair.Key.Path.StartsWith(pathBegins, StringComparison.Ordinal)
            && (domain == null || pair.Key.Domain == domain)).Select(pair => pair.Value).ToList();
    /// <summary>Queries the private view by category and relative path.</summary>
    public List<IAsset> GetManyInCategory(string categoryCode, string pathBegins, string? domain = null, bool loadAsset = true) =>
        GetMany(categoryCode + "/" + pathBegins, domain, loadAsset);
    /// <summary>Returns independent location objects for the private view.</summary>
    public List<AssetLocation> GetLocations(string pathBegins, string? domain = null) =>
        GetMany(pathBegins, domain).Select(asset => asset.Location.Clone()).ToList();
    /// <summary>Deserializes matching private assets, reporting malformed content through the caller's logger.</summary>
    public Dictionary<AssetLocation, T> GetMany<T>(ILogger logger, string pathBegins, string? domain = null)
    {
        var result = new Dictionary<AssetLocation, T>();
        foreach (IAsset asset in GetMany(pathBegins, domain))
        {
            try { result[asset.Location.Clone()] = asset.ToObject<T>(); }
            catch (Exception error) { logger.Error(error); }
        }
        return result;
    }
    #endregion

    #region Unsupported mutation
    /// <summary>Rejects registration: only snapshot capture may populate this isolated view.</summary>
    public void Add(AssetLocation path, IAsset asset) => throw new NotSupportedException();
    /// <summary>Rejects reloads that could reach live content.</summary>
    public int Reload(AssetLocation location) => throw new NotSupportedException();
    /// <summary>Rejects category reloads that could reach live content.</summary>
    public int Reload(AssetCategory category) => throw new NotSupportedException();
    /// <summary>Rejects external origins in an already captured patch view.</summary>
    public void AddPathOrigin(string domain, string fullPath) => throw new NotSupportedException();
    /// <summary>Rejects external origins in an already captured patch view.</summary>
    public void AddModOrigin(string domain, string fullPath) => throw new NotSupportedException();
    /// <summary>Rejects external origins in an already captured patch view.</summary>
    public void AddModOrigin(string domain, string fullPath, string pathForReservedCharsCheck) => throw new NotSupportedException();
    /// <summary>Rejects origin mutation in an already captured patch view.</summary>
    public void RemoveModOrigin(string domain, string fullpath) => throw new NotSupportedException();
    #endregion
    #endregion
}
