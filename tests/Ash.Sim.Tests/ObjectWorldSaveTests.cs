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
        var reloaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(first, Fingerprint));
        var second = ObjectWorldSave.Write(Capture(reloaded.Objects));

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

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(
                    ObjectWorldSave.Capture(
                        store,
                        Fingerprint,
                        physics.Tick,
                        currentMapId: 0)),
                Fingerprint));
        var restored = loaded.Objects;

        var restoredPlate = restored.Enumerate()
            .Single(value => value.Name == "Plate");
        Assert.Equal(plate, restoredPlate);
        Assert.Equal(store.Get(faller), restored.Get(faller));
        Assert.Equal(physics.Tick, loaded.SimulationTick);

        // A loaded world resumes; it does not re-settle. Restoring runs no
        // physics at all, and the next tick matches the original's next tick.
        var restoredPhysics = new PhysicsSystem(
            restored,
            startTick: loaded.SimulationTick);
        Assert.Equal(physics.Tick, restoredPhysics.Tick);
        restoredPhysics.Advance();
        physics.Advance();
        Assert.Equal(store.Get(faller), restored.Get(faller));
        Assert.Equal(physics.Tick, restoredPhysics.Tick);
        restoredPhysics.ValidateInvariants();
    }

    [Fact]
    public void TerrainRoundTripsCellForCell()
    {
        var store = BuildWorld(out _, out var map);

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(Capture(store)),
                Fingerprint));
        var restored = loaded.Maps.Single();

        Assert.Equal(map.MapId, restored.MapId);
        Assert.Equal(map.Width, restored.Width);
        Assert.Equal(map.Depth, restored.Depth);
        Assert.Equal(map.BucketSize, restored.BucketSize);
        Assert.Equal(map.WorldBounds, restored.WorldBounds);
        for (var y = 0; y < map.Depth; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                Assert.Equal(map.GetTerrain(x, y), restored.GetTerrain(x, y));
            }
        }
    }

    [Fact]
    public void SeveralMapsSurviveAndKeepTheirOwnTerrain()
    {
        var store = new ObjectStore();
        using var ground = new WorldMap(store, 0, width: 4, depth: 4);
        using var cellar = new WorldMap(store, 9, width: 6, depth: 3);
        cellar.SetTerrain(
            5,
            2,
            new TerrainCell(-32, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));
        store.Create(new ObjectSpawn
        {
            TypeId = "prop.crate",
            Name = "Crate",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(9, new Vec3i(1536, 768, -32)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags = ObjectFlags.Solid | ObjectFlags.Visible,
        });

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(
                    ObjectWorldSave.Capture(store, Fingerprint, 0, 9)),
                Fingerprint));

        Assert.Equal([(ushort)0, (ushort)9], loaded.Maps.Select(map => map.MapId));
        Assert.Equal(
            new TerrainCell(-32, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport),
            loaded.Objects.Maps.Get(9).GetTerrain(5, 2));
        Assert.Equal(TerrainCell.Floor, loaded.Objects.Maps.Get(0).GetTerrain(3, 3));
        Assert.Single(loaded.Objects.Maps.Get(9).QueryAll());
        Assert.Empty(loaded.Objects.Maps.Get(0).QueryAll());
    }

    [Fact]
    public void CorruptTerrainIsRejected()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 2, depth: 2);
        var snapshot = ObjectWorldSave.Capture(store, Fingerprint, 0, 0);
        var bytes = ObjectWorldSave.Write(snapshot);

        // The terrain flags byte of the first cell sits after the header, the
        // map count, the map header and that cell's floor elevation.
        var header = ObjectWorldSave.Magic.Length +
            sizeof(int) + sizeof(int) +
            sizeof(int) + System.Text.Encoding.UTF8.GetByteCount(Fingerprint) +
            sizeof(long) + sizeof(ushort) + sizeof(int) + 32;
        var flagsOffset = header + sizeof(int) +
            sizeof(ushort) + sizeof(int) + sizeof(int) + sizeof(int) +
            sizeof(int);
        var corrupt = bytes.ToArray();
        corrupt[flagsOffset] = 0x80;

        var mismatched = Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(corrupt, Fingerprint));
        Assert.Contains("checksum", mismatched.Message);

        // Re-sign the corrupted payload so the terrain check, not the checksum,
        // is what rejects it.
        var resigned = ObjectWorldSave.Write(
            snapshot with
            {
                Maps =
                [
                    snapshot.Maps[0] with
                    {
                        Terrain =
                        [
                            new TerrainCell(0, (TerrainFlags)0x80),
                            .. snapshot.Maps[0].Terrain.Skip(1),
                        ],
                    },
                ],
            });

        var rejected = Assert.Throws<ObjectWorldSaveException>(
            () => ObjectWorldSave.Read(resigned, Fingerprint));
        Assert.Contains("terrain flags", rejected.Message);
    }

    [Fact]
    public void DerivedCachesAreRebuiltAndNotSaved()
    {
        var store = BuildWorld(out _, out var map);
        var region = new WorldRectangle(0, 40 * 256, 0, 40 * 256);
        var before = map.QueryAll().Select(value => value.Id).ToArray();
        var beforeRegion = map.Query(region, ObjectFlags.Solid)
            .Select(value => value.Id)
            .ToArray();

        var bytes = ObjectWorldSave.Write(Capture(store));
        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(bytes, Fingerprint));
        var restoredMap = loaded.Maps.Single();

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
        var store = BuildWorld(out _, out var map);
        var snapshot = ObjectWorldSave.Capture(
            store,
            Fingerprint,
            simulationTick: 4_294_967_400,
            currentMapId: map.MapId);

        var read = ObjectWorldSave.Read(
            ObjectWorldSave.Write(snapshot),
            Fingerprint);

        Assert.Equal(4_294_967_400, read.SimulationTick);
        Assert.Equal(map.MapId, read.CurrentMapId);
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
    public void ASaveNamingADeadParentIsRejected()
    {
        var store = BuildWorld(out var destroyed, out _);
        var snapshot = Capture(store);
        var orphan = snapshot.Objects.Slots
            .Select((slot, index) => (slot, index))
            .First(pair =>
                pair.slot.IsAlive &&
                pair.slot.Value!.Value.Location.Kind == LocationKind.InContainer);
        var slots = snapshot.Objects.Slots.ToArray();

        // Point the child at a container that was destroyed before the save.
        slots[orphan.index] = orphan.slot with
        {
            Value = orphan.slot.Value!.Value with
            {
                Location = ObjectLocation.InContainer(destroyed[0]),
            },
        };

        var corrupt = ObjectWorldSave.Write(
            snapshot with
            {
                Objects = new ObjectStoreSnapshot(
                    slots,
                    snapshot.Objects.FreeSlots),
            });

        Assert.ThrowsAny<InvalidOperationException>(
            () => ObjectWorldSave.Restore(
                ObjectWorldSave.Read(corrupt, Fingerprint)));
    }

    [Fact]
    public void ASaveWithAnImpossibleSupportIsRejected()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var crate = store.Create(new ObjectSpawn
        {
            TypeId = "prop.crate",
            Name = "Crate",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags =
                ObjectFlags.Solid |
                ObjectFlags.Movable |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
        });
        new PhysicsSystem(store).Advance();
        var snapshot = ObjectWorldSave.Capture(store, Fingerprint, 1, 0);
        var slots = snapshot.Objects.Slots.ToArray();
        var index = crate.Index;

        // Claim the crate rests on a surface that is nowhere near its base.
        slots[index] = slots[index] with
        {
            Value = slots[index].Value!.Value with
            {
                Support = SupportRef.Terrain(0, 1, 1, topZ: 96),
            },
        };

        var corrupt = ObjectWorldSave.Write(
            snapshot with
            {
                Objects = new ObjectStoreSnapshot(
                    slots,
                    snapshot.Objects.FreeSlots),
            });

        var rejected = Assert.ThrowsAny<InvalidOperationException>(
            () => ObjectWorldSave.Restore(
                ObjectWorldSave.Read(corrupt, Fingerprint)));
        Assert.Contains("support", rejected.Message);

        // The live world is untouched by the failed load.
        Assert.Equal(0, store.Get(crate).Location.Position.Z);
        Assert.Equal(SupportKind.Terrain, store.Get(crate).Support.Kind);
        map.ValidateIndex();
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
        ObjectWorldSave.Capture(
            store,
            Fingerprint,
            simulationTick: 12_345,
            currentMapId: 3);

    /// <summary>
    /// A world with a thousand objects across every location kind, plus dead
    /// slots in a deliberately non-sequential free order.
    /// </summary>
    private static ObjectStore BuildWorld(
        out IReadOnlyList<ObjectId> destroyed,
        out WorldMap map)
    {
        const ushort mapId = 3;
        var store = new ObjectStore();

        // The map stays alive: a capture has to see the terrain as well as the
        // objects, and the caller owns it for the rest of the test.
        map = new WorldMap(store, mapId, width: 40, depth: 40);

        // Distinctive cells well clear of where the objects below stand, so the
        // fixture stays a physically valid world.
        map.SetTerrain(
            37,
            37,
            new TerrainCell(24, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));
        map.SetTerrain(36, 36, new TerrainCell(0, TerrainFlags.Solid));
        map.SetTerrain(
            38,
            38,
            new TerrainCell(-64, TerrainFlags.Walkable | TerrainFlags.Hazard));
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
                    new Vec3i(
                        ((index % 20) + 1) * 256,
                        ((index / 20) + 1) * 256,
                        0)),
                Footprint = new ObjectFootprint(128, 128),
                Height = 40,
                Flags =
                    ObjectFlags.Container |
                    ObjectFlags.Solid |
                    ObjectFlags.Visible,
                SlotCapacity = 12,
            }));
            actors.Add(store.Create(new ObjectSpawn
            {
                TypeId = $"actor.guard-{index:D2}",
                Name = $"Guard {index}",
                ShapeId = "actor.guard",
                Location = ObjectLocation.OnMap(
                    mapId,
                    new Vec3i(
                        ((index % 20) + 1) * 256,
                        ((index / 20) + 4) * 256,
                        0)),
                Footprint = new ObjectFootprint(128, 128),
                Height = 64,
                StepHeight = 8,
                Flags =
                    ObjectFlags.Actor |
                    ObjectFlags.Container |
                    ObjectFlags.Solid |
                    ObjectFlags.Visible,
                Strength = 8,
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
                    (byte)EquipmentSlot.RightHand),
                Footprint = new ObjectFootprint(32, 32),
                Height = 8,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
                EquipmentSlots = EquipmentSlotMask.RightHand,
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
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Stackable |
                ObjectFlags.Visible,
            Quality = index % 7,
            Quantity = (index % 5) + 1,
            MaxQuantity = 20,
            Condition = 100 - (index % 40),
        };
}
