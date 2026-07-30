using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class CollisionTests
{
    [Fact]
    public void SweepStopsAtTheContactFaceAndNamesTheBlockingObject()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Spawn(
            "actor",
            new Vec3i(512, 256, 0),
            ObjectFlags.Actor | ObjectFlags.Solid));
        var crate = store.Create(Spawn(
            "crate",
            new Vec3i(1024, 256, 0),
            ObjectFlags.Movable | ObjectFlags.Solid));
        var solver = new MovementSolver(store);

        var sweep = solver.Resolve(actor, new Vec3i(512, 0, 0));

        Assert.False(sweep.ReachedTarget);
        Assert.True(sweep.Moved);
        Assert.Equal(new Vec3i(896, 256, 0), sweep.ResolvedPosition);
        Assert.Equal(MovementFailure.Blocked, sweep.Failure);
        Assert.Equal(PlacementBlockerKind.Object, sweep.Blocker.Kind);
        Assert.Equal(crate, sweep.Blocker.ObjectId);
        Assert.Equal(
            new Vec3i(512, 256, 0),
            store.Get(actor).Location.Position);
    }

    [Fact]
    public void SolidTerrainBlocksActorsAndMovablePropsAlike()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        map.SetTerrain(2, 0, new TerrainCell(FloorZ: 0, TerrainFlags.Solid));
        map.SetTerrain(2, 1, new TerrainCell(FloorZ: 0, TerrainFlags.Solid));
        var actor = store.Create(Spawn(
            "actor",
            new Vec3i(256, 256, 0),
            ObjectFlags.Actor | ObjectFlags.Solid));
        var crate = store.Create(Spawn(
            "crate",
            new Vec3i(256, 512, 0),
            ObjectFlags.Movable | ObjectFlags.Solid));
        var solver = new MovementSolver(store);

        var actorSweep = solver.Resolve(actor, new Vec3i(512, 0, 0));
        var crateSweep = solver.Resolve(crate, new Vec3i(512, 0, 0));

        Assert.False(actorSweep.ReachedTarget);
        Assert.Equal(new Vec3i(512, 256, 0), actorSweep.ResolvedPosition);
        Assert.Equal(PlacementBlockerKind.Terrain, actorSweep.Blocker.Kind);
        Assert.Equal(2, actorSweep.Blocker.TerrainX);
        Assert.Equal(0, actorSweep.Blocker.TerrainY);
        Assert.False(crateSweep.ReachedTarget);
        Assert.Equal(PlacementBlockerKind.Terrain, crateSweep.Blocker.Kind);
    }

    [Fact]
    public void MapEdgesStopASweepWithoutMutatingTheWorld()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var actor = store.Create(Spawn(
            "actor",
            new Vec3i(1024, 256, 0),
            ObjectFlags.Actor | ObjectFlags.Solid));
        var solver = new MovementSolver(store);
        var revision = map.Revision;

        var sweep = solver.Resolve(actor, new Vec3i(256, 0, 0));

        Assert.False(sweep.ReachedTarget);
        Assert.False(sweep.Moved);
        Assert.Equal(PlacementBlockerKind.MapEdge, sweep.Blocker.Kind);
        Assert.Equal(new Vec3i(1024, 256, 0), sweep.ResolvedPosition);
        Assert.Equal(revision, map.Revision);
    }

    [Fact]
    public void NonSolidObjectsShareSpaceButSolidOnesNeverOverlap()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Spawn(
            "actor",
            new Vec3i(512, 512, 0),
            ObjectFlags.Actor | ObjectFlags.Solid));
        var item = store.Create(Spawn(
            "apple",
            new Vec3i(1024, 512, 0),
            ObjectFlags.Item | ObjectFlags.Movable));
        var transfers = new ObjectTransferService(store);

        var dropped = transfers.Execute(
            new ObjectTransferRequest(
                item,
                store.Get(item).Location,
                store.Get(actor).Location));
        Assert.True(dropped.Succeeded, dropped.Message);

        var solid = store.Create(Spawn(
            "crate",
            new Vec3i(1024, 512, 0),
            ObjectFlags.Movable | ObjectFlags.Solid));
        var blocked = transfers.Execute(
            new ObjectTransferRequest(
                solid,
                store.Get(solid).Location,
                store.Get(actor).Location));

        Assert.False(blocked.Succeeded);
        Assert.Equal(ObjectTransferFailure.ObjectBlocked, blocked.Failure);
        Assert.Equal(actor, blocked.Blocker.ObjectId);
        Assert.Equal(
            new Vec3i(1024, 512, 0),
            store.Get(solid).Location.Position);
        Assert.All(
            map.QueryAll(ObjectFlags.Solid),
            first => Assert.All(
                map.QueryAll(ObjectFlags.Solid),
                second => Assert.True(
                    first.Id == second.Id ||
                    !WorldMap.VolumeFor(first)
                        .Overlaps(WorldMap.VolumeFor(second)))));
    }

    [Fact]
    public void TransformingAnObjectChangesCollisionAtTheSameCommit()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Spawn(
            "actor",
            new Vec3i(512, 256, 0),
            ObjectFlags.Actor | ObjectFlags.Solid));
        var door = store.Create(Spawn(
            "door",
            new Vec3i(768, 256, 0),
            ObjectFlags.Solid | ObjectFlags.Usable));
        var solver = new MovementSolver(store);

        Assert.False(solver.Resolve(actor, new Vec3i(256, 0, 0)).ReachedTarget);

        store.Transform(
            door,
            "door.open",
            "Open Door",
            "door.open",
            ObjectFlags.Usable | ObjectFlags.Visible);

        var afterOpening = solver.Resolve(actor, new Vec3i(256, 0, 0));
        Assert.True(afterOpening.ReachedTarget);
        Assert.Equal(new Vec3i(768, 256, 0), afterOpening.ResolvedPosition);
    }

    [Fact]
    public void FixedObjectsAreNeitherSweptNorTransferredToANewPosition()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var pillar = store.Create(Spawn(
            "pillar",
            new Vec3i(512, 512, 0),
            ObjectFlags.Solid | ObjectFlags.Fixed));
        var solver = new MovementSolver(store);
        var transfers = new ObjectTransferService(store);

        var sweep = solver.Resolve(pillar, new Vec3i(256, 0, 0));
        var transfer = transfers.Execute(
            new ObjectTransferRequest(
                pillar,
                store.Get(pillar).Location,
                ObjectLocation.OnMap(0, new Vec3i(768, 512, 0))));

        Assert.Equal(MovementFailure.Immovable, sweep.Failure);
        Assert.False(sweep.Moved);
        Assert.False(transfer.Succeeded);
        Assert.Equal(ObjectTransferFailure.Immovable, transfer.Failure);
        Assert.Equal(
            new Vec3i(512, 512, 0),
            store.Get(pillar).Location.Position);
    }

    [Fact]
    public void RejectedPlacementLeavesStoreAndIndexRevisionUntouched()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        map.SetTerrain(3, 3, new TerrainCell(FloorZ: 0, TerrainFlags.Solid));
        var crate = store.Create(Spawn(
            "crate",
            new Vec3i(256, 256, 0),
            ObjectFlags.Movable | ObjectFlags.Solid));
        var transfers = new ObjectTransferService(store);
        var source = store.Get(crate).Location;
        var revision = map.Revision;

        var intoWall = transfers.Execute(
            new ObjectTransferRequest(
                crate,
                source,
                ObjectLocation.OnMap(0, new Vec3i(1024, 1024, 0))));
        var offMap = transfers.Execute(
            new ObjectTransferRequest(
                crate,
                source,
                ObjectLocation.OnMap(0, new Vec3i(1152, 256, 0))));
        var unknownMap = transfers.Execute(
            new ObjectTransferRequest(
                crate,
                source,
                ObjectLocation.OnMap(9, new Vec3i(256, 256, 0))));

        Assert.Equal(ObjectTransferFailure.TerrainBlocked, intoWall.Failure);
        Assert.Equal(3, intoWall.Blocker.TerrainX);
        Assert.Equal(3, intoWall.Blocker.TerrainY);
        Assert.Equal(ObjectTransferFailure.OutOfMapBounds, offMap.Failure);
        Assert.Equal(ObjectTransferFailure.UnknownMap, unknownMap.Failure);
        Assert.Equal(source, store.Get(crate).Location);
        Assert.Equal(revision, map.Revision);
        map.ValidateIndex();
        store.ValidateInvariants();
    }

    [Fact]
    public void OneTransactionCannotStackTwoSolidObjectsOnTheSameSpace()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var left = store.Create(Spawn(
            "left",
            new Vec3i(256, 256, 0),
            ObjectFlags.Movable | ObjectFlags.Solid));
        var right = store.Create(Spawn(
            "right",
            new Vec3i(512, 256, 0),
            ObjectFlags.Movable | ObjectFlags.Solid));
        var transfers = new ObjectTransferService(store);
        var target = ObjectLocation.OnMap(0, new Vec3i(768, 256, 0));

        var collision = transfers.Execute(
            new ObjectTransferRequest(left, store.Get(left).Location, target),
            new ObjectTransferRequest(right, store.Get(right).Location, target));
        Assert.False(collision.Succeeded);
        Assert.Equal(ObjectTransferFailure.ObjectBlocked, collision.Failure);

        var swap = transfers.Execute(
            new ObjectTransferRequest(
                left,
                store.Get(left).Location,
                ObjectLocation.OnMap(0, new Vec3i(768, 256, 0))),
            new ObjectTransferRequest(
                right,
                store.Get(right).Location,
                ObjectLocation.OnMap(0, new Vec3i(256, 256, 0))));

        Assert.True(swap.Succeeded, swap.Message);
        Assert.Equal(
            new Vec3i(768, 256, 0),
            store.Get(left).Location.Position);
        Assert.Equal(
            new Vec3i(256, 256, 0),
            store.Get(right).Location.Position);
        map.ValidateIndex();
    }

    [Fact]
    public void RepeatedSweepSequencesProduceIdenticalPositionsAndBlockers()
    {
        var first = RunSweepSequence();
        var second = RunSweepSequence();

        Assert.Equal(first, second);
        Assert.Contains(
            first,
            step => step.Blocker == PlacementBlockerKind.Object);
        Assert.Contains(
            first,
            step => step.Blocker == PlacementBlockerKind.Terrain);
    }

    private static IReadOnlyList<(Vec3i Position, PlacementBlockerKind Blocker)>
        RunSweepSequence()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 12, depth: 12);
        map.SetTerrain(0, 0, new TerrainCell(FloorZ: 0, TerrainFlags.Solid));
        var actor = store.Create(Spawn(
            "actor",
            new Vec3i(256, 512, 0),
            ObjectFlags.Actor | ObjectFlags.Solid));
        for (var index = 0; index < 20; index++)
        {
            store.Create(Spawn(
                $"crate-{index:D2}",
                new Vec3i(
                    ((index % 5) + 3) * 256,
                    ((index / 5) + 1) * 256,
                    0),
                ObjectFlags.Movable | ObjectFlags.Solid));
        }

        var solver = new MovementSolver(store);
        var transfers = new ObjectTransferService(store);
        var steps = new List<(Vec3i, PlacementBlockerKind)>();
        var displacements = new[]
        {
            new Vec3i(0, -256, 0),
            new Vec3i(256, 0, 0),
            new Vec3i(1024, 0, 0),
            new Vec3i(0, 256, 0),
            new Vec3i(-256, 0, 0),
            new Vec3i(768, 0, 0),
        };

        foreach (var displacement in displacements)
        {
            var sweep = solver.Resolve(actor, displacement);
            if (sweep.Moved)
            {
                var move = transfers.Execute(
                    new ObjectTransferRequest(
                        actor,
                        store.Get(actor).Location,
                        ObjectLocation.OnMap(0, sweep.ResolvedPosition)));
                Assert.True(move.Succeeded, move.Message);
            }

            steps.Add((
                store.Get(actor).Location.Position,
                sweep.Blocker.Kind));
        }

        map.ValidateIndex();
        return steps;
    }

    private static ObjectSpawn Spawn(
        string name,
        Vec3i position,
        ObjectFlags flags) =>
        new()
        {
            TypeId = $"test.{name}",
            Name = name,
            ShapeId = "shape",
            Location = ObjectLocation.OnMap(0, position),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags = flags | ObjectFlags.Visible,
        };
}
