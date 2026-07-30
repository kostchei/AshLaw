using System.Security.Cryptography;
using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class ObjectWorldSaveTests
{
    private const string Fingerprint = "ash.test-content.v1";

    [Fact]
    public void AThousandObjectWorldRoundTripsWithEveryComponent()
    {
        var store = BuildWorld(out _, out _);
        var snapshot = Capture(store);

        var restored = ObjectStore.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(snapshot),
                Fingerprint).Objects);

        Assert.Equal(store.Count, restored.Count);
        Assert.Equal(
            store.Enumerate().ToArray(),
            restored.Enumerate().ToArray());
        restored.ValidateInvariants();
    }

    [Fact]
    public void SaveLoadSaveIsByteIdentical()
    {
        var store = BuildWorld(out _, out _);

        var first = ObjectWorldSave.Write(Capture(store));
        var reloaded = ObjectStore.Restore(
            ObjectWorldSave.Read(first, Fingerprint).Objects);
        var second = ObjectWorldSave.Write(Capture(reloaded));

        Assert.Equal(first, second);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(first)),
            Convert.ToHexString(SHA256.HashData(second)));
    }

    [Fact]
    public void DestroyedGenerationsAndSlotReuseOrderSurviveLoading()
    {
        var store = BuildWorld(out var destroyed, out _);
        var nextSlots = new List<int>();
        var expected = ObjectStore.Restore(Capture(store).Objects);
        for (var index = 0; index < 3; index++)
        {
            nextSlots.Add(expected.Create(Item($"probe-{index}")).Index);
        }

        var restored = ObjectStore.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(Capture(store)),
                Fingerprint).Objects);

        foreach (var stale in destroyed)
        {
            Assert.False(restored.TryGet(stale, out _));
            Assert.Throws<InvalidObjectIdException>(() => restored.Get(stale));
        }

        // Allocation after loading must reuse the same slots in the same order
        // the unsaved world would have used.
        for (var index = 0; index < nextSlots.Count; index++)
        {
            Assert.Equal(
                nextSlots[index],
                restored.Create(Item($"probe-{index}")).Index);
        }
    }

    [Fact]
    public void MidFallAndSupportedObjectsResumeExactly()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var table = store.Create(new ObjectSpawn
        {
            TypeId = "prop.table",
            Name = "Table",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            Flags = ObjectFlags.Solid | ObjectFlags.Visible,
        });
        store.Create(new ObjectSpawn
        {
            TypeId = "item.plate",
            Name = "Plate",
            ShapeId = "loot",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 300)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.AffectedByGravity,
        });
        var faller = store.Create(new ObjectSpawn
        {
            TypeId = "item.gem",
            Name = "Gem",
            ShapeId = "loot",
            Location = ObjectLocation.OnMap(0, new Vec3i(1024, 512, 3000)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.AffectedByGravity,
        });
        var physics = new PhysicsSystem(store);
        for (var tick = 0; tick < 14; tick++)
        {
            physics.Advance();
        }

        var plate = store.Enumerate().Single(value => value.Name == "Plate");
        Assert.Equal(MotionState.Resting, plate.Motion);
        Assert.Equal(table, plate.Support.ObjectId);
        Assert.Equal(MotionState.Falling, store.Get(faller).Motion);
        Assert.True(store.Get(faller).VerticalVelocity > 0);

        var restored = ObjectStore.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(Capture(store)),
                Fingerprint).Objects);

        var restoredPlate = restored.Enumerate()
            .Single(value => value.Name == "Plate");
        Assert.Equal(plate, restoredPlate);
        Assert.Equal(store.Get(faller), restored.Get(faller));

        // A loaded world resumes; it does not re-settle. Ticking the restored
        // world reproduces the same next step as the original.
        using var restoredMap = new WorldMap(restored, 0, width: 8, depth: 8);
        var restoredPhysics = new PhysicsSystem(restored);
        restoredPhysics.Advance();
        physics.Advance();
        Assert.Equal(store.Get(faller), restored.Get(faller));
        restoredPhysics.ValidateInvariants();
    }

    [Fact]
    public void DerivedCachesAreRebuiltAndNotSaved()
    {
        var store = BuildWorld(out _, out var mapId);
        using var map = new WorldMap(store, mapId, width: 40, depth: 40);
        var region = new WorldRectangle(0, 40 * 256, 0, 40 * 256);
        var before = map.QueryAll().Select(value => value.Id).ToArray();
        var beforeRegion = map.Query(region, ObjectFlags.Solid)
            .Select(value => value.Id)
            .ToArray();

        var bytes = ObjectWorldSave.Write(Capture(store));
        var restored = ObjectStore.Restore(
            ObjectWorldSave.Read(bytes, Fingerprint).Objects);
        using var restoredMap = new WorldMap(restored, mapId, width: 40, depth: 40);

        Assert.Equal(
            before,
            restoredMap.QueryAll().Select(value => value.Id).ToArray());
        Assert.Equal(
            beforeRegion,
            restoredMap.Query(region, ObjectFlags.Solid)
                .Select(value => value.Id)
                .ToArray());
        restoredMap.ValidateIndex();
    }

    [Fact]
    public void HeaderCarriesTheTickFingerprintAndCurrentMap()
    {
        var store = BuildWorld(out _, out var mapId);
        var snapshot = new ObjectWorldSnapshot(
            Fingerprint,
            SimulationTick: 4_294_967_400,
            CurrentMapId: mapId,
            store.Capture());

        var read = ObjectWorldSave.Read(
            ObjectWorldSave.Write(snapshot),
            Fingerprint);

        Assert.Equal(4_294_967_400, read.SimulationTick);
        Assert.Equal(mapId, read.CurrentMapId);
        Assert.Equal(Fingerprint, read.ContentFingerprint);
    }

    // Offsets into the header and payload: the magic, the format version, and
    // the last payload byte. -1 means "the final byte".
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void CorruptSavesFailWithAClearError(int offset)
    {
        var bytes = ObjectWorldSave.Write(Capture(BuildWorld(out _, out _)));
        var corrupt = bytes.ToArray();
        var index = offset < 0 ? corrupt.Length - 1 : offset;
        corrupt[index] ^= 0xFF;

        Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(corrupt, Fingerprint));
    }

    [Fact]
    public void TruncatedTrailingAndMisfingerprintedSavesAreRejected()
    {
        var bytes = ObjectWorldSave.Write(Capture(BuildWorld(out _, out _)));

        Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(bytes.AsSpan(0, bytes.Length - 8), Fingerprint));
        Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(
                bytes.Concat(new byte[] { 7 }).ToArray(),
                Fingerprint));
        var fingerprintError = Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(bytes, "ash.other-content.v2"));
        Assert.Contains(Fingerprint, fingerprintError.Message);
    }

    [Fact]
    public void ARestoredStoreRejectsAFreeListThatDisagreesWithItsDeadSlots()
    {
        var store = BuildWorld(out _, out _);
        var snapshot = store.Capture();

        var lying = new ObjectStoreSnapshot(
            snapshot.Slots,
            [.. snapshot.FreeSlots, snapshot.Slots.Count - 1]);

        Assert.Throws<ArgumentException>(() => ObjectStore.Restore(lying));
    }

    [Fact]
    public void WriteFileReplacesAtomicallyAndReadsBack()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            var first = Capture(BuildWorld(out _, out _));
            ObjectWorldSave.WriteFile(path, first);
            var second = first with { SimulationTick = first.SimulationTick + 1 };
            ObjectWorldSave.WriteFile(path, second);

            var read = ObjectWorldSave.ReadFile(path, Fingerprint);

            Assert.Equal(second.SimulationTick, read.SimulationTick);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Equal(
                ObjectWorldSave.Write(second),
                File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ObjectWorldSnapshot Capture(ObjectStore store) =>
        new(Fingerprint, SimulationTick: 12_345, CurrentMapId: 3, store.Capture());

    /// <summary>
    /// A world with a thousand objects across every location kind, plus dead
    /// slots in a deliberately non-sequential free order.
    /// </summary>
    private static ObjectStore BuildWorld(
        out IReadOnlyList<ObjectId> destroyed,
        out ushort mapId)
    {
        mapId = 3;
        var store = new ObjectStore();
        using var map = new WorldMap(store, mapId, width: 40, depth: 40);
        var containers = new List<ObjectId>();
        var actors = new List<ObjectId>();
        for (var index = 0; index < 40; index++)
        {
            containers.Add(store.Create(new ObjectSpawn
            {
                TypeId = $"container.chest-{index:D2}",
                Name = $"Chest {index}",
                ShapeId = "container.chest",
                Location = ObjectLocation.OnMap(
                    mapId,
                    new Vec3i(((index % 20) + 1) * 256, 256, 0)),
                Footprint = new ObjectFootprint(128, 128),
                Height = 40,
                Flags =
                    ObjectFlags.Container |
                    ObjectFlags.Solid |
                    ObjectFlags.Visible,
                ContainerCapacity = 12,
            }));
            actors.Add(store.Create(new ObjectSpawn
            {
                TypeId = $"actor.guard-{index:D2}",
                Name = $"Guard {index}",
                ShapeId = "actor.guard",
                Location = ObjectLocation.OnMap(
                    mapId,
                    new Vec3i(((index % 20) + 1) * 256, 1024, 8)),
                Footprint = new ObjectFootprint(128, 128),
                Height = 64,
                StepHeight = 8,
                Flags =
                    ObjectFlags.Actor |
                    ObjectFlags.Container |
                    ObjectFlags.Solid |
                    ObjectFlags.Visible,
                ContainerCapacity = 8,
                Health = 5,
                MaxHealth = 9,
                Quality = index,
                Condition = 90 - index,
            }));
        }

        var loose = new List<ObjectId>();
        for (var index = 0; index < 920; index++)
        {
            var location = (index % 4) switch
            {
                0 => ObjectLocation.OnMap(
                    mapId,
                    new Vec3i(
                        ((index % 30) + 1) * 256,
                        ((index % 20) + 5) * 256,
                        (index % 3) * 8)),
                1 => ObjectLocation.InContainer(
                    containers[index / 4 % containers.Count]),
                2 => ObjectLocation.InContainer(
                    actors[index / 4 % actors.Count]),
                _ => ObjectLocation.InTransfer((uint)index + 1),
            };
            loose.Add(store.Create(Item($"item-{index:D4}", location, index)));
        }

        for (var slot = 0; slot < 8; slot++)
        {
            store.Create(new ObjectSpawn
            {
                TypeId = $"item.blade-{slot}",
                Name = $"Blade {slot}",
                ShapeId = "loot.shortsword",
                Location = ObjectLocation.Equipped(
                    actors[slot],
                    (byte)EquipmentSlot.MainHand),
                Footprint = new ObjectFootprint(32, 32),
                Height = 8,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
                EquipmentSlots = EquipmentSlotMask.MainHand,
            });
        }

        // Destroy out of order so the free-slot stack is not simply ascending.
        var removed = new List<ObjectId>();
        foreach (var offset in new[] { 700, 12, 455, 99, 800 })
        {
            var id = loose[offset];
            if (store.Get(id).Location.Kind == LocationKind.InTransfer ||
                store.Get(id).Location.Kind == LocationKind.OnMap ||
                store.Get(id).Location.Kind == LocationKind.InContainer)
            {
                store.Destroy(id);
                removed.Add(id);
            }
        }

        destroyed = removed;
        return store;
    }

    private static ObjectSpawn Item(
        string name,
        ObjectLocation? location = null,
        int index = 0) =>
        new()
        {
            TypeId = $"item.{name}",
            Name = name,
            ShapeId = "loot.generic",
            Location = location ??
                ObjectLocation.InTransfer((uint)index + 1),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            StepHeight = index % 3,
            Flags = ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Visible,
            Quality = index % 7,
            Quantity = (index % 5) + 1,
            Condition = 100 - (index % 40),
        };
}
