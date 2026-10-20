using Microsoft.Data.Sqlite;
using Moq;
using VanillaExpanded.EcosystemMap.Storage;
using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Owns disposable test-only ecosystem directories and initialized SQLite dependencies; never points at a real world cache.</summary>
internal sealed class StorageFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ve-ecosystem-storage-" + Guid.NewGuid().ToString("N"));
    internal EcosystemStorageIdentity Identity { get; } = new("test-world", 10000, 256, 12000, 110, 512, 32);

    #region Public API
    /// <summary>Initializes the installed provider and creates only this fixture's unique directory.</summary>
    internal StorageFixture() { SQLitePCL.Batteries_V2.Init(); Directory.CreateDirectory(Root); }
    /// <summary>Creates a bounded owner for this test world; callers await/dispose it before fixture cleanup.</summary>
    internal EnvironmentalStorage Open(EcosystemStorageIdentity? identity = null, int capacity = 2, ILogger? logger = null) =>
        new(Root, identity ?? Identity, logger ?? Mock.Of<ILogger>(), capacity);
    /// <summary>Creates a small layout containing negative and high-bit integer values to detect lossy encoding.</summary>
    internal static EnvironmentalMap Map(int offset = 0) => new(3, 1, 0,
        new[] { int.MinValue + offset, -123, 0, 1, 127, 255, 256, 0x12345678, int.MaxValue - offset });
    /// <summary>Creates a complete tile with unsigned-height edge cases.</summary>
    internal static TileEnvironment Tile(EcosystemTileKey key) => new(key, 32,
        Enumerable.Range(0, 1024).Select(index => (ushort)(index == 0 ? ushort.MaxValue : index)));
    /// <summary>Executes deliberate cache corruption/inspection exclusively against this fixture's feature-owned path.</summary>
    internal void Execute(string sql, EcosystemStorageIdentity? identity = null)
    {
        using var connection = new SqliteConnection("Data Source=" + (identity ?? Identity).GetDatabasePath(Root) + ";Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    /// <summary>Removes only the unique directory created by this fixture, after every connection owner has closed.</summary>
    public void Dispose()
    {
        string root = Path.GetFullPath(Root);
        if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(root).StartsWith("ve-ecosystem-storage-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected fixture cleanup path.");
        Directory.Delete(root, true);
    }
    #endregion
}
