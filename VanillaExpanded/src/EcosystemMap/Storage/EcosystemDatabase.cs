using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Owns only the dedicated ecosystem SQLite connection, schema and synchronous commands; used exclusively by the storage worker.</summary>
internal sealed class EcosystemDatabase : SQLiteDBConnection
{
    internal const int SchemaVersion = 1;
    internal const int RegionalRecordVersion = 2;
    internal const int TileRecordVersion = 3;
    private readonly EcosystemStorageIdentity identity;
    private PackedTextureStore uploads = null!;
    internal string FilePath { get; }
    public override string DBTypeCode => "ecosystem cache";

    #region Public API
    #region Connection and schema
    /// <summary>Computes an independent feature path; it never receives or opens the vanilla map cache connection.</summary>
    internal EcosystemDatabase(string dataPath, EcosystemStorageIdentity identity, ILogger logger) : base(logger)
    {
        this.identity = identity;
        FilePath = identity.GetDatabasePath(dataPath);
    }

    /// <summary>Opens the feature file and verifies identity before any environmental records can be used.</summary>
    internal void Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string error = "";
        try
        {
            if (!OpenOrCreate(FilePath, ref error, true, true, false))
                throw new IOException("Cannot open the ecosystem database: " + error);
            VerifyIdentity();
            uploads = new PackedTextureStore(sqliteConn);
        }
        catch { Close(); throw; }
    }

    /// <summary>Creates the independent schema only for known versions; unknown schemas are not migrated or interpreted.</summary>
    protected override void CreateTablesIfNotExists(SqliteConnection connection)
    {
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        long current = (long)version.ExecuteScalar()!;
        if (current != 0 && current != SchemaVersion) throw new NotSupportedException("Unsupported ecosystem database schema.");
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ecosystem_identity (
                id INTEGER PRIMARY KEY CHECK(id=1), world_id TEXT NOT NULL, world_width INTEGER NOT NULL, world_height INTEGER NOT NULL, world_depth INTEGER NOT NULL,
                sea_level INTEGER NOT NULL, region_size INTEGER NOT NULL, chunk_size INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS ecosystem_region (
                dimension INTEGER NOT NULL, x INTEGER NOT NULL, z INTEGER NOT NULL, version INTEGER NOT NULL,
                climate BLOB, forest BLOB, shrubs BLOB, completeness INTEGER NOT NULL,
                PRIMARY KEY(dimension,x,z));
            CREATE TABLE IF NOT EXISTS ecosystem_map_piece (
                dimension INTEGER NOT NULL, x INTEGER NOT NULL, z INTEGER NOT NULL,
                region_x INTEGER NOT NULL, region_z INTEGER NOT NULL, version INTEGER NOT NULL,
                elevation BLOB NOT NULL, completeness INTEGER NOT NULL, PRIMARY KEY(dimension,x,z));
            CREATE TABLE IF NOT EXISTS ecosystem_region_upload (
                dimension INTEGER NOT NULL, x INTEGER NOT NULL, z INTEGER NOT NULL, version INTEGER NOT NULL,
                payload BLOB NOT NULL, PRIMARY KEY(dimension,x,z));
            CREATE TABLE IF NOT EXISTS ecosystem_tile_upload (
                dimension INTEGER NOT NULL, x INTEGER NOT NULL, z INTEGER NOT NULL, version INTEGER NOT NULL,
                payload BLOB NOT NULL, PRIMARY KEY(dimension,x,z));
            PRAGMA user_version=1;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
    #endregion

    #region Record access
    #region Original records
    /// <summary>Atomically merges independently arriving regional components; a null component leaves a previously captured input intact.</summary>
    internal void WriteRegion(RegionalEnvironment record)
    {
        var previous = ReadRegion(record.Key);
        var merged = new RegionalEnvironment(record.Key, record.Climate ?? previous?.Climate,
            record.Forest ?? previous?.Forest, record.Shrubs ?? previous?.Shrubs);
        var packed = new PackedRegionalData(merged, message => logger.Warning(message));
        using var transaction = sqliteConn.BeginTransaction();
        using var command = sqliteConn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ecosystem_region(dimension,x,z,version,climate,forest,shrubs,completeness)
            VALUES($d,$x,$z,$v,$c,$f,$s,$complete)
            ON CONFLICT(dimension,x,z) DO UPDATE SET version=excluded.version, climate=excluded.climate,
                forest=excluded.forest, shrubs=excluded.shrubs, completeness=excluded.completeness;
            """;
        AddKey(command, record.Key.X, record.Key.Z, record.Key.Dimension);
        command.Parameters.AddWithValue("$v", RegionalRecordVersion);
        command.Parameters.AddWithValue("$c", Encode(merged.Climate));
        command.Parameters.AddWithValue("$f", Encode(merged.Forest));
        command.Parameters.AddWithValue("$s", Encode(merged.Shrubs));
        command.Parameters.AddWithValue("$complete", (int)merged.Completeness);
        command.ExecuteNonQuery();
        uploads.WriteRegion(record.Key, packed, transaction);
        transaction.Commit();
    }

    /// <summary>Reads only own local records; unsupported row/component versions remain unavailable.</summary>
    internal RegionalEnvironment? ReadRegion(EcosystemRegionKey key)
    {
        using var command = sqliteConn.CreateCommand();
        command.CommandText = "SELECT version,climate,forest,shrubs FROM ecosystem_region WHERE dimension=$d AND x=$x AND z=$z";
        AddKey(command, key.X, key.Z, key.Dimension);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != RegionalRecordVersion) return null;
        return new RegionalEnvironment(key, ReadMap(reader, 1), ReadMap(reader, 2), ReadMap(reader, 3));
    }

    /// <summary>Stores one tile transaction with coordinate-only association to a region in the same feature database.</summary>
    internal void WriteTile(TileEnvironment tile)
    {
        if (tile.Size != identity.ChunkSize) throw new ArgumentException("Tile layout differs from this world.", nameof(tile));
        // Independent packet and surface observations must not erase each other when they arrive in either order.
        var previous = ReadTile(tile.Key);
        tile = new TileEnvironment(tile.Key, tile.Size, tile.HasElevation ? tile.Elevation : previous?.Elevation, tile.Ocean ?? previous?.Ocean);
        var region = identity.RegionFor(tile.Key);
        var packed = PackedTerrainData.Pack(tile, identity.ChunkSize);
        using var transaction = sqliteConn.BeginTransaction();
        using var command = sqliteConn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ecosystem_map_piece(dimension,x,z,region_x,region_z,version,elevation,completeness)
            VALUES($d,$x,$z,$rx,$rz,$v,$e,$complete)
            ON CONFLICT(dimension,x,z) DO UPDATE SET region_x=excluded.region_x,region_z=excluded.region_z,
                version=excluded.version,elevation=excluded.elevation,completeness=excluded.completeness;
            """;
        AddKey(command, tile.Key.X, tile.Key.Z, tile.Key.Dimension);
        command.Parameters.AddWithValue("$rx", region.X);
        command.Parameters.AddWithValue("$rz", region.Z);
        command.Parameters.AddWithValue("$v", TileRecordVersion);
        command.Parameters.AddWithValue("$e", EnvironmentalCompression.Encode(EnvironmentalRecordCodec.EncodeTile(tile)));
        command.Parameters.AddWithValue("$complete", (int)tile.Completeness);
        command.ExecuteNonQuery();
        uploads.WriteTile(tile.Key, tile.Size, packed, transaction);
        transaction.Commit();
    }

    /// <summary>Returns unavailable for missing, incompatible or incorrectly associated tile data.</summary>
    internal TileEnvironment? ReadTile(EcosystemTileKey key)
    {
        using var command = sqliteConn.CreateCommand();
        command.CommandText = "SELECT version,region_x,region_z,elevation FROM ecosystem_map_piece WHERE dimension=$d AND x=$x AND z=$z";
        AddKey(command, key.X, key.Z, key.Dimension);
        using var reader = command.ExecuteReader();
        var region = identity.RegionFor(key);
        if (!reader.Read() || reader.GetInt32(0) != TileRecordVersion
            || reader.GetInt32(1) != region.X || reader.GetInt32(2) != region.Z) return null;
        // Geometry bounds tile decompression more tightly than the general regional safety ceiling.
        long cells = (long)identity.ChunkSize * identity.ChunkSize;
        int maximum = (int)Math.Min(EnvironmentalCompression.MaximumDecodedLength, 16L + cells * 2 + (cells + 3) / 4);
        return EnvironmentalRecordCodec.DecodeTile(key, EnvironmentalCompression.Decode((byte[])reader.GetValue(3), maximum), identity.ChunkSize);
    }

    #endregion
    #region Upload cache
    /// <summary>Loads saved RGBA bytes directly; only missing derived buffers require decoding current local originals.</summary>
    internal PackedRegionalData? ReadRegionForUpload(EcosystemRegionKey key)
    {
        var packed = uploads.ReadRegion(key);
        if (packed != null) return packed;
        var original = ReadRegion(key);
        if (original == null) return null;
        packed = new PackedRegionalData(original, message => logger.Warning(message));
        uploads.WriteRegion(key, packed);
        return packed;
    }

    /// <summary>Loads saved RG16UI words directly, rebuilding only missing/invalid derived data on this worker.</summary>
    internal PackedTerrainData? ReadTileForUpload(EcosystemTileKey key)
    {
        var packed = uploads.ReadTile(key, identity.ChunkSize, identity.RegionFor(key));
        if (packed != null) return packed;
        var original = ReadTile(key);
        if (original == null) return null;
        packed = new PackedTerrainData(identity.ChunkSize, PackedTerrainData.Pack(original, identity.ChunkSize));
        uploads.WriteTile(key, identity.ChunkSize, packed.Values);
        return packed;
    }
    #endregion
    #region Cache clearing
    /// <summary>Intentionally clears original and derived ecosystem rows; identity/schema remain scoped to the current world.</summary>
    internal void Purge()
    {
        using var transaction = sqliteConn.BeginTransaction();
        using var command = sqliteConn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM ecosystem_tile_upload; DELETE FROM ecosystem_region_upload; DELETE FROM ecosystem_map_piece; DELETE FROM ecosystem_region;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }
    #endregion
    #endregion
    #endregion

    #region Private
    /// <summary>Initializes or verifies immutable world/layout metadata, rejecting accidental reuse of another identity.</summary>
    private void VerifyIdentity()
    {
        using (var insert = sqliteConn.CreateCommand())
        {
            insert.CommandText = """
                INSERT OR IGNORE INTO ecosystem_identity(id,world_id,world_width,world_height,world_depth,sea_level,region_size,chunk_size)
                VALUES(1,$id,$width,$height,$depth,$sea,$region,$chunk);
                """;
            insert.Parameters.AddWithValue("$id", identity.SavegameIdentifier);
            insert.Parameters.AddWithValue("$width", identity.WorldWidth);
            insert.Parameters.AddWithValue("$depth", identity.WorldDepth);
            insert.Parameters.AddWithValue("$height", identity.WorldHeight);
            insert.Parameters.AddWithValue("$sea", identity.SeaLevel);
            insert.Parameters.AddWithValue("$region", identity.RegionSize);
            insert.Parameters.AddWithValue("$chunk", identity.ChunkSize);
            insert.ExecuteNonQuery();
        }
        using var command = sqliteConn.CreateCommand();
        command.CommandText = "SELECT world_id,world_width,world_height,world_depth,sea_level,region_size,chunk_size FROM ecosystem_identity WHERE id=1";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetString(0) != identity.SavegameIdentifier || reader.GetInt32(1) != identity.WorldWidth || reader.GetInt32(2) != identity.WorldHeight
            || reader.GetInt32(3) != identity.WorldDepth || reader.GetInt32(4) != identity.SeaLevel || reader.GetInt32(5) != identity.RegionSize || reader.GetInt32(6) != identity.ChunkSize)
            throw new InvalidDataException("The ecosystem database identity or sampling layout does not match this world.");
    }

    /// <summary>Adds dimension-aware coordinate parameters without using any vanilla pixel-record identifiers.</summary>
    private static void AddKey(SqliteCommand command, int x, int z, int dimension)
    {
        command.Parameters.AddWithValue("$d", dimension);
        command.Parameters.AddWithValue("$x", x);
        command.Parameters.AddWithValue("$z", z);
    }

    /// <summary>Encodes an available component or stores an explicit SQL null for missing data.</summary>
    private static object Encode(EnvironmentalMap? map) => map == null ? DBNull.Value : EnvironmentalCompression.Encode(EnvironmentalRecordCodec.EncodeMap(map));
    /// <summary>Validates a component independently so one unavailable input cannot make other components look absent.</summary>
    private static EnvironmentalMap? ReadMap(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : EnvironmentalRecordCodec.DecodeMap(EnvironmentalCompression.Decode((byte[])reader.GetValue(index)));
    #endregion
}
