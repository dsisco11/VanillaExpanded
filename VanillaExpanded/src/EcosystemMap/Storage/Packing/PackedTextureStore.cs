using Microsoft.Data.Sqlite;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Reads/writes only derived upload tables using the owning database worker's existing connection and transactions.</summary>
internal sealed class PackedTextureStore
{
    private readonly SqliteConnection connection;

    #region Public API
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

    #region Private
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
