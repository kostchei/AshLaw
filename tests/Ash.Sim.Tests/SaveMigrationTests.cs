using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ash.Core;

namespace Ash.Sim.Tests;

/// <summary>
/// The first real format migration. Version 2 files exist, so version 3 has to
/// read them rather than refuse them, and the transform has to be exact: it may
/// not invent stack capacity that version 2 never had.
/// </summary>
public sealed class SaveMigrationTests
{
    private const string Fingerprint = "ash.test-content.v1";

    [Fact]
    public void AVersion2SaveLoadsAndItsQuantitiesBecomeExactStackLimits()
    {
        var bytes = WriteVersion2Save(quantity: 7);

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(bytes, Fingerprint));

        var coins = loaded.Objects.Enumerate().Single();
        Assert.Equal("Gold Coins", coins.Name);
        Assert.Equal(7, coins.Quantity);

        // Exactly what it carried: a version 2 stack could never grow, so the
        // migration must not hand it room it never had.
        Assert.Equal(7, coins.MaxQuantity);
        Assert.True(coins.HasFlag(ObjectFlags.Stackable));
        Assert.Equal(42, loaded.SimulationTick);
        Assert.Equal(
            new TerrainCell(16, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport),
            loaded.Maps.Single().GetTerrain(1, 0));
        loaded.Objects.ValidateInvariants();
        loaded.Maps.Single().Dispose();
    }

    [Fact]
    public void ASingleObjectFromVersion2StaysUnstackable()
    {
        var bytes = WriteVersion2Save(quantity: 1);

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(bytes, Fingerprint));

        var item = loaded.Objects.Enumerate().Single();
        Assert.Equal(1, item.Quantity);
        Assert.Equal(1, item.MaxQuantity);
        Assert.False(item.HasFlag(ObjectFlags.Stackable));
        loaded.Maps.Single().Dispose();
    }

    [Fact]
    public void AMigratedSaveRewrittenAtTheCurrentVersionRoundTrips()
    {
        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(WriteVersion2Save(quantity: 7), Fingerprint));

        var rewritten = ObjectWorldSave.Write(
            ObjectWorldSave.Capture(loaded.Objects, Fingerprint, 42, 0));
        var reread = ObjectWorldSave.Read(rewritten, Fingerprint);

        Assert.Equal(
            ObjectWorldSave.FormatVersion,
            BinaryPrimitives.ReadInt32LittleEndian(
                rewritten.AsSpan(ObjectWorldSave.Magic.Length)));
        Assert.Equal(
            loaded.Objects.Enumerate().ToArray(),
            ObjectStore.Restore(reread.Objects).Enumerate().ToArray());
        loaded.Maps.Single().Dispose();
    }

    [Fact]
    public void AVersionWithNoMigrationIsRefused()
    {
        var version1 = WriteVersion2Save(quantity: 1, formatVersion: 1);
        var future = WriteVersion2Save(
            quantity: 1,
            formatVersion: ObjectWorldSave.FormatVersion + 1);

        var old = Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(version1, Fingerprint));
        var ahead = Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(future, Fingerprint));

        Assert.Contains("no migration", old.Message);
        Assert.Contains("no migration", ahead.Message);
    }

    /// <summary>
    /// Builds a save in the version 2 layout by hand: one 2x1 map and one
    /// object. Writing it out here rather than committing a binary keeps the
    /// old layout readable, which is the point of the fixture.
    /// </summary>
    private static byte[] WriteVersion2Save(
        int quantity,
        int formatVersion = 2)
    {
        using var payload = new MemoryStream();

        // One map: id 0, 2x1 cells, default bucket size.
        Int32(payload, 1);
        UInt16(payload, 0);
        Int32(payload, 2);
        Int32(payload, 1);
        Int32(payload, WorldMap.DefaultBucketSize);
        Int32(payload, 0);
        payload.WriteByte(
            (byte)(TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));
        Int32(payload, 16);
        payload.WriteByte(
            (byte)(TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));

        // One live slot holding the object.
        Int32(payload, 1);
        payload.WriteByte(1);
        payload.WriteByte(1);
        Text(payload, "item.gold");
        Text(payload, quantity > 1 ? "Gold Coins" : "Gold Coin");
        Text(payload, "loot.generic");
        Int32(payload, 0);
        payload.WriteByte((byte)LocationKind.OnMap);
        UInt16(payload, 0);
        Int32(payload, 256);
        Int32(payload, 256);
        Int32(payload, 0);
        Int32(payload, 32);
        Int32(payload, 32);
        Int32(payload, 8);
        Int32(payload, 0);
        payload.WriteByte((byte)MotionState.Resting);
        Int32(payload, 0);
        payload.WriteByte((byte)SupportKind.None);
        Int32(payload, (int)(ObjectFlags.Item | ObjectFlags.Movable));
        UInt16(payload, 0);
        Int32(payload, 0);
        Int32(payload, quantity);

        // No MaxQuantity field: that is exactly what version 2 lacked.
        Int32(payload, 100);
        Int32(payload, 0);
        Int32(payload, 0);
        Int32(payload, 0);
        payload.WriteByte(0);

        // No free slots.
        Int32(payload, 0);

        var body = payload.ToArray();
        using var file = new MemoryStream();
        file.Write(ObjectWorldSave.Magic);
        Int32(file, formatVersion);
        Int32(file, 2);
        Text(file, Fingerprint);
        Int64(file, 42);
        UInt16(file, 0);
        Int32(file, body.Length);
        file.Write(SHA256.HashData(body));
        file.Write(body);
        return file.ToArray();
    }

    private static void Int32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Int64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void UInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Text(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Int32(stream, bytes.Length);
        stream.Write(bytes);
    }
}
