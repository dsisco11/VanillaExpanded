using System.Buffers.Binary;
using Moq;
using VanillaExpanded.EcosystemMap.Acquisition;
using VanillaExpanded.EcosystemMap.Storage;
using VanillaExpanded.EcosystemMap.Transport;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VanillaExpanded.Tests.Unit.EcosystemMap;

/// <summary>Verifies original shrub transport, normal-delivery scope and per-connection content revisions.</summary>
public sealed class ShrubTransportTests
{
    #region Public API
    /// <summary>Preserves original integer bits, padding and negative regional coordinates through wire and persistent storage.</summary>
    [Fact]
    public async Task OriginalShrubsRoundTripThroughPayloadAndReopen()
    {
        using var fixture = new StorageFixture();
        var key = new EcosystemRegionKey(-3, 7);
        var original = StorageFixture.Map();
        var payload = ShrubPayload.Encode(key, original);
        var decoded = ShrubPayload.Decode(payload)!;
        Assert.Equal(key, decoded.Key);
        Assert.Equal(original.Size, decoded.Shrubs!.Size);
        Assert.Equal(original.TopLeftPadding, decoded.Shrubs.TopLeftPadding);
        Assert.Equal(original.BottomRightPadding, decoded.Shrubs.BottomRightPadding);
        Assert.Equal(original.Values, decoded.Shrubs.Values);
        await using (var storage = fixture.Open())
            await storage.WriteRegionAsync(decoded, storage.CurrentEpoch);
        await using var reopened = fixture.Open();
        Assert.Equal(original.Values, (await reopened.ReadRegionAsync(key)).Value!.Shrubs!.Values);
    }

    /// <summary>Rejects unsupported wire/component versions, hostile lengths and invalid padding before accepting an input.</summary>
    [Fact]
    public void MalformedPayloadsRemainUnavailable()
    {
        byte[] good = ShrubPayload.Encode(new(1, 2), StorageFixture.Map());
        Assert.Null(ShrubPayload.Decode(null));
        for (int count = 0; count < good.Length; count++) Assert.Null(ShrubPayload.Decode(good[..count]));
        foreach (var change in new[] { (0, 99), (12, 99), (16, int.MaxValue), (20, -1), (24, 3), (28, int.MaxValue) })
        {
            byte[] bad = (byte[])good.Clone();
            BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(change.Item1), change.Item2);
            Assert.Null(ShrubPayload.Decode(bad));
        }
        Assert.Null(ShrubPayload.Decode(good.Concat(new byte[1]).ToArray()));
        Assert.Throws<ArgumentException>(() => ShrubPayload.Encode(new(0, 0, 1), StorageFixture.Map()));
    }

    /// <summary>Early obligations survive initialization, unchanged arrays deduplicate, changed originals resend and reconnects start fresh.</summary>
    [Fact]
    public void OrdinaryDeliveryUsesConnectionGenerationAndContentRevision()
    {
        var delivery = new ShrubDelivery<object>();
        var first = new object();
        var key = new EcosystemRegionKey(5, 6);
        EnvironmentalMap current = StorageFixture.Map();
        var sent = new List<byte[]>();
        int reads = 0;
        /// <summary>Counts loaded-only reads and verifies the exact observed regional identity.</summary>
        EnvironmentalMap Read(EcosystemRegionKey requested) { Assert.Equal(key, requested); reads++; return current; }
        delivery.Observe(first, key);
        delivery.Drain(first, false, Read, sent.Add);
        Assert.Empty(sent);
        Assert.Equal(0, reads);
        delivery.Drain(first, true, Read, sent.Add);
        Assert.Single(sent);
        delivery.Observe(first, key);
        delivery.Drain(first, true, Read, sent.Add);
        Assert.Single(sent);
        current = StorageFixture.Map(1);
        delivery.Observe(first, key);
        delivery.Drain(first, true, Read, sent.Add);
        Assert.Equal(2, sent.Count);
        var reconnect = new object();
        delivery.RetainConnections(new[] { reconnect });
        delivery.Drain(first, true, Read, sent.Add);
        Assert.Equal(2, sent.Count);
        delivery.Observe(reconnect, key);
        delivery.Drain(reconnect, true, Read, sent.Add);
        Assert.Equal(3, sent.Count);
        delivery.Clear();
        delivery.Drain(reconnect, true, Read, sent.Add);
        Assert.Equal(3, sent.Count);
    }


    /// <summary>Revision identity includes coordinates, original layout and values without allocating a wire payload.</summary>
    [Fact]
    public void RevisionTracksCoordinatesLayoutAndEveryValue()
    {
        var key = new EcosystemRegionKey(3, 4);
        var map = StorageFixture.Map();
        ulong revision = ShrubRevision.Compute(key, map);
        Assert.Equal(revision, ShrubRevision.Compute(key, StorageFixture.Map()));
        Assert.NotEqual(revision, ShrubRevision.Compute(new(4, 4), map));
        Assert.NotEqual(revision, ShrubRevision.Compute(new(3, 5), map));
        Assert.NotEqual(revision, ShrubRevision.Compute(new(3, 4, 1), map));
        Assert.NotEqual(revision, ShrubRevision.Compute(key, new EnvironmentalMap(3, 0, 1, map.Values)));
        for (int index = 0; index < map.Values.Count; index++)
        {
            int[] changed = map.Values.ToArray();
            changed[index] ^= int.MinValue;
            Assert.NotEqual(revision, ShrubRevision.Compute(key, new EnvironmentalMap(3, 1, 0, changed)));
        }
    }

    /// <summary>Missing regions stay unavailable; neither panning nor repeated ticks invent an unobserved delivery request.</summary>
    [Fact]
    public void MissingAndUnobservedRegionsDoNotTriggerRetrieval()
    {
        var delivery = new ShrubDelivery<object>();
        var connection = new object();
        int reads = 0;
        var sent = new List<byte[]>();
        delivery.Drain(connection, true, _ => { reads++; return StorageFixture.Map(); }, sent.Add);
        Assert.Equal(0, reads);
        delivery.Observe(connection, new(2, 3));
        delivery.Drain(connection, true, _ => { reads++; return null; }, sent.Add);
        delivery.Drain(connection, true, _ => { reads++; return StorageFixture.Map(); }, sent.Add);
        Assert.Equal(1, reads);
        Assert.Empty(sent);
        delivery.Observe(connection, new(2, 3)); // Ordinary revisit re-establishes availability.
        delivery.Drain(connection, true, _ => StorageFixture.Map(), sent.Add);
        Assert.Single(sent);
    }

    /// <summary>Reads the server's stored shrub map even when its final climate was forced after generation.</summary>
    [Fact]
    public void ForcedClimateDoesNotRegenerateOriginalShrubs()
    {
        var shrubs = new IntDataMap2D { Size = 3, TopLeftPadding = 1, Data = StorageFixture.Map().Values.ToArray() };
        var climate = new IntDataMap2D { Size = 2, Data = new[] { 99, 99, 99, 99 } };
        var region = new Mock<IMapRegion>(MockBehavior.Strict);
        region.SetupGet(value => value.ShrubMap).Returns(shrubs);
        region.SetupGet(value => value.ClimateMap).Returns(climate);
        var original = EnvironmentalSnapshots.Map(region.Object.ShrubMap)!;
        Array.Fill(climate.Data, 0);
        var decoded = ShrubPayload.Decode(ShrubPayload.Encode(new(4, 5), original))!;
        Assert.Equal(shrubs.Data, decoded.Shrubs!.Values);
        region.VerifyGet(value => value.ClimateMap, Times.Never);
    }
    #endregion
}
