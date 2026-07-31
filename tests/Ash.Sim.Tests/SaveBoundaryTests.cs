using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class SaveBoundaryTests : IDisposable
{
    private const string Fingerprint = "ash.test-content.v1";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ash-boundary-{Guid.NewGuid():N}");

    public SaveBoundaryTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ASafeWorldSavesImmediately()
    {
        var world = BuildWorld(out var physics, out _);
        var gate = new WorldSaveGate(world, physics, new Ash.Rules.Dice(1), Fingerprint);
        var path = Path.Combine(_directory, "immediate.ashw");

        var attempt = gate.Request(path, currentMapId: 0);

        Assert.True(attempt.Saved);
        Assert.False(attempt.Deferred);
        Assert.False(gate.IsSavePending);
        Assert.Equal(SaveDeferralReason.None, attempt.Reason);
        Assert.True(File.Exists(path));
        Assert.Null(gate.Flush(currentMapId: 0));
    }

    [Fact]
    public void AnObjectInTransferDefersTheSaveToTheNextSafeTick()
    {
        var world = BuildWorld(out var physics, out var crate);
        var transfers = new ObjectTransferService(world);
        var gate = new WorldSaveGate(world, physics, new Ash.Rules.Dice(1), Fingerprint);
        var path = Path.Combine(_directory, "deferred.ashw");
        Assert.True(
            transfers.Execute(
                new ObjectTransferRequest(
                    crate,
                    world.Get(crate).Location,
                    ObjectLocation.InTransfer(77))).Succeeded);

        var deferred = gate.Request(path, currentMapId: 0);

        Assert.False(deferred.Saved);
        Assert.True(deferred.Deferred);
        Assert.Equal(SaveDeferralReason.TransferInFlight, deferred.Reason);
        Assert.True(gate.IsSavePending);
        Assert.False(File.Exists(path));

        // Ticking while the drag is still in flight keeps deferring.
        physics.Advance();
        var stillPending = gate.Flush(currentMapId: 0);
        Assert.Equal(SaveDeferralReason.TransferInFlight, stillPending?.Reason);
        Assert.True(gate.IsSavePending);
        Assert.False(File.Exists(path));

        // Completing the drag makes the next tick a safe point.
        Assert.True(
            transfers.Execute(
                new ObjectTransferRequest(
                    crate,
                    world.Get(crate).Location,
                    ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)))).Succeeded);
        physics.Advance();
        var written = gate.Flush(currentMapId: 0);

        Assert.True(written?.Saved);
        Assert.False(gate.IsSavePending);
        Assert.True(File.Exists(path));
        var reloaded = ObjectWorldSave.RestoreFile(path, Fingerprint);
        Assert.Equal(
            world.Get(crate).Location,
            reloaded.Objects.Get(crate).Location);
        Assert.All(
            reloaded.Objects.Enumerate(),
            value => Assert.NotEqual(
                LocationKind.InTransfer,
                value.Location.Kind));
        reloaded.Maps.Single().Dispose();
    }

    [Fact]
    public void ARequestFromInsideACommitIsDeferredNotTaken()
    {
        var world = BuildWorld(out var physics, out var crate);
        var transfers = new ObjectTransferService(world);
        var gate = new WorldSaveGate(world, physics, new Ash.Rules.Dice(1), Fingerprint);
        var path = Path.Combine(_directory, "mid-commit.ashw");
        SaveAttempt? fromInsideCommit = null;
        world.Committed += _ =>
            fromInsideCommit ??= gate.Request(path, currentMapId: 0);

        Assert.True(
            transfers.Execute(
                new ObjectTransferRequest(
                    crate,
                    world.Get(crate).Location,
                    ObjectLocation.OnMap(0, new Vec3i(1024, 512, 0)))).Succeeded);

        Assert.Equal(
            SaveDeferralReason.CommitInProgress,
            fromInsideCommit?.Reason);
        Assert.False(File.Exists(path));
        Assert.True(gate.IsSavePending);

        physics.Advance();
        Assert.True(gate.Flush(currentMapId: 0)?.Saved);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ARequestFromInsideATickWaitsForTheTickToFinish()
    {
        var world = BuildWorld(out var physics, out _);
        var gate = new WorldSaveGate(world, physics, new Ash.Rules.Dice(1), Fingerprint);
        var path = Path.Combine(_directory, "mid-tick.ashw");
        SaveAttempt? fromInsideTick = null;

        // Something still falling, so ticks keep committing.
        world.Create(Crate("faller", new Vec3i(2048, 512, 600)));

        // A physics commit's notification runs while the tick is advancing.
        world.Committed += _ =>
        {
            if (physics.IsAdvancing)
            {
                fromInsideTick ??= gate.Request(path, currentMapId: 0);
            }
        };

        for (var tick = 0; tick < 40 && fromInsideTick is null; tick++)
        {
            physics.Advance();
        }

        Assert.NotNull(fromInsideTick);
        Assert.True(fromInsideTick!.Value.Deferred);
        Assert.Contains(
            fromInsideTick.Value.Reason,
            new[]
            {
                SaveDeferralReason.TickInProgress,
                SaveDeferralReason.CommitInProgress,
            });
        Assert.False(File.Exists(path));

        physics.Advance();
        Assert.True(gate.Flush(currentMapId: 0)?.Saved);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RepeatedSaveLoadCyclesNeitherDuplicateNorOrphanObjects()
    {
        var world = BuildWorld(out var physics, out _);
        var gate = new WorldSaveGate(world, physics, new Ash.Rules.Dice(1), Fingerprint);
        var path = Path.Combine(_directory, "cycle.ashw");
        Assert.True(gate.Request(path, currentMapId: 0).Saved);

        var firstBytes = File.ReadAllBytes(path);
        var expected = world.Enumerate().ToArray();
        var expectedCount = world.Count;
        LoadedObjectWorld? current = null;

        for (var cycle = 0; cycle < 5; cycle++)
        {
            var loaded = ObjectWorldSave.RestoreFile(path, Fingerprint);
            current?.Maps.Single().Dispose();
            current = loaded;

            Assert.Equal(expectedCount, loaded.Objects.Count);
            Assert.Equal(expected, loaded.Objects.Enumerate().ToArray());
            Assert.Equal(
                expected.Length,
                loaded.Objects.Enumerate().Select(value => value.Id).Distinct().Count());

            // No orphans: every parent an object names is still live.
            foreach (var value in loaded.Objects.Enumerate())
            {
                if (value.Location.Kind is
                    LocationKind.InContainer or LocationKind.Equipped)
                {
                    Assert.True(
                        loaded.Objects.TryGet(value.Location.Parent, out _),
                        $"{value.Name} points at a dead parent.");
                }
            }

            var cycled = new WorldSaveGate(
                loaded.Objects,
                new PhysicsSystem(loaded.Objects, startTick: loaded.SimulationTick),
                Ash.Rules.Dice.FromState(loaded.DiceState),
                Fingerprint);
            Assert.True(cycled.Request(path, currentMapId: 0).Saved);
            Assert.Equal(firstBytes, File.ReadAllBytes(path));
        }

        current?.Maps.Single().Dispose();
    }

    [Fact]
    public void TheDemoDefersASaveAndTakesItOnTheNextTick()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var path = Path.Combine(_directory, "demo.ashw");
        var sword = world.BackpackItems.First();
        Assert.True(
            world.Transfers.Execute(
                new ObjectTransferRequest(
                    sword.Id,
                    sword.Location,
                    ObjectLocation.InTransfer(1))).Succeeded);

        var deferred = world.RequestSave(path);

        Assert.False(deferred.Succeeded);
        Assert.Contains("deferred", deferred.Message);
        Assert.Equal(deferred.Message, world.LastMessage);
        Assert.True(world.SaveGate.IsSavePending);

        Assert.True(
            world.Transfers.Execute(
                new ObjectTransferRequest(
                    sword.Id,
                    world.Objects.Get(sword.Id).Location,
                    ObjectLocation.InContainer(world.PlayerId))).Succeeded);
        world.AdvancePhysics();

        Assert.False(world.SaveGate.IsSavePending);
        Assert.True(File.Exists(path));
        Assert.Contains("Saved at tick", world.LastMessage);

        using var loaded = PlayableSliceWorld.Load(path);
        Assert.Equal(world.PlayerPosition, loaded.PlayerPosition);
        Assert.Equal(world.Physics.Tick, loaded.Physics.Tick);
    }

    [Fact]
    public void AnInvalidWorldIsNeverWrittenAsIfItWereValid()
    {
        var world = BuildWorld(out var physics, out var crate);
        var gate = new WorldSaveGate(world, physics, new Ash.Rules.Dice(1), Fingerprint);
        var path = Path.Combine(_directory, "invalid.ashw");

        // A second solid volume in the same space: spawning does not validate
        // placement, so this world is physically invalid.
        world.Create(Crate("intruder", world.Get(crate).Location.Position));

        Assert.Throws<InvalidOperationException>(
            () => gate.Request(path, currentMapId: 0));
        Assert.False(File.Exists(path));
    }

    private static ObjectStore BuildWorld(
        out PhysicsSystem physics,
        out ObjectId crate)
    {
        var store = new ObjectStore();
        _ = new WorldMap(store, 0, width: 8, depth: 8);
        crate = store.Create(Crate("crate", new Vec3i(512, 512, 0)));
        store.Create(new ObjectSpawn
        {
            TypeId = "container.chest",
            Name = "Chest",
            ShapeId = "container.chest",
            Location = ObjectLocation.OnMap(0, new Vec3i(1536, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            Flags = ObjectFlags.Container | ObjectFlags.Solid,
            SlotCapacity = 4,
        });
        var chest = store.Enumerate().Single(value => value.Name == "Chest").Id;
        store.Create(new ObjectSpawn
        {
            TypeId = "item.gem",
            Name = "Gem",
            ShapeId = "loot",
            Location = ObjectLocation.InContainer(chest),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });
        physics = new PhysicsSystem(store);
        physics.Advance();
        return store;
    }

    private static ObjectSpawn Crate(string name, Vec3i position) =>
        new()
        {
            TypeId = $"prop.{name}",
            Name = name,
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, position),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags =
                ObjectFlags.Solid |
                ObjectFlags.Movable |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
        };
}
