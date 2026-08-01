using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ash.Core;
using Ash.Rules;

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
    ulong DiceState,
    ObjectStoreSnapshot Objects,
    IReadOnlyList<WorldMapSnapshot> Maps,
    IReadOnlyList<ActorConditionSnapshot>? Conditions = null,
    CombatDirectorSnapshot? Combat = null,
    WorldProgressSnapshot? Progress = null,
    WorldAiSnapshot? Ai = null);

/// <summary>
/// The parts of a world that are neither objects nor the fight the player is
/// in: what is currently in the air, what is being cast, what a mishap has
/// taken away, and whose day is going where.
/// </summary>
/// <remarks>
/// Grouped rather than added to <see cref="ObjectWorldSnapshot"/> one list at a
/// time because they share a property: each is a process attached to objects
/// that already save themselves, so none of them can be reconstructed from the
/// object graph and all of them are meaningless without it.
/// </remarks>
public sealed record WorldAiSnapshot(
    IReadOnlyList<ProjectileFlight> Projectiles,
    IReadOnlyList<SpellCast> Casts,
    IReadOnlyList<SpellLockout> Lockouts,
    IReadOnlyList<NpcScheduleSnapshot> Schedules)
{
    public static WorldAiSnapshot Empty { get; } = new([], [], [], []);
}

/// <summary>Character-origin facts and one-subzone campaign progress.</summary>
public sealed record WorldProgressSnapshot(
    CharacterCreationMethod Method,
    Ancestry Ancestry,
    AbilityScores? CreationScores,
    int CreationAttempts,
    int TalentRolls,
    ClassTalentRoll? FirstTalent,
    ClassTalentRoll? SecondTalent,
    ClassTalentRoll? AdvancementTalent,
    int Experience,
    bool DungeonCompleted);

/// <summary>
/// A world rebuilt from a save: a store, its maps and the tick they were saved
/// on. Loading builds this beside the live world and never mutates it.
/// </summary>
public sealed record LoadedObjectWorld(
    ObjectStore Objects,
    IReadOnlyList<WorldMap> Maps,
    long SimulationTick,
    ushort CurrentMapId,
    ulong DiceState,
    IReadOnlyList<ActorConditionSnapshot>? Conditions = null,
    CombatDirectorSnapshot? Combat = null,
    WorldProgressSnapshot? Progress = null,
    WorldAiSnapshot? Ai = null);

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

    /// <summary>
    /// Version 2 added the terrain section, version 3 stack limits, version 4
    /// gear slots, version 5 the actor sheet — ability scores, class, level and
    /// armour — and version 6 the injury state: the wound layer under
    /// concussion hits, the death clock, and which abilities a body has lost
    /// the reliable use of, and version 7 persistent actor conditions with
    /// exact expiry and next-periodic ticks, and version 8 explicit condition
    /// removal and stacking policies, and version 9 exact combat director
    /// state: active wind-ups, cooldowns, NPC decisions, movement ledgers and
    /// death-save timing, and version 10 character-creation provenance,
    /// rolled talents, experience and one-subzone completion, and version 11
    /// the player's resolved choice within every immutable talent roll, and
    /// version 12 territorial NPC home, last-known-position and perception
    /// timing state, and version 13 the rest of what a fight and a day are made
    /// of: per-creature behaviour profiles and withdrawal timing, projectiles in
    /// flight, casts in progress, spells a mishap has locked away until dawn,
    /// and NPC schedule state.
    /// Version 1 was never written outside this repository's tests, so it has
    /// no migration and is simply refused.
    /// </summary>
    public const int FormatVersion = 13;

    /// <summary>
    /// The oldest format this build still reads. Version 2 migrates forward
    /// through <see cref="MigrateStackLimit"/> rather than being refused,
    /// because version 2 files exist.
    /// </summary>
    public const int MinimumSupportedVersion = 2;

    /// <summary>
    /// The oldest reader that can still make sense of a file this build writes.
    /// A reader below this refuses the file instead of guessing at it.
    /// </summary>
    public const int MinimumReaderVersion = 13;

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

        // The dice are world sequence state: a loaded world must roll what it
        // would have rolled, not start a fresh sequence.
        WriteUInt64(buffer, snapshot.DiceState);
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

        if (formatVersion < MinimumSupportedVersion ||
            formatVersion > FormatVersion)
        {
            // Compatible older versions migrate through explicit, tested
            // transforms; anything else is refused rather than guessed at.
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
        var diceState = formatVersion >= 5 ? reader.ReadUInt64() : 0UL;
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

        var (maps, objects, conditions, combat, progress, ai) =
            ReadPayload(payload, formatVersion, tick);
        return new ObjectWorldSnapshot(
            fingerprint,
            tick,
            currentMapId,
            diceState,
            objects,
            maps,
            conditions,
            combat,
            progress,
            ai);
    }

    /// <summary>
    /// Captures the whole object world: every map in id order and every object
    /// slot. Derived caches are deliberately absent.
    /// </summary>
    public static ObjectWorldSnapshot Capture(
        ObjectStore objects,
        string contentFingerprint,
        long simulationTick,
        ushort currentMapId,
        ulong diceState = 0,
        IReadOnlyList<ActorConditionSnapshot>? conditions = null,
        CombatDirectorSnapshot? combat = null,
        WorldProgressSnapshot? progress = null,
        WorldAiSnapshot? ai = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFingerprint);
        return new ObjectWorldSnapshot(
            contentFingerprint,
            simulationTick,
            currentMapId,
            diceState,
            objects.Capture(),
            objects.Maps.All.Select(map => map.Capture()).ToArray(),
            conditions ?? [],
            combat ?? CombatDirectorSnapshot.Empty(simulationTick),
            progress,
            ai ?? WorldAiSnapshot.Empty);
    }

    /// <summary>
    /// Rebuilds a complete world beside the live one: the store first, then its
    /// maps, which index and validate what the store now holds. The caller
    /// decides whether to adopt it — loading never mutates a running world, and
    /// never re-settles the physics: a world resumes exactly where it was saved.
    /// </summary>
    public static LoadedObjectWorld Restore(ObjectWorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var objects = ObjectStore.Restore(snapshot.Objects);
        var maps = new List<WorldMap>(snapshot.Maps.Count);
        try
        {
            foreach (var map in snapshot.Maps)
            {
                maps.Add(WorldMap.Restore(objects, map));
            }

            foreach (var map in maps)
            {
                map.ValidateIndex();
            }

            new PhysicsSystem(objects).ValidateInvariants();
        }
        catch
        {
            foreach (var map in maps)
            {
                map.Dispose();
            }

            throw;
        }

        return new LoadedObjectWorld(
            objects,
            maps,
            snapshot.SimulationTick,
            snapshot.CurrentMapId,
            snapshot.DiceState,
            snapshot.Conditions ?? [],
            snapshot.Combat ?? CombatDirectorSnapshot.Empty(snapshot.SimulationTick),
            snapshot.Progress,
            snapshot.Ai ?? WorldAiSnapshot.Empty);
    }

    public static LoadedObjectWorld RestoreFile(
        string path,
        string expectedFingerprint) =>
        Restore(ReadFile(path, expectedFingerprint));

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

        // Maps first: a reader can build the world in the order it is written.
        var maps = snapshot.Maps.OrderBy(map => map.MapId).ToArray();
        if (maps.Select(map => map.MapId).Distinct().Count() != maps.Length)
        {
            throw new ObjectWorldSaveException(
                "Two maps in the snapshot share one map id.");
        }

        WriteInt32(buffer, maps.Length);
        foreach (var map in maps)
        {
            WriteUInt16(buffer, map.MapId);
            WriteInt32(buffer, map.Width);
            WriteInt32(buffer, map.Depth);
            WriteInt32(buffer, map.BucketSize);
            if (map.Terrain.Count != checked(map.Width * map.Depth))
            {
                throw new ObjectWorldSaveException(
                    $"Map {map.MapId} has {map.Terrain.Count} terrain cells " +
                    $"for a {map.Width}x{map.Depth} grid.");
            }

            foreach (var cell in map.Terrain)
            {
                WriteInt32(buffer, cell.FloorZ);
                buffer.WriteByte((byte)cell.Flags);
            }
        }

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

        var conditions = (snapshot.Conditions ?? [])
            .OrderBy(condition => condition.ActorId)
            .ThenBy(condition => condition.AppliedTick)
            .ThenBy(condition => condition.Kind)
            .ToArray();
        WriteInt32(buffer, conditions.Length);
        foreach (var condition in conditions)
        {
            WriteUInt32(buffer, condition.ActorId.Value);
            WriteInt32(buffer, (int)condition.Kind);
            WriteUInt32(buffer, condition.SourceId.Value);
            WriteInt32(buffer, condition.Magnitude);
            WriteInt64(buffer, condition.AppliedTick);
            WriteNullableInt64(buffer, condition.ExpiresAtTick);
            WriteNullableInt64(buffer, condition.NextPeriodicTick);
            WriteInt32(buffer, (int)condition.RemovalPolicy);
            WriteInt32(buffer, (int)condition.StackingPolicy);
            WriteText(buffer, condition.Detail ?? string.Empty);
            WriteText(buffer, condition.PresentationKey);
        }

        WriteCombatDirector(
            buffer,
            snapshot.Combat ?? CombatDirectorSnapshot.Empty(snapshot.SimulationTick));

        WriteProgress(buffer, snapshot.Progress);
        WriteAi(buffer, snapshot.Ai ?? WorldAiSnapshot.Empty);

        return buffer.ToArray();
    }

    private static (IReadOnlyList<WorldMapSnapshot> Maps, ObjectStoreSnapshot Objects,
        IReadOnlyList<ActorConditionSnapshot> Conditions, CombatDirectorSnapshot Combat,
        WorldProgressSnapshot? Progress, WorldAiSnapshot Ai)
        ReadPayload(ReadOnlySpan<byte> payload, int formatVersion, long simulationTick)
    {
        var reader = new SpanReader(payload);
        var maps = ReadMaps(ref reader);
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
                    alive == 1
                        ? ReadObject(ref reader, index, generation, formatVersion)
                        : null));
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

        var conditions = new List<ActorConditionSnapshot>();
        if (formatVersion >= 7)
        {
            var conditionCount = reader.ReadInt32();
            if (conditionCount < 0 || conditionCount > 1_000_000)
            {
                throw new ObjectWorldSaveException(
                    $"The save declares an impossible condition count of {conditionCount}.");
            }

            for (var index = 0; index < conditionCount; index++)
            {
                var actorId = ObjectIdFromValue(reader.ReadUInt32());
                var kind = ReadEnum<TraumaEffectKind>(ref reader, index, nameof(TraumaEffectKind));
                var sourceId = ObjectIdFromValue(reader.ReadUInt32());
                var magnitude = reader.ReadInt32();
                var appliedTick = reader.ReadInt64();
                var expiresAtTick = ReadNullableInt64(ref reader);
                var nextPeriodicTick = ReadNullableInt64(ref reader);
                var removal = formatVersion >= 8
                    ? ReadEnum<ActorConditionRemovalPolicy>(
                        ref reader, index, nameof(ActorConditionRemovalPolicy))
                    : ActorConditionService.RemovalPolicyFor(new TraumaEffect(
                        kind,
                        DurationUnit: expiresAtTick.HasValue
                            ? TraumaDurationUnit.Rounds
                            : TraumaDurationUnit.None));
                var stacking = formatVersion >= 8
                    ? ReadEnum<ActorConditionStackingPolicy>(
                        ref reader, index, nameof(ActorConditionStackingPolicy))
                    : ActorConditionService.StackingPolicyFor(kind);
                conditions.Add(new ActorConditionSnapshot(
                    actorId,
                    kind,
                    sourceId,
                    magnitude,
                    appliedTick,
                    expiresAtTick,
                    nextPeriodicTick,
                    kind == TraumaEffectKind.StableAtZero
                        ? ActorConditionRemovalPolicy.RecoverAtExpiry
                        : removal,
                    stacking,
                    EmptyToNull(reader.ReadText()),
                    reader.ReadText()));
            }
        }

        var combat = formatVersion >= 9
            ? ReadCombatDirector(ref reader, formatVersion)
            : CombatDirectorSnapshot.Empty(simulationTick);

        var progress = formatVersion >= 10
            ? ReadProgress(ref reader, formatVersion)
            : null;

        var ai = formatVersion >= 13
            ? ReadAi(ref reader)
            : WorldAiSnapshot.Empty;

        if (!reader.AtEnd)
        {
            throw new ObjectWorldSaveException(
                "The payload has trailing bytes after the object store.");
        }

        return (maps, new ObjectStoreSnapshot(slots, free), conditions, combat, progress, ai);
    }

    /// <summary>
    /// Writes what a fight and a day are doing outside the object graph. Every
    /// list is written in a total order — object id, then id and string — so the
    /// same world always produces the same bytes.
    /// </summary>
    private static void WriteAi(Stream buffer, WorldAiSnapshot ai)
    {
        var flights = ai.Projectiles.OrderBy(value => value.ProjectileId).ToArray();
        WriteInt32(buffer, flights.Length);
        foreach (var flight in flights)
        {
            WriteUInt32(buffer, flight.ProjectileId.Value);
            WriteUInt32(buffer, flight.ShooterId.Value);
            WriteUInt32(buffer, flight.TargetId.Value);
            WriteText(buffer, flight.DefinitionId);
            WriteVec3i(buffer, flight.Origin);
            WriteVec3i(buffer, flight.AimPoint);
            WriteInt32(buffer, flight.TilesTravelled);
            WriteInt64(buffer, flight.LaunchedAtTick);
        }

        var casts = ai.Casts.OrderBy(value => value.CasterId).ToArray();
        WriteInt32(buffer, casts.Length);
        foreach (var cast in casts)
        {
            WriteUInt32(buffer, cast.CasterId.Value);
            WriteText(buffer, cast.SpellId);
            WriteUInt32(buffer, cast.TargetId.Value);
            WriteVec3i(buffer, cast.AimPoint);
            WriteInt64(buffer, cast.StartTick);
            WriteInt64(buffer, cast.ReleaseTick);
        }

        var lockouts = ai.Lockouts
            .OrderBy(value => value.CasterId)
            .ThenBy(value => value.SpellId, StringComparer.Ordinal)
            .ToArray();
        WriteInt32(buffer, lockouts.Length);
        foreach (var lockout in lockouts)
        {
            WriteUInt32(buffer, lockout.CasterId.Value);
            WriteText(buffer, lockout.SpellId);
            WriteInt64(buffer, lockout.ExpiresAtTick);
        }

        var schedules = ai.Schedules.OrderBy(value => value.ActorId).ToArray();
        WriteInt32(buffer, schedules.Length);
        foreach (var schedule in schedules)
        {
            WriteUInt32(buffer, schedule.ActorId.Value);
            WriteText(buffer, schedule.RoutineId);
            WriteVec3i(buffer, schedule.Anchor);
            WriteInt32(buffer, (int)schedule.Activity);
            WriteInt32(buffer, (int)schedule.Fallback);
            WriteVec3i(buffer, schedule.Destination);
            WriteInt64(buffer, schedule.StepReadyAtTick);
            WriteInt64(buffer, schedule.SuspendedUntilTick);
        }
    }

    private static WorldAiSnapshot ReadAi(ref SpanReader reader)
    {
        var flightCount = ReadBoundedCount(ref reader, "projectile", 1_000_000);
        var flights = new ProjectileFlight[flightCount];
        for (var index = 0; index < flights.Length; index++)
        {
            flights[index] = new ProjectileFlight(
                ObjectIdFromValue(reader.ReadUInt32()),
                ObjectIdFromValue(reader.ReadUInt32()),
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadText(),
                ReadVec3i(ref reader),
                ReadVec3i(ref reader),
                reader.ReadInt32(),
                reader.ReadInt64());
        }

        var castCount = ReadBoundedCount(ref reader, "spell cast", 1_000_000);
        var casts = new SpellCast[castCount];
        for (var index = 0; index < casts.Length; index++)
        {
            casts[index] = new SpellCast(
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadText(),
                ObjectIdFromValue(reader.ReadUInt32()),
                ReadVec3i(ref reader),
                reader.ReadInt64(),
                reader.ReadInt64());
        }

        var lockoutCount = ReadBoundedCount(ref reader, "spell lockout", 1_000_000);
        var lockouts = new SpellLockout[lockoutCount];
        for (var index = 0; index < lockouts.Length; index++)
        {
            lockouts[index] = new SpellLockout(
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadText(),
                reader.ReadInt64());
        }

        var scheduleCount = ReadBoundedCount(ref reader, "NPC schedule", 1_000_000);
        var schedules = new NpcScheduleSnapshot[scheduleCount];
        for (var index = 0; index < schedules.Length; index++)
        {
            schedules[index] = new NpcScheduleSnapshot(
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadText(),
                ReadVec3i(ref reader),
                ReadEnum<ScheduleActivity>(ref reader, index, nameof(ScheduleActivity)),
                ReadEnum<ScheduleFallback>(ref reader, index, nameof(ScheduleFallback)),
                ReadVec3i(ref reader),
                reader.ReadInt64(),
                reader.ReadInt64());
        }

        return new WorldAiSnapshot(flights, casts, lockouts, schedules);
    }

    private static void WriteVec3i(Stream buffer, Vec3i value)
    {
        WriteInt32(buffer, value.X);
        WriteInt32(buffer, value.Y);
        WriteInt32(buffer, value.Z);
    }

    private static Vec3i ReadVec3i(ref SpanReader reader) =>
        new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

    private static void WriteProgress(
        Stream buffer,
        WorldProgressSnapshot? progress)
    {
        buffer.WriteByte(progress is null ? (byte)0 : (byte)1);
        if (progress is null)
        {
            return;
        }

        WriteInt32(buffer, (int)progress.Method);
        WriteInt32(buffer, (int)progress.Ancestry);
        var creationScores = progress.CreationScores ?? throw new InvalidOperationException(
            "Save format 11 and later require the confirmed " +
            "character-creation scores.");
        foreach (var score in creationScores.InOrder)
        {
            WriteInt32(buffer, score);
        }
        WriteInt32(buffer, progress.CreationAttempts);
        WriteInt32(buffer, progress.TalentRolls);
        WriteTalent(buffer, progress.FirstTalent);
        WriteTalent(buffer, progress.SecondTalent);
        WriteTalent(buffer, progress.AdvancementTalent);
        WriteInt32(buffer, progress.Experience);
        buffer.WriteByte(progress.DungeonCompleted ? (byte)1 : (byte)0);
    }

    private static WorldProgressSnapshot? ReadProgress(
        ref SpanReader reader,
        int formatVersion)
    {
        var present = reader.ReadByte();
        if (present == 0)
        {
            return null;
        }
        if (present != 1)
        {
            throw new ObjectWorldSaveException("World progress has an invalid presence flag.");
        }

        var method = ReadEnum<CharacterCreationMethod>(
            ref reader, 0, nameof(CharacterCreationMethod));
        var ancestry = ReadEnum<Ancestry>(ref reader, 0, nameof(Ancestry));
        AbilityScores? creationScores = null;
        if (formatVersion >= 11)
        {
            var scores = new int[AbilityScores.AbilityCount];
            for (var index = 0; index < scores.Length; index++)
            {
                scores[index] = reader.ReadInt32();
            }
            creationScores = AbilityScores.FromOrder(scores);
        }
        var attempts = reader.ReadInt32();
        var talentRolls = reader.ReadInt32();
        var first = ReadTalent(ref reader, formatVersion);
        var second = ReadTalent(ref reader, formatVersion);
        var advancement = ReadTalent(ref reader, formatVersion);
        var experience = reader.ReadInt32();
        var completed = reader.ReadByte();
        if (attempts < 1 || talentRolls is < 0 or > 2 || experience < 0 ||
            completed > 1)
        {
            throw new ObjectWorldSaveException("World progress contains invalid values.");
        }
        if ((first is null ? 0 : 1) + (second is null ? 0 : 1) != talentRolls)
        {
            throw new ObjectWorldSaveException(
                "World progress talent count does not match its rolled talents.");
        }

        return new WorldProgressSnapshot(
            method,
            ancestry,
            creationScores,
            attempts,
            talentRolls,
            first,
            second,
            advancement,
            experience,
            completed == 1);
    }

    private static void WriteTalent(Stream buffer, ClassTalentRoll? talent)
    {
        buffer.WriteByte(talent is null ? (byte)0 : (byte)1);
        if (talent is null)
        {
            return;
        }
        WriteInt32(buffer, talent.Roll);
        WriteText(buffer, talent.Id);
        WriteText(buffer, talent.Name);
        WriteText(buffer, talent.Description);
        WriteTalentChoice(buffer, talent.Choice);
    }

    private static ClassTalentRoll? ReadTalent(
        ref SpanReader reader,
        int formatVersion)
    {
        var present = reader.ReadByte();
        if (present == 0)
        {
            return null;
        }
        if (present != 1)
        {
            throw new ObjectWorldSaveException("A talent has an invalid presence flag.");
        }
        var roll = reader.ReadInt32();
        if (roll is < 2 or > 12)
        {
            throw new ObjectWorldSaveException($"A saved talent has invalid roll {roll}.");
        }
        var talent = new ClassTalentRoll(
            roll,
            reader.ReadText(),
            reader.ReadText(),
            reader.ReadText());
        return formatVersion >= 11
            ? talent with { Choice = ReadTalentChoice(ref reader) }
            : talent;
    }

    private static void WriteTalentChoice(
        Stream buffer,
        ClassTalentChoice? choice)
    {
        buffer.WriteByte(choice is null ? (byte)0 : (byte)1);
        if (choice is null)
        {
            return;
        }

        WriteText(buffer, choice.Id);
        WriteInt32(buffer, (int)choice.Kind);
        WriteText(buffer, choice.Description);
        WriteInt32(buffer, choice.FirstAbility is { } first ? (int)first : -1);
        WriteInt32(buffer, choice.FirstIncrease);
        WriteInt32(buffer, choice.SecondAbility is { } second ? (int)second : -1);
        WriteInt32(buffer, choice.SecondIncrease);
        WriteInt32(buffer, choice.WeaponFamily is { } family ? (int)family : -1);
        WriteInt32(buffer, choice.RogueSkill is { } skill ? (int)skill : -1);
    }

    private static ClassTalentChoice? ReadTalentChoice(ref SpanReader reader)
    {
        var present = reader.ReadByte();
        if (present == 0)
        {
            return null;
        }
        if (present != 1)
        {
            throw new ObjectWorldSaveException(
                "A talent choice has an invalid presence flag.");
        }

        var id = reader.ReadText();
        var kindValue = reader.ReadInt32();
        var description = reader.ReadText();
        var firstValue = reader.ReadInt32();
        var firstIncrease = reader.ReadInt32();
        var secondValue = reader.ReadInt32();
        var secondIncrease = reader.ReadInt32();
        var familyValue = reader.ReadInt32();
        var skillValue = reader.ReadInt32();
        if (string.IsNullOrWhiteSpace(id) ||
            !Enum.IsDefined((TalentChoiceKind)kindValue) ||
            firstValue < -1 || firstValue >= AbilityScores.AbilityCount ||
            secondValue < -1 || secondValue >= AbilityScores.AbilityCount ||
            firstIncrease is < 0 or > 2 || secondIncrease is < 0 or > 1 ||
            (familyValue != -1 && !Enum.IsDefined((WeaponFamily)familyValue)) ||
            (skillValue != -1 && !Enum.IsDefined((RogueSkill)skillValue)))
        {
            throw new ObjectWorldSaveException(
                "A saved talent choice contains invalid values.");
        }

        return new ClassTalentChoice(
            id,
            (TalentChoiceKind)kindValue,
            description,
            firstValue == -1 ? null : (Ability)firstValue,
            firstIncrease,
            secondValue == -1 ? null : (Ability)secondValue,
            secondIncrease,
            familyValue == -1 ? null : (WeaponFamily)familyValue,
            skillValue == -1 ? null : (RogueSkill)skillValue);
    }

    private static void WriteCombatDirector(
        Stream buffer,
        CombatDirectorSnapshot combat)
    {
        WriteInt64(buffer, combat.PlayerReadyAtTick);
        WriteNullableInt64(buffer, combat.PlayerDeathSaveAtTick);

        var steps = combat.PlayerStepTicks.Order().ToArray();
        WriteInt32(buffer, steps.Length);
        foreach (var tick in steps)
        {
            WriteInt64(buffer, tick);
        }

        var npcs = combat.Npcs.OrderBy(value => value.ActorId).ToArray();
        WriteInt32(buffer, npcs.Length);
        foreach (var npc in npcs)
        {
            WriteUInt32(buffer, npc.ActorId.Value);
            WriteInt32(buffer, (int)npc.Awareness);
            WriteInt32(buffer, (int)npc.Activity);
            WriteInt32(buffer, (int)npc.Reaction);
            buffer.WriteByte(npc.Action);
            buffer.WriteByte(npc.Provoked ? (byte)1 : (byte)0);
            WriteInt64(buffer, npc.ActionReadyAtTick);
            WriteInt64(buffer, npc.SwingReadyAtTick);
            WriteInt64(buffer, npc.StepReadyAtTick);
            WriteInt32(buffer, npc.HomePosition.X);
            WriteInt32(buffer, npc.HomePosition.Y);
            WriteInt32(buffer, npc.HomePosition.Z);
            WriteInt32(buffer, npc.LastKnownPlayerPosition.X);
            WriteInt32(buffer, npc.LastKnownPlayerPosition.Y);
            WriteInt32(buffer, npc.LastKnownPlayerPosition.Z);
            WriteInt64(buffer, npc.LastPerceivedAtTick);
            WriteInt64(buffer, npc.WithdrawUntilTick);
        }

        var behaviors = combat.Behaviors.OrderBy(value => value.ActorId).ToArray();
        WriteInt32(buffer, behaviors.Length);
        foreach (var (actorId, profile) in behaviors)
        {
            WriteUInt32(buffer, actorId.Value);
            WriteInt32(buffer, (int)profile);
        }

        var attacks = combat.ActiveAttacks
            .OrderBy(value => value.AttackerId)
            .ToArray();
        WriteInt32(buffer, attacks.Length);
        foreach (var action in attacks)
        {
            WriteUInt32(buffer, action.AttackerId.Value);
            WriteUInt32(buffer, action.TargetId.Value);
            WriteUInt32(buffer, action.WeaponId.Value);
            WriteInt64(buffer, action.StartTick);
            WriteInt64(buffer, action.ImpactTick);
            WriteInt64(buffer, action.RecoveryTick);
            WriteInt32(buffer, (int)action.Phase);
            WriteInt32(buffer, (int)action.InterruptionPolicy);
            WriteText(buffer, action.Outcome ?? string.Empty);
        }
    }

    private static CombatDirectorSnapshot ReadCombatDirector(
        ref SpanReader reader,
        int formatVersion)
    {
        var playerReady = reader.ReadInt64();
        var deathSave = ReadNullableInt64(ref reader);
        var stepCount = ReadBoundedCount(ref reader, "player movement beat", 1_000_000);
        var steps = new long[stepCount];
        for (var index = 0; index < steps.Length; index++)
        {
            steps[index] = reader.ReadInt64();
        }

        var npcCount = ReadBoundedCount(ref reader, "NPC combat state", 1_000_000);
        var npcs = new NpcCombatSnapshot[npcCount];
        for (var index = 0; index < npcs.Length; index++)
        {
            var actorId = ObjectIdFromValue(reader.ReadUInt32());
            var awareness = ReadEnum<Awareness>(ref reader, index, nameof(Awareness));
            var activity = ReadEnum<MonsterActivity>(ref reader, index, nameof(MonsterActivity));
            var reaction = ReadEnum<MonsterReaction>(ref reader, index, nameof(MonsterReaction));
            var action = reader.ReadByte();
            if (!Enum.IsDefined((CombatDirector.NpcAction)action))
            {
                throw new ObjectWorldSaveException(
                    $"NPC combat state {index} has unknown action {action}.");
            }
            var provoked = reader.ReadByte() switch
            {
                0 => false,
                1 => true,
                var value => throw new ObjectWorldSaveException(
                    $"NPC combat state {index} has invalid provoked flag {value}."),
            };
            var actionReady = reader.ReadInt64();
            var swingReady = reader.ReadInt64();
            var stepReady = reader.ReadInt64();
            var home = default(Vec3i);
            var lastKnown = default(Vec3i);
            var lastPerceived = -1L;
            var withdrawUntil = 0L;
            if (formatVersion >= 12)
            {
                home = new Vec3i(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
                lastKnown = new Vec3i(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());
                lastPerceived = reader.ReadInt64();
            }

            if (formatVersion >= 13)
            {
                withdrawUntil = reader.ReadInt64();
            }
            npcs[index] = new NpcCombatSnapshot(
                actorId,
                awareness,
                activity,
                reaction,
                action,
                provoked,
                actionReady,
                swingReady,
                stepReady,
                home,
                lastKnown,
                lastPerceived,
                withdrawUntil);
        }

        var behaviors = Array.Empty<(ObjectId, BehaviorProfile)>();
        if (formatVersion >= 13)
        {
            var behaviorCount = ReadBoundedCount(ref reader, "behaviour override", 1_000_000);
            var overrides = new (ObjectId, BehaviorProfile)[behaviorCount];
            for (var index = 0; index < overrides.Length; index++)
            {
                overrides[index] = (
                    ObjectIdFromValue(reader.ReadUInt32()),
                    ReadEnum<BehaviorProfile>(ref reader, index, nameof(BehaviorProfile)));
            }

            behaviors = overrides;
        }

        var attackCount = ReadBoundedCount(ref reader, "active attack", 1_000_000);
        var attacks = new AttackAction[attackCount];
        for (var index = 0; index < attacks.Length; index++)
        {
            attacks[index] = new AttackAction(
                ObjectIdFromValue(reader.ReadUInt32()),
                ObjectIdFromValue(reader.ReadUInt32()),
                ObjectIdFromValue(reader.ReadUInt32()),
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt64(),
                ReadEnum<AttackActionPhase>(
                    ref reader, index, nameof(AttackActionPhase)),
                ReadEnum<AttackInterruptionPolicy>(
                    ref reader, index, nameof(AttackInterruptionPolicy)),
                EmptyToNull(reader.ReadText()));
        }

        return new CombatDirectorSnapshot(
            playerReady, deathSave, steps, npcs, attacks, behaviors);
    }

    private static int ReadBoundedCount(
        ref SpanReader reader,
        string name,
        int maximum)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > maximum)
        {
            throw new ObjectWorldSaveException(
                $"The save declares an impossible {name} count of {count}.");
        }
        return count;
    }

    private static IReadOnlyList<WorldMapSnapshot> ReadMaps(ref SpanReader reader)
    {
        const int MaximumCellsPerMap = 16 * 1024 * 1024;
        const byte KnownTerrainFlags =
            (byte)(TerrainFlags.Walkable |
                   TerrainFlags.Solid |
                   TerrainFlags.ProvidesSupport |
                   TerrainFlags.Hazard);

        var mapCount = reader.ReadInt32();
        if (mapCount < 0 || mapCount > ushort.MaxValue + 1)
        {
            throw new ObjectWorldSaveException(
                $"The save declares an impossible map count of {mapCount}.");
        }

        var maps = new List<WorldMapSnapshot>(mapCount);
        var seen = new HashSet<ushort>();
        for (var index = 0; index < mapCount; index++)
        {
            var mapId = reader.ReadUInt16();
            if (!seen.Add(mapId))
            {
                throw new ObjectWorldSaveException(
                    $"The save holds map {mapId} twice.");
            }

            var width = reader.ReadInt32();
            var depth = reader.ReadInt32();
            var bucketSize = reader.ReadInt32();
            if (width <= 0 || depth <= 0 || bucketSize <= 0 ||
                (long)width * depth > MaximumCellsPerMap)
            {
                throw new ObjectWorldSaveException(
                    $"Map {mapId} declares impossible extents " +
                    $"{width}x{depth} with bucket size {bucketSize}.");
            }

            var cells = new TerrainCell[width * depth];
            for (var cell = 0; cell < cells.Length; cell++)
            {
                var floorZ = reader.ReadInt32();
                var flags = reader.ReadByte();
                if ((flags & ~KnownTerrainFlags) != 0)
                {
                    throw new ObjectWorldSaveException(
                        $"Map {mapId} cell {cell} has unknown terrain flags " +
                        $"0x{flags:X2}.");
                }

                cells[cell] = new TerrainCell(floorZ, (TerrainFlags)flags);
            }

            maps.Add(
                new WorldMapSnapshot(mapId, width, depth, bucketSize, cells));
        }

        return maps;
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
        WriteInt32(buffer, value.MaxQuantity);
        WriteInt32(buffer, value.Condition);
        WriteInt32(buffer, value.Health);
        WriteInt32(buffer, value.MaxHealth);
        WriteInt32(buffer, value.Wounds);
        WriteInt32(buffer, value.MaxWounds);
        WriteInt32(buffer, value.DeathSaveSuccesses);
        WriteInt32(buffer, value.DeathSaveFailures);
        WriteInt32(buffer, (int)value.Impairments);
        WriteInt32(buffer, (int)value.Vitality);
        WriteInt32(buffer, value.SlotCost);
        WriteInt32(buffer, value.SlotCapacity);
        WriteInt32(buffer, value.QuantityPerSlot);
        WriteText(buffer, value.SlotGroup);
        WriteInt32(buffer, value.Strength);
        WriteInt32(buffer, value.Dexterity);
        WriteInt32(buffer, value.Constitution);
        WriteInt32(buffer, value.Intelligence);
        WriteInt32(buffer, value.Wisdom);
        WriteInt32(buffer, value.Charisma);
        WriteInt32(buffer, (int)value.Class);
        WriteInt32(buffer, value.Level);
        WriteInt32(buffer, (int)value.ArmorType);
        WriteInt32(buffer, value.DefenseBonus);
        WriteInt32(buffer, value.GearSlotBonus);
        buffer.WriteByte(value.IsContainerOpen ? (byte)1 : (byte)0);
    }

    private static WorldObject ReadObject(
        ref SpanReader reader,
        int index,
        byte generation,
        int formatVersion)
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
        var maxQuantity = formatVersion >= 3
            ? reader.ReadInt32()
            : MigrateStackLimit(quantity);
        if (formatVersion < 3 && quantity > 1)
        {
            // A version 2 object carrying more than one really was a stack; it
            // simply had no way to say so. Marking it stackable with a limit of
            // exactly what it carries is the only reading that keeps the store's
            // invariants and cannot invent capacity that never existed.
            flags |= ObjectFlags.Stackable;
        }

        var condition = reader.ReadInt32();
        var health = reader.ReadInt32();
        var maxHealth = reader.ReadInt32();

        // Version 5 and earlier had no wound layer, so running out of
        // concussion hits was simply death — which is exactly what those worlds
        // meant, and the only reading that keeps a corpse a corpse across the
        // upgrade. A body with no wound layer reads as one that never had one.
        var wounds = 0;
        var maxWounds = 0;
        var deathSaveSuccesses = 0;
        var deathSaveFailures = 0;
        var impairments = AbilityMask.None;
        var vitality = maxHealth > 0 && health == 0
            ? VitalityState.Dead
            : VitalityState.Standing;
        if (formatVersion >= 6)
        {
            wounds = reader.ReadInt32();
            maxWounds = reader.ReadInt32();
            deathSaveSuccesses = reader.ReadInt32();
            deathSaveFailures = reader.ReadInt32();
            impairments = (AbilityMask)reader.ReadInt32();
            if ((impairments & ~AbilityMask.All) != 0)
            {
                throw new ObjectWorldSaveException(
                    $"Slot {index} names an ability that does not exist in its " +
                    "impairments.");
            }

            vitality = ReadEnum<VitalityState>(
                ref reader,
                index,
                nameof(VitalityState));
        }

        // Version 3 wrote one capacity field here; version 4 writes the slot
        // cost, the capacity, and the strength and bonus behind it.
        var slotCost = GearSlots.StandardCost;
        var strength = 0;
        var gearSlotBonus = 0;

        // Version 4 and earlier had no actor sheet: an unrolled character reads
        // as one that never had scores, a class or armour, which is exactly
        // what those worlds meant.
        var dexterity = 0;
        var constitution = 0;
        var intelligence = 0;
        var wisdom = 0;
        var charisma = 0;
        var characterClass = CharacterClass.Fighter;
        var level = 0;
        var armorType = ArmorType.None;
        var defenseBonus = 0;
        if (formatVersion >= 4)
        {
            slotCost = reader.ReadInt32();
        }

        var slotCapacity = reader.ReadInt32();
        var quantityPerSlot = 0;
        var slotGroup = string.Empty;
        if (formatVersion >= 4)
        {
            quantityPerSlot = reader.ReadInt32();
            slotGroup = reader.ReadText();
            strength = reader.ReadInt32();
            if (formatVersion >= 5)
            {
                dexterity = reader.ReadInt32();
                constitution = reader.ReadInt32();
                intelligence = reader.ReadInt32();
                wisdom = reader.ReadInt32();
                charisma = reader.ReadInt32();
                characterClass = ReadEnum<CharacterClass>(
                    ref reader,
                    index,
                    nameof(CharacterClass));
                level = reader.ReadInt32();
                armorType = ReadEnum<ArmorType>(
                    ref reader,
                    index,
                    nameof(ArmorType));
                defenseBonus = reader.ReadInt32();
            }

            gearSlotBonus = reader.ReadInt32();
        }

        if (formatVersion < 4 && flags.HasFlag(ObjectFlags.Actor))
        {
            // Version 3 authored an actor's capacity directly. Slots derive it
            // from strength now, so the old capacity becomes the strength that
            // produces exactly the same capacity.
            strength = slotCapacity;
            slotCapacity = 0;
        }

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
            maxQuantity,
            condition,
            health,
            maxHealth,
            wounds,
            maxWounds,
            deathSaveSuccesses,
            deathSaveFailures,
            impairments,
            vitality,
            slotCost,
            slotCapacity,
            quantityPerSlot,
            slotGroup,
            strength,
            dexterity,
            constitution,
            intelligence,
            wisdom,
            charisma,
            characterClass,
            level,
            armorType,
            defenseBonus,
            gearSlotBonus,
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

    /// <summary>
    /// Version 2 had no stack limit: every object carried whatever quantity it
    /// had and could not grow, so its limit is exactly that quantity.
    /// </summary>
    private static int MigrateStackLimit(int quantity) => Math.Max(1, quantity);

    /// <summary>
    /// Reads an enum the save must already know: an unknown value is a corrupt
    /// or future file, not something to coerce into a default.
    /// </summary>
    private static TEnum ReadEnum<TEnum>(
        ref SpanReader reader,
        int index,
        string name)
        where TEnum : struct, Enum
    {
        var value = reader.ReadInt32();
        var boxed = Enum.ToObject(typeof(TEnum), value);
        if (!Enum.IsDefined(typeof(TEnum), boxed))
        {
            throw new ObjectWorldSaveException(
                $"Slot {index} holds {value}, which is not a known {name}.");
        }

        return (TEnum)boxed;
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

    private static void WriteNullableInt64(Stream buffer, long? value)
    {
        buffer.WriteByte(value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue)
        {
            WriteInt64(buffer, value.Value);
        }
    }

    private static long? ReadNullableInt64(ref SpanReader reader)
    {
        var present = reader.ReadByte();
        return present switch
        {
            0 => null,
            1 => reader.ReadInt64(),
            _ => throw new ObjectWorldSaveException("A nullable tick has an invalid presence flag."),
        };
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static void WriteUInt64(Stream buffer, ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
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

        public ulong ReadUInt64() =>
            BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(sizeof(ulong)));

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
