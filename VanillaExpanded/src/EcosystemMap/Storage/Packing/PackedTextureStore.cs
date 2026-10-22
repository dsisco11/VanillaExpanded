using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Reads/writes only derived upload tables using the owning database worker's existing connection and transactions.</summary>
internal sealed class PackedTextureStore : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly Dictionary<(bool Terrain, int Count), SqliteCommand> reads = new();

    #region Public API
    #region Single records
    /// <summary>Borrows the feature connection; lifetime, original records and all scheduling remain owned by EcosystemDatabase.</summary>
    internal PackedTextureStore(SqliteConnection connection) { this.connection = connection; }
    /// <summary>Reads reusable RGBA bytes for this original region, rejecting unknown or malformed cached layouts.</summary>
    internal PackedRegionalData? ReadRegion(EcosystemRegionKey key) => PackedTextureCodec.DecodeRegion(
        Read("ecosystem_region_upload", "ecosystem_region", EcosystemDatabase.RegionalRecordVersion, key.X, key.Z, key.Dimension));
    /// <summary>Stores the region buffer in the same transaction as a source update, or as one atomic local cache rebuild.</summary>
    internal void WriteRegion(EcosystemRegionKey key, PackedRegionalData data, SqliteTransaction? transaction = null) =>
        Write("ecosystem_region_upload", key.X, key.Z, key.Dimension, PackedTextureCodec.EncodeRegion(data), transaction);
    /// <summary>Reads interleaved height/flag words with a decoded bound derived from this world's chunk geometry.</summary>
    internal PackedTerrainData? ReadTile(EcosystemTileKey key, int size, EcosystemRegionKey region) => PackedTextureCodec.DecodeTile(
        Read("ecosystem_tile_upload", "ecosystem_map_piece", EcosystemDatabase.TileRecordVersion, key.X, key.Z, key.Dimension,
            checked(12 + size * size * 4), region), size);
    /// <summary>Stores RG16UI words atomically with their source tile, or during a worker-owned local cache rebuild.</summary>
    internal void WriteTile(EcosystemTileKey key, int size, ushort[] values, SqliteTransaction? transaction = null) =>
        Write("ecosystem_tile_upload", key.X, key.Z, key.Dimension, PackedTextureCodec.EncodeTile(values, size), transaction);
    #endregion
    #region Batched records and lifetime
    /// <summary>Runs at most two bounded selects, preserving missing results and exact requested coordinate identities.</summary>
    internal (Dictionary<EcosystemRegionKey, PackedRegionalData?> Regions, Dictionary<EcosystemTileKey, PackedTerrainData?> Tiles)
        ReadBatch(IReadOnlyCollection<EcosystemRegionKey> regions, IReadOnlyCollection<EcosystemTileKey> tiles, EcosystemStorageIdentity identity)
    {
        var regional = regions.ToDictionary(key => key, _ => (PackedRegionalData?)null);
        var terrain = tiles.ToDictionary(key => key, _ => (PackedTerrainData?)null);
        if (regional.Count != 0)
        {
            var command = BatchCommand(false, regional.Count);
            int index = 0;
            foreach (var key in regional.Keys) SetBatchKey(command, index++, key.Dimension, key.X, key.Z);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = new EcosystemRegionKey(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(0));
                if (reader.GetInt32(3) == PackedTextureCodec.Version && reader.GetValue(4) is byte[] payload)
                    regional[key] = PackedTextureCodec.DecodeRegion(EnvironmentalCompression.Decode(payload));
            }
        }
        if (terrain.Count != 0)
        {
            var command = BatchCommand(true, terrain.Count);
            int index = 0;
            foreach (var key in terrain.Keys)
            {
                SetBatchKey(command, index, key.Dimension, key.X, key.Z);
                var region = identity.RegionFor(key);
                command.Parameters["$rx" + index].Value = region.X;
                command.Parameters["$rz" + index++].Value = region.Z;
            }
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = new EcosystemTileKey(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(0));
                if (reader.GetInt32(3) == PackedTextureCodec.Version && reader.GetValue(4) is byte[] payload)
                    terrain[key] = PackedTextureCodec.DecodeTile(EnvironmentalCompression.Decode(payload,
                        checked(12 + identity.ChunkSize * identity.ChunkSize * 4)), identity.ChunkSize);
            }
        }
        return (regional, terrain);
    }

    /// <summary>Disposes the finite command cache on its owning storage worker.</summary>
    public void Dispose()
    {
        foreach (var command in reads.Values) command.Dispose();
        reads.Clear();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Prepares one reusable key-set query for each bounded shape; all coordinates remain SQL parameters.</summary>
    private SqliteCommand BatchCommand(bool terrain, int count)
    {
        if (count <= 0 || count > PackedReadBatch.MaximumRecords) throw new ArgumentOutOfRangeException(nameof(count));
        if (reads.TryGetValue((terrain, count), out var cached)) return cached;
        var command = connection.CreateCommand();
        string keys = string.Join(",", Enumerable.Range(0, count).Select(index => terrain
            ? $"($d{index},$x{index},$z{index},$rx{index},$rz{index})" : $"($d{index},$x{index},$z{index})"));
        string columns = terrain ? "d,x,z,rx,rz" : "d,x,z";
        string table = terrain ? "ecosystem_tile_upload" : "ecosystem_region_upload";
        string original = terrain ? "ecosystem_map_piece" : "ecosystem_region";
        command.CommandText = $"WITH wanted({columns}) AS (VALUES {keys}) SELECT c.dimension,c.x,c.z,c.version,c.payload FROM wanted w "
            + $"JOIN {table} c ON c.dimension=w.d AND c.x=w.x AND c.z=w.z "
            + $"JOIN {original} s ON s.dimension=c.dimension AND s.x=c.x AND s.z=c.z WHERE s.version=$version"
            + (terrain ? " AND s.region_x=w.rx AND s.region_z=w.rz" : "");
        command.Parameters.AddWithValue("$version", terrain ? EcosystemDatabase.TileRecordVersion : EcosystemDatabase.RegionalRecordVersion);
        for (int index = 0; index < count; index++)
        {
            command.Parameters.AddWithValue("$d" + index, 0);
            command.Parameters.AddWithValue("$x" + index, 0);
            command.Parameters.AddWithValue("$z" + index, 0);
            if (terrain)
            {
                command.Parameters.AddWithValue("$rx" + index, 0);
                command.Parameters.AddWithValue("$rz" + index, 0);
            }
        }
        command.Prepare();
        reads.Add((terrain, count), command);
        return command;
    }

    /// <summary>Rebinds a coordinate without constructing new parameter objects or SQL text.</summary>
    private static void SetBatchKey(SqliteCommand command, int index, int dimension, int x, int z)
    {
        command.Parameters["$d" + index].Value = dimension;
        command.Parameters["$x" + index].Value = x;
        command.Parameters["$z" + index].Value = z;
    }

    /// <summary>Reads only a fixed internal table and validates compression before decoding channel metadata.</summary>
    private byte[]? Read(string table, string originalTable, int originalVersion, int x, int z, int dimension, int maximum = EnvironmentalCompression.MaximumDecodedLength, EcosystemRegionKey? region = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT c.version,c.payload FROM {table} c JOIN {originalTable} s ON c.dimension=s.dimension AND c.x=s.x AND c.z=s.z "
            + "WHERE c.dimension=$d AND c.x=$x AND c.z=$z AND s.version=$originalVersion";
        if (region is { } expected)
        {
            command.CommandText += " AND s.region_x=$rx AND s.region_z=$rz";
            command.Parameters.AddWithValue("$rx", expected.X);
            command.Parameters.AddWithValue("$rz", expected.Z);
        }
        command.Parameters.AddWithValue("$originalVersion", originalVersion);
        AddKey(command, x, z, dimension);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != PackedTextureCodec.Version) return null;
        return reader.GetValue(1) is byte[] payload ? EnvironmentalCompression.Decode(payload, maximum) : null;
    }
    /// <summary>Writes a compressed derived buffer; table identifiers are fixed feature constants, never external inputs.</summary>
    private void Write(string table, int x, int z, int dimension, byte[] payload, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {table}(dimension,x,z,version,payload) VALUES($d,$x,$z,$v,$p) "
            + "ON CONFLICT(dimension,x,z) DO UPDATE SET version=excluded.version,payload=excluded.payload";
        AddKey(command, x, z, dimension);
        command.Parameters.AddWithValue("$v", PackedTextureCodec.Version);
        command.Parameters.AddWithValue("$p", EnvironmentalCompression.Encode(payload));
        command.ExecuteNonQuery();
    }
    /// <summary>Uses the same dimension-aware coordinate identity as the original records in this feature database.</summary>
    private static void AddKey(SqliteCommand command, int x, int z, int dimension)
    {
        command.Parameters.AddWithValue("$d", dimension);
        command.Parameters.AddWithValue("$x", x);
        command.Parameters.AddWithValue("$z", z);
    }
    #endregion
}
