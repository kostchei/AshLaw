using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class PhysicsTests
{
    [Fact]
    public void AnUnsupportedObjectFallsAndLandsOnTerrain()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var crate = store.Create(Crate("crate", new Vec3i(512, 512, 64)));
        var physics = new PhysicsSystem(store);

        Assert.Equal(MotionState.Falling, store.Get(crate).Motion);
        var events = RunUntilRest(physics, store, crate);

        var landed = store.Get(crate);
        Assert.Equal(0, landed.Location.Position.Z);
        Assert.Equal(SupportKind.Terrain, landed.Support.Kind);
        Assert.Equal(0, landed.Support.TopZ);
        var landing = Assert.Single(
            events.Where(value => value.Kind == PhysicsEventKind.Landed));
        Assert.Equal(crate, landing.ObjectId);
        Assert.True(landing.LandingSpeed > 0);
        physics.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void LootLandsOnFurnitureAndKeepsItsTopElevation()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var table = store.Create(Crate(
            "table",
            new Vec3i(512, 512, 0),
            height: 40,
            gravity: false));
        var tonic = store.Create(Crate(
            "tonic",
            new Vec3i(512, 512, 200),
            height: 8,
            solid: false));
        var physics = new PhysicsSystem(store);

        RunUntilRest(physics, store, tonic);

        var resting = store.Get(tonic);
        Assert.Equal(40, resting.Location.Position.Z);
        Assert.Equal(SupportKind.Object, resting.Support.Kind);
        Assert.Equal(table, resting.Support.ObjectId);
        Assert.Equal([tonic], map.SupportedObjects(table));
        physics.ValidateInvariants();
    }

    [Fact]
    public void AFastFallStopsOnTheHighestCrossedSupport()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var shelf = store.Create(Crate(
            "shelf",
            new Vec3i(512, 512, 0),
            height: 16,
            gravity: false));
        var gem = store.Create(Crate(
            "gem",
            new Vec3i(512, 512, 4000),
            height: 8,
            solid: false));
        var physics = new PhysicsSystem(store, gravityPerTick: 32);

        RunUntilRest(physics, store, gem);

        Assert.Equal(16, store.Get(gem).Location.Position.Z);
        Assert.Equal(shelf, store.Get(gem).Support.ObjectId);
        physics.ValidateInvariants();
    }

    [Fact]
    public void RemovingASupportMakesEveryDependantFall()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var table = store.Create(Crate(
            "table",
            new Vec3i(512, 512, 0),
            height: 40,
            gravity: false));
        var plate = store.Create(Crate(
            "plate",
            new Vec3i(512, 512, 40),
            height: 8,
            solid: false));
        var mug = store.Create(Crate(
            "mug",
            new Vec3i(512, 512, 48),
            height: 8,
            solid: false));
        var physics = new PhysicsSystem(store);
        RunUntilRest(physics, store, mug);

        Assert.Equal([plate], map.SupportedObjects(table));
        Assert.Equal([mug], map.SupportedObjects(plate));

        store.Destroy(table);
        var started = physics.Advance();

        Assert.Contains(
            started.Events,
            value =>
                value.Kind == PhysicsEventKind.FallStarted &&
                value.ObjectId == plate);
        RunUntilRest(physics, store, mug);
        Assert.Equal(0, store.Get(plate).Location.Position.Z);
        Assert.Equal(8, store.Get(mug).Location.Position.Z);
        Assert.Equal(plate, store.Get(mug).Support.ObjectId);
        physics.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void ContainingAFallingObjectCancelsTheFallSafely()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var chest = store.Create(new ObjectSpawn
        {
            TypeId = "container.chest",
            Name = "Chest",
            ShapeId = "container.chest",
            Location = ObjectLocation.OnMap(0, new Vec3i(256, 256, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            Flags = ObjectFlags.Container | ObjectFlags.Solid,
            ContainerCapacity = 4,
        });
        var gem = store.Create(Crate(
            "gem",
            new Vec3i(512, 512, 400),
            height: 8,
            solid: false));
        var physics = new PhysicsSystem(store);
        physics.Advance();
        Assert.Equal(MotionState.Falling, store.Get(gem).Motion);

        var transfers = new ObjectTransferService(store);
        var stored = transfers.Execute(
            new ObjectTransferRequest(
                gem,
                store.Get(gem).Location,
                ObjectLocation.InContainer(chest)));
        Assert.True(stored.Succeeded, stored.Message);

        physics.Advance();

        var contained = store.Get(gem);
        Assert.Equal(LocationKind.InContainer, contained.Location.Kind);
        Assert.Equal(MotionState.Resting, contained.Motion);
        Assert.True(contained.Support.IsNone);
        Assert.Equal(0, contained.VerticalVelocity);
        physics.ValidateInvariants();
    }

    [Fact]
    public void AMovingSupportCarriesEverythingRestingOnIt()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var cart = store.Create(Crate("cart", new Vec3i(512, 512, 0), height: 16));
        var barrel = store.Create(Crate("barrel", new Vec3i(512, 512, 16)));
        var physics = new PhysicsSystem(store);
        RunUntilRest(physics, store, barrel);
        Assert.Equal(cart, store.Get(barrel).Support.ObjectId);

        var carried = physics.MoveWithDependants(cart, new Vec3i(256, 0, 0));

        Assert.True(carried.Succeeded, carried.Message);
        Assert.Equal([barrel], carried.Carried);
        Assert.Equal(new Vec3i(768, 512, 0), store.Get(cart).Location.Position);
        Assert.Equal(
            new Vec3i(768, 512, 16),
            store.Get(barrel).Location.Position);
        Assert.Equal(cart, store.Get(barrel).Support.ObjectId);
        physics.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void CarryingIntoAnObstacleBlocksTheSupportAndNamesTheBlocker()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var cart = store.Create(Crate("cart", new Vec3i(512, 512, 0), height: 16));
        var crate = store.Create(Crate("crate", new Vec3i(512, 512, 16), height: 16));
        var overhang = store.Create(new ObjectSpawn
        {
            TypeId = "test.overhang",
            Name = "Overhang",
            ShapeId = "shape",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 24)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 16,
            Flags = ObjectFlags.Solid | ObjectFlags.Fixed | ObjectFlags.Visible,
        });
        var physics = new PhysicsSystem(store);
        RunUntilRest(physics, store, crate);

        var carried = physics.MoveWithDependants(cart, new Vec3i(256, 0, 0));

        Assert.False(carried.Succeeded);
        Assert.Equal(CarryFailure.Blocked, carried.Failure);
        Assert.Equal(overhang, carried.Blocker.ObjectId);
        Assert.Equal(new Vec3i(512, 512, 0), store.Get(cart).Location.Position);
        Assert.Equal(new Vec3i(512, 512, 16), store.Get(crate).Location.Position);
        physics.ValidateInvariants();
    }

    [Fact]
    public void ActorsClimbStairsRejectTooHighStepsAndFallOffLedges()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 4);
        for (var x = 1; x <= 3; x++)
        {
            map.SetTerrain(
                x,
                0,
                new TerrainCell(
                    x * 8,
                    TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));
        }

        map.SetTerrain(
            5,
            0,
            new TerrainCell(
                80,
                TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));
        var actor = store.Create(new ObjectSpawn
        {
            TypeId = "actor.avatar",
            Name = "Avatar",
            ShapeId = "avatar",
            Location = ObjectLocation.OnMap(0, new Vec3i(256, 256, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            StepHeight = 8,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Solid |
                ObjectFlags.AffectedByGravity,
        });
        var solver = new MovementSolver(store);
        var transfers = new ObjectTransferService(store);
        var physics = new PhysicsSystem(store);
        physics.Advance();

        for (var step = 1; step <= 3; step++)
        {
            var climb = solver.Resolve(actor, new Vec3i(256, 0, 0));
            Assert.True(climb.ReachedTarget, climb.Message);
            Assert.Equal(step * 8, climb.ResolvedPosition.Z);
            Commit(transfers, store, actor, climb);
        }

        var tooHigh = solver.Resolve(actor, new Vec3i(512, 0, 0));
        Assert.Equal(MovementFailure.StepTooHigh, tooHigh.Failure);
        Assert.False(tooHigh.Moved);

        var ledge = solver.Resolve(actor, new Vec3i(256, 0, 0));
        Assert.True(ledge.ReachedTarget, ledge.Message);
        Assert.Equal(MotionState.Falling, ledge.Motion);
        Assert.Equal(24, ledge.ResolvedPosition.Z);
        Commit(transfers, store, actor, ledge);

        RunUntilRest(physics, store, actor);
        Assert.Equal(0, store.Get(actor).Location.Position.Z);
        Assert.Equal(SupportKind.Terrain, store.Get(actor).Support.Kind);
        physics.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void RepeatedTickSequencesProduceIdenticalStateAndEvents()
    {
        var first = RunFallingScene();
        var second = RunFallingScene();

        Assert.Equal(first.States, second.States);
        Assert.Equal(first.Events, second.Events);
        Assert.Contains(
            first.Events,
            value => value.Kind == PhysicsEventKind.Landed);
    }

    private static (
        IReadOnlyList<(ObjectId Id, Vec3i Position, MotionState Motion,
            SupportRef Support)> States,
        IReadOnlyList<PhysicsEvent> Events) RunFallingScene()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        map.SetTerrain(
            2,
            2,
            new TerrainCell(
                16,
                TerrainFlags.Walkable | TerrainFlags.ProvidesSupport));
        for (var index = 0; index < 24; index++)
        {
            store.Create(Crate(
                $"crate-{index:D2}",
                new Vec3i(
                    ((index % 6) + 1) * 256,
                    ((index / 6) + 1) * 256,
                    100 + (index * 13)),
                height: 16));
        }

        var physics = new PhysicsSystem(store);
        var events = new List<PhysicsEvent>();
        for (var tick = 0; tick < 40; tick++)
        {
            events.AddRange(physics.Advance().Events);
        }

        physics.ValidateInvariants();
        map.ValidateIndex();
        var states = store.Enumerate()
            .Select(value => (
                value.Id,
                value.Location.Position,
                value.Motion,
                value.Support))
            .ToArray();
        return (states, events);
    }

    private static void Commit(
        ObjectTransferService transfers,
        ObjectStore store,
        ObjectId id,
        MovementResolution resolution)
    {
        var result = transfers.Execute(
            [
                new ObjectTransferRequest(
                    id,
                    store.Get(id).Location,
                    ObjectLocation.OnMap(0, resolution.ResolvedPosition)),
            ],
            [resolution.PhysicsFor(id)]);
        Assert.True(result.Succeeded, result.Message);
    }

    private static IReadOnlyList<PhysicsEvent> RunUntilRest(
        PhysicsSystem physics,
        ObjectStore store,
        ObjectId id)
    {
        var events = new List<PhysicsEvent>();
        for (var tick = 0; tick < 200; tick++)
        {
            events.AddRange(physics.Advance().Events);
            if (store.Get(id).Motion == MotionState.Resting)
            {
                return events;
            }
        }

        throw new InvalidOperationException(
            $"Object {id} never came to rest.");
    }

    private static ObjectSpawn Crate(
        string name,
        Vec3i position,
        int height = 32,
        bool gravity = true,
        bool solid = true) =>
        new()
        {
            TypeId = $"test.{name}",
            Name = name,
            ShapeId = "shape",
            Location = ObjectLocation.OnMap(0, position),
            Footprint = new ObjectFootprint(128, 128),
            Height = height,
            Flags =
                ObjectFlags.Visible |
                ObjectFlags.Movable |
                (solid ? ObjectFlags.Solid : ObjectFlags.ProvidesSupport) |
                (gravity ? ObjectFlags.AffectedByGravity : ObjectFlags.None),
        };
}
