using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ash.Core;

namespace Ash.Sim;

/// <summary>
/// The authoritative object-world state a save file holds. Derived caches —
/// spatial buckets, support back-links, visible-object lists, render sort order
/// — are never saved: they are rebuilt from this on load.
/// </summary>
public sealed record ObjectWorldSnapshot(
    string ContentFingerprint,
    long SimulationTick,
    ushort CurrentMapId,
    ObjectStoreSnapshot Objects);

public sealed class ObjectWorldSaveException : InvalidOperationException
{
    public ObjectWorldSaveException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Save v1's canonical binary format. Every value is a fixed-width
/// little-endian integer or a length-prefixed UTF-8 string: no floating point,
/// no platform-native sizes, no timestamps, no unordered dictionary output. The
/// same world therefore always produces the same bytes, which is what makes
/// <c>save → load → save</c> byte identity a real test rather than a hope.
/// </summary>
public static class ObjectWorldSave
{
    /// <summary>"ASHW", the file magic.</summary>
    public static readonly byte[] Magic = "ASHW"u8.ToArray();

    public const int FormatVersion = 1;

    /// <summary>
    /// The oldest reader that can still make sense of this file. A reader below
    /// this refuses the file instead of guessing at it.
    /// </summary>
    public const int MinimumReaderVersion = 1;

    /// <summary>A sanity bound so a corrupt length cannot ask for a huge buffer.</summary>
    public const int MaximumPayloadBytes = 256 * 1024 * 1024;

    public static byte[] Write(ObjectWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var payload = WritePayload(snapshot);
        var checksum = SHA256.HashData(payload);
        using var buffer = new MemoryStream();
        buffer.Write(Magic);
        WriteInt32(buffer, FormatVersion);
        WriteInt32(buffer, MinimumReaderVersion);
        WriteText(buffer, snapshot.ContentFingerprint);
        WriteInt64(buffer, snapshot.SimulationTick);
        WriteUInt16(buffer, snapshot.CurrentMapId);
        WriteInt32(buffer, payload.Length);
        buffer.Write(checksum);
        buffer.Write(payload);
        return buffer.ToArray();
    }

    public static ObjectWorldSnapshot Read(
        ReadOnlySpan<byte> bytes,
        string expectedFingerprint)
    {
        ArgumentNullException.ThrowIfNull(expectedFingerprint);
        var reader = new SpanReader(bytes);
        if (!reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
        {
            throw new ObjectWorldSaveException(
                "This is not an Ash object-world save: the magic does not match.");
        }

        var formatVersion = reader.ReadInt32();
        var minimumReader = reader.ReadInt32();
        if (minimumReader > FormatVersion)
        {
            throw new ObjectWorldSaveException(
                $"The save requires a reader for format {minimumReader}; this " +
                $"build reads format {FormatVersion}.");
        }

        if (formatVersion != FormatVersion)
        {
            // Compatible older versions migrate through explicit, tested
            // transforms. There is no such version yet, so there is nothing to
            // guess at here.
            throw new ObjectWorldSaveException(
                $"Save format {formatVersion} has no migration to " +
                $"{FormatVersion}.");
        }

        var fingerprint = reader.ReadText();
        if (!string.Equals(fingerprint, expectedFingerprint, StringComparison.Ordinal))
        {
            throw new ObjectWorldSaveException(
                $"The save was written for content '{fingerprint}' but this " +
                $"build is '{expectedFingerprint}'.");
        }

        var tick = reader.ReadInt64();
        var currentMapId = reader.ReadUInt16();
        var payloadLength = reader.ReadInt32();
        if (payloadLength < 0 || payloadLength > MaximumPayloadBytes)
        {
            throw new ObjectWorldSaveException(
                $"The save declares an impossible payload length of " +
                $"{payloadLength} bytes.");
        }

        var checksum = reader.ReadBytes(SHA256.HashSizeInBytes);
        var payload = reader.ReadBytes(payloadLength);
        if (!reader.AtEnd)
        {
            throw new ObjectWorldSaveException(
                "The save has trailing bytes after its payload.");
        }

        if (!SHA256.HashData(payload).AsSpan().SequenceEqual(checksum))
        {
            throw new ObjectWorldSaveException(
                "The save payload does not match its checksum.");
        }

        return new ObjectWorldSnapshot(
            fingerprint,
            tick,
            currentMapId,
            ReadPayload(payload));
    }

    /// <summary>
    /// Writes through a sibling temporary file whose checksum is verified before
    /// the destination is replaced, so a crash leaves either the previous valid
    /// save or the new one, never a half-written file.
    /// </summary>
    public static void WriteFile(string path, ObjectWorldSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = Write(snapshot);
        var temporary = path + ".tmp";
        using (var file = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
        {
            file.Write(bytes);
            file.Flush(flushToDisk: true);
        }

        // Read the temporary file back before it becomes the save: a file that
        // cannot be loaded must never replace one that can.
        _ = Read(File.ReadAllBytes(temporary), snapshot.ContentFingerprint);
        File.Move(temporary, path, overwrite: true);
    }

    public static ObjectWorldSnapshot ReadFile(
        string path,
        string expectedFingerprint) =>
        Read(File.ReadAllBytes(path), expectedFingerprint);

    private static byte[] WritePayload(ObjectWorldSnapshot snapshot)
    {
        using var buffer = new MemoryStream();
        var slots = snapshot.Objects.Slots;
        WriteInt32(buffer, slots.Count);
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            buffer.WriteByte(slot.IsAlive ? (byte)1 : (byte)0);
            buffer.WriteByte(slot.Generation);
            if (!slot.IsAlive)
            {
                continue;
            }

            WriteObject(
                buffer,
                slot.Value ?? throw new ObjectWorldSaveException(
                    $"Slot {index} is live but carries no object."));
        }

        WriteInt32(buffer, snapshot.Objects.FreeSlots.Count);
        foreach (var free in snapshot.Objects.FreeSlots)
        {
            WriteInt32(buffer, free);
        }

        return buffer.ToArray();
    }

    private static ObjectStoreSnapshot ReadPayload(ReadOnlySpan<byte> payload)
    {
        var reader = new SpanReader(payload);
        var slotCount = reader.ReadInt32();
        if (slotCount < 0 || slotCount > ObjectId.MaxIndex + 1)
        {
            throw new ObjectWorldSaveException(
                $"The save declares an impossible slot count of {slotCount}.");
        }

        var slots = new List<ObjectSlotSnapshot>(slotCount);
        for (var index = 0; index < slotCount; index++)
        {
            var alive = reader.ReadByte();
            if (alive > 1)
            {
                throw new ObjectWorldSaveException(
                    $"Slot {index} has an invalid live flag.");
            }

            var generation = reader.ReadByte();
            slots.Add(
                new ObjectSlotSnapshot(
                    alive == 1,
                    generation,
                    alive == 1 ? ReadObject(ref reader, index, generation) : null));
        }

        var freeCount = reader.ReadInt32();
        if (freeCount < 0 || freeCount > slotCount)
        {
            throw new ObjectWorldSaveException(
                $"The save declares an impossible free-slot count of " +
                $"{freeCount}.");
        }

        var free = new List<int>(freeCount);
        for (var index = 0; index < freeCount; index++)
        {
            var slot = reader.ReadInt32();
            if (slot < 0 || slot >= slotCount)
            {
                throw new ObjectWorldSaveException(
                    $"Free-slot entry {index} points outside the store.");
            }

            free.Add(slot);
        }

        if (!reader.AtEnd)
        {
            throw new ObjectWorldSaveException(
                "The payload has trailing bytes after the object store.");
        }

        return new ObjectStoreSnapshot(slots, free);
    }

    private static void WriteObject(Stream buffer, WorldObject value)
    {
        WriteText(buffer, value.TypeId);
        WriteText(buffer, value.Name);
        WriteText(buffer, value.ShapeId);
        WriteInt32(buffer, value.FrameId);
        WriteLocation(buffer, value.Location);
        WriteInt32(buffer, value.Footprint.Width);
        WriteInt32(buffer, value.Footprint.Depth);
        WriteInt32(buffer, value.Height);
        WriteInt32(buffer, value.StepHeight);
        buffer.WriteByte((byte)value.Motion);
        WriteInt32(buffer, value.VerticalVelocity);
        WriteSupport(buffer, value.Support);
        WriteInt32(buffer, (int)value.Flags);
        WriteUInt16(buffer, (ushort)value.EquipmentSlots);
        WriteInt32(buffer, value.Quality);
        WriteInt32(buffer, value.Quantity);
        WriteInt32(buffer, value.Condition);
        WriteInt32(buffer, value.Health);
        WriteInt32(buffer, value.MaxHealth);
        WriteInt32(buffer, value.ContainerCapacity);
        buffer.WriteByte(value.IsContainerOpen ? (byte)1 : (byte)0);
    }

    private static WorldObject ReadObject(
        ref SpanReader reader,
        int index,
        byte generation)
    {
        if (generation == 0)
        {
            throw new ObjectWorldSaveException(
                $"Live slot {index} has the reserved generation zero.");
        }

        var typeId = reader.ReadText();
        var name = reader.ReadText();
        var shapeId = reader.ReadText();
        var frameId = reader.ReadInt32();
        var location = ReadLocation(ref reader);
        var footprint = new ObjectFootprint(
            reader.ReadInt32(),
            reader.ReadInt32());
        var height = reader.ReadInt32();
        var stepHeight = reader.ReadInt32();
        var motion = reader.ReadByte();
        if (motion > (byte)MotionState.Falling)
        {
            throw new ObjectWorldSaveException(
                $"Slot {index} has an unknown motion state {motion}.");
        }

        var verticalVelocity = reader.ReadInt32();
        var support = ReadSupport(ref reader);
        var flags = (ObjectFlags)reader.ReadInt32();
        var equipmentSlots = (EquipmentSlotMask)reader.ReadUInt16();
        var quality = reader.ReadInt32();
        var quantity = reader.ReadInt32();
        var condition = reader.ReadInt32();
        var health = reader.ReadInt32();
        var maxHealth = reader.ReadInt32();
        var containerCapacity = reader.ReadInt32();
        var containerOpen = reader.ReadByte() == 1;
        return new WorldObject(
            ObjectId.FromParts(index, generation),
            typeId,
            name,
            shapeId,
            frameId,
            location,
            footprint,
            height,
            stepHeight,
            (MotionState)motion,
            verticalVelocity,
            support,
            flags,
            equipmentSlots,
            quality,
            quantity,
            condition,
            health,
            maxHealth,
            containerCapacity,
            containerOpen);
    }

    private static void WriteLocation(Stream buffer, ObjectLocation location)
    {
        buffer.WriteByte((byte)location.Kind);
        switch (location.Kind)
        {
            case LocationKind.OnMap:
                WriteUInt16(buffer, location.MapId);
                WriteInt32(buffer, location.Position.X);
                WriteInt32(buffer, location.Position.Y);
                WriteInt32(buffer, location.Position.Z);
                break;
            case LocationKind.InContainer:
                WriteUInt32(buffer, location.Parent.Value);
                break;
            case LocationKind.Equipped:
                WriteUInt32(buffer, location.Parent.Value);
                buffer.WriteByte(location.Slot);
                break;
            case LocationKind.InTransfer:
                WriteUInt32(buffer, location.TransferId);
                break;
            default:
                throw new ObjectWorldSaveException(
                    "A live object cannot have an invalid location.");
        }
    }

    private static ObjectLocation ReadLocation(ref SpanReader reader)
    {
        var kind = reader.ReadByte();
        return kind switch
        {
            (byte)LocationKind.OnMap => ObjectLocation.OnMap(
                reader.ReadUInt16(),
                new Vec3i(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32())),
            (byte)LocationKind.InContainer => ObjectLocation.InContainer(
                ObjectIdFromValue(reader.ReadUInt32())),
            (byte)LocationKind.Equipped => ObjectLocation.Equipped(
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadByte()),
            (byte)LocationKind.InTransfer => ObjectLocation.InTransfer(
                reader.ReadUInt32()),
            _ => throw new ObjectWorldSaveException(
                $"The save holds an unknown location kind {kind}."),
        };
    }

    private static void WriteSupport(Stream buffer, SupportRef support)
    {
        buffer.WriteByte((byte)support.Kind);
        switch (support.Kind)
        {
            case SupportKind.None:
                break;
            case SupportKind.Terrain:
                WriteUInt16(buffer, support.MapId);
                WriteInt32(buffer, support.CellX);
                WriteInt32(buffer, support.CellY);
                WriteInt32(buffer, support.TopZ);
                break;
            case SupportKind.Object:
                WriteUInt32(buffer, support.ObjectId.Value);
                WriteInt32(buffer, support.TopZ);
                break;
            default:
                throw new ObjectWorldSaveException(
                    $"Unknown support kind {support.Kind}.");
        }
    }

    private static SupportRef ReadSupport(ref SpanReader reader)
    {
        var kind = reader.ReadByte();
        return kind switch
        {
            (byte)SupportKind.None => SupportRef.None,
            (byte)SupportKind.Terrain => SupportRef.Terrain(
                reader.ReadUInt16(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32()),
            (byte)SupportKind.Object => SupportRef.Object(
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadInt32()),
            _ => throw new ObjectWorldSaveException(
                $"The save holds an unknown support kind {kind}."),
        };
    }

    private static ObjectId ObjectIdFromValue(uint value)
    {
        if (value == 0)
        {
            return ObjectId.None;
        }

        var generation = (byte)(value >> 24);
        if (generation == 0)
        {
            throw new ObjectWorldSaveException(
                $"Handle {value} has the reserved generation zero.");
        }

        return ObjectId.FromParts((int)(value & 0x00FF_FFFF), generation);
    }

    private static void WriteInt32(Stream buffer, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        buffer.Write(bytes);
    }

    private static void WriteUInt32(Stream buffer, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        buffer.Write(bytes);
    }

    private static void WriteInt64(Stream buffer, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        buffer.Write(bytes);
    }

    private static void WriteUInt16(Stream buffer, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        buffer.Write(bytes);
    }

    private static void WriteText(Stream buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(buffer, bytes.Length);
        buffer.Write(bytes);
    }

    /// <summary>
    /// A forward-only reader that refuses to read past the end, so a truncated
    /// or lying length fails as a save error instead of an index exception.
    /// </summary>
    private ref struct SpanReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _position;

        public readonly bool AtEnd => _position == _bytes.Length;

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (count < 0 || _position + count > _bytes.Length)
            {
                throw new ObjectWorldSaveException(
                    $"The save ends after {_bytes.Length} bytes but {count} " +
                    $"more were required at offset {_position}.");
            }

            var slice = _bytes.Slice(_position, count);
            _position += count;
            return slice;
        }

        public byte ReadByte() => ReadBytes(1)[0];

        public int ReadInt32() =>
            BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(sizeof(int)));

        public uint ReadUInt32() =>
            BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(sizeof(uint)));

        public long ReadInt64() =>
            BinaryPrimitives.ReadInt64LittleEndian(ReadBytes(sizeof(long)));

        public ushort ReadUInt16() =>
            BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(sizeof(ushort)));

        public string ReadText()
        {
            var length = ReadInt32();
            if (length < 0 || length > MaximumPayloadBytes)
            {
                throw new ObjectWorldSaveException(
                    $"The save declares an impossible text length of {length}.");
            }

            return Encoding.UTF8.GetString(ReadBytes(length));
        }
    }
}
