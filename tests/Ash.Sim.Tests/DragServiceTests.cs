using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class DragServiceTests
{
    [Fact]
    public void AGroundItemMovesToTheHandAndLeavesEveryWorldQuery()
    {
        var world = Build(out var map, out var actor, out var apple, out _);
        var drag = new DragService(world);

        var began = drag.Begin(actor, apple);

        Assert.True(began.Succeeded, began.Message);
        Assert.True(drag.IsDragging);
        Assert.Equal(apple, drag.State.ObjectId);
        Assert.Equal(
            LocationKind.InTransfer,
            world.Get(apple).Location.Kind);
        Assert.DoesNotContain(map.QueryAll(), value => value.Id == apple);
        Assert.Empty(map.QueryAnchor(new Vec3i(768, 512, 0)));
        map.ValidateIndex();
    }

    [Fact]
    public void DroppingOnTheMapUsesTheSupportUnderThePointNotTheCallersZ()
    {
        var world = Build(out var map, out var actor, out var apple, out var table);
        var drag = new DragService(world);
        Assert.True(drag.Begin(actor, apple).Succeeded);

        var dropped = drag.DropOnMap(0, x: 1024, y: 512);

        Assert.True(dropped.Succeeded, dropped.Message);
        Assert.False(drag.IsDragging);
        var value = world.Get(apple);
        Assert.Equal(new Vec3i(1024, 512, 40), value.Location.Position);
        Assert.Equal(SupportKind.Object, value.Support.Kind);
        Assert.Equal(table, value.Support.ObjectId);
        Assert.Equal(MotionState.Resting, value.Motion);
        Assert.Equal([apple], map.SupportedObjects(table));
        new PhysicsSystem(world).ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void DropsIntoContainersAndEquipmentUseTheSameTransaction()
    {
        var world = Build(out _, out var actor, out var apple, out _);
        var sword = world.Create(new ObjectSpawn
        {
            TypeId = "item.sword",
            Name = "Sword",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 768, 0)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
            EquipmentSlots = EquipmentSlotMask.RightHand,
        });
        var drag = new DragService(world);

        Assert.True(drag.Begin(actor, apple).Succeeded);
        var intoPack = drag.DropInContainer(actor);
        Assert.True(intoPack.Succeeded, intoPack.Message);
        Assert.Equal(
            ObjectLocation.InContainer(actor),
            world.Get(apple).Location);

        Assert.True(drag.Begin(actor, sword).Succeeded);
        var equipped = drag.DropOnEquipment(actor, EquipmentSlot.RightHand);

        Assert.True(equipped.Succeeded, equipped.Message);
        Assert.Equal(
            ObjectLocation.Equipped(actor, EquipmentSlot.RightHand),
            world.Get(sword).Location);
        world.ValidateInvariants();
    }

    [Fact]
    public void AnItemInTheHoldersPackIsAlwaysInReachButTheWorldIsNot()
    {
        var world = Build(out _, out var actor, out _, out _);
        var carried = world.Create(new ObjectSpawn
        {
            TypeId = "item.ration",
            Name = "Ration",
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(actor),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });
        var distant = world.Create(new ObjectSpawn
        {
            TypeId = "item.far-gem",
            Name = "Far Gem",
            ShapeId = "loot.generic",
            Location = ObjectLocation.OnMap(0, new Vec3i(3072, 512, 0)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });
        var drag = new DragService(world);

        var tooFar = drag.Begin(actor, distant);
        Assert.False(tooFar.Succeeded);
        Assert.Equal(DragFailure.OutOfReach, tooFar.Failure);
        Assert.False(drag.IsDragging);
        Assert.Equal(
            LocationKind.OnMap,
            world.Get(distant).Location.Kind);

        Assert.True(drag.Begin(actor, carried).Succeeded);
        var farDrop = drag.DropOnMap(0, x: 3072, y: 512);
        Assert.False(farDrop.Succeeded);
        Assert.Equal(DragFailure.OutOfReach, farDrop.Failure);

        // A refused drop keeps the gesture alive rather than losing the object.
        Assert.True(drag.IsDragging);
        Assert.Equal(
            LocationKind.InTransfer,
            world.Get(carried).Location.Kind);
        Assert.True(drag.Cancel().Succeeded);
        Assert.Equal(
            ObjectLocation.InContainer(actor),
            world.Get(carried).Location);
    }

    [Fact]
    public void FixedAndImmovableObjectsCannotBePickedUp()
    {
        var world = Build(out _, out var actor, out _, out var table);
        var pillar = world.Create(new ObjectSpawn
        {
            TypeId = "prop.pillar",
            Name = "Pillar",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 768, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 96,
            Flags = ObjectFlags.Solid | ObjectFlags.Fixed | ObjectFlags.Visible,
        });
        var drag = new DragService(world);

        var fixedResult = drag.Begin(actor, pillar);
        var tableResult = drag.Begin(actor, table);
        var self = drag.Begin(actor, actor);

        Assert.Equal(DragFailure.Immovable, fixedResult.Failure);
        Assert.Equal(DragFailure.Immovable, tableResult.Failure);
        Assert.Equal(DragFailure.Immovable, self.Failure);
        Assert.False(drag.IsDragging);
    }

    [Fact]
    public void ADropIntoABlockedSpaceIsRefusedAndTheDragSurvives()
    {
        var world = Build(out var map, out var actor, out _, out _);
        var crate = world.Create(new ObjectSpawn
        {
            TypeId = "prop.crate",
            Name = "Crate",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 1024, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags =
                ObjectFlags.Solid | ObjectFlags.Movable | ObjectFlags.Visible,
        });
        world.Create(new ObjectSpawn
        {
            TypeId = "prop.anvil",
            Name = "Anvil",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags = ObjectFlags.Solid | ObjectFlags.Visible,
        });

        // A shelf above the anvil, out of the holder's reach so it cannot be
        // the surface, but low enough that a crate on the anvil hits it.
        world.Create(new ObjectSpawn
        {
            TypeId = "prop.shelf",
            Name = "Shelf",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 56)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 16,
            Flags = ObjectFlags.Solid | ObjectFlags.Fixed | ObjectFlags.Visible,
        });
        var drag = new DragService(world);
        Assert.True(drag.Begin(actor, crate).Succeeded);

        var blocked = drag.DropOnMap(0, x: 768, y: 512);

        Assert.False(blocked.Succeeded);
        Assert.Equal(DragFailure.Rejected, blocked.Failure);
        Assert.Equal(PlacementBlockerKind.Object, blocked.Blocker.Kind);
        Assert.Equal("Shelf", world.Get(blocked.Blocker.ObjectId).Name);
        Assert.True(drag.IsDragging);

        var elsewhere = drag.DropOnMap(0, x: 512, y: 1024);
        Assert.True(elsewhere.Succeeded, elsewhere.Message);
        Assert.Equal(
            new Vec3i(512, 1024, 0),
            world.Get(crate).Location.Position);
        map.ValidateIndex();
    }

    [Fact]
    public void CancellingReturnsTheObjectToItsExactSourceAndSupport()
    {
        var world = Build(out var map, out var actor, out var apple, out _);
        var drag = new DragService(world);
        var source = world.Get(apple);
        Assert.True(drag.Begin(actor, apple).Succeeded);

        var cancelled = drag.Cancel();

        Assert.True(cancelled.Succeeded, cancelled.Message);
        Assert.False(drag.IsDragging);
        Assert.Equal(source, world.Get(apple));
        Assert.Contains(map.QueryAll(), value => value.Id == apple);
        new PhysicsSystem(world).ValidateInvariants();
    }

    [Fact]
    public void ACancelWithNowhereToReturnToKeepsTheObjectInHand()
    {
        var world = Build(out var map, out var actor, out var apple, out _);
        var drag = new DragService(world);
        Assert.True(drag.Begin(actor, apple).Succeeded);

        // The floor the apple came from collapses into solid rock while it is
        // in hand, so there is nowhere to put it back.
        map.SetTerrain(2, 1, new TerrainCell(FloorZ: 0, TerrainFlags.Solid));

        var cancelled = drag.Cancel();

        Assert.False(cancelled.Succeeded);
        Assert.Equal(DragFailure.SourceLost, cancelled.Failure);
        Assert.True(drag.IsDragging);
        Assert.Equal(
            LocationKind.InTransfer,
            world.Get(apple).Location.Kind);

        var elsewhere = drag.DropInContainer(actor);
        Assert.True(elsewhere.Succeeded, elsewhere.Message);
        Assert.False(drag.IsDragging);
    }

    [Fact]
    public void OneGestureAtATimeAndNoGestureMeansNoDrop()
    {
        var world = Build(out _, out var actor, out var apple, out _);
        var second = world.Create(new ObjectSpawn
        {
            TypeId = "item.pear",
            Name = "Pear",
            ShapeId = "loot.generic",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 768, 0)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });
        var drag = new DragService(world);

        Assert.Equal(DragFailure.NotDragging, drag.Cancel().Failure);
        Assert.Equal(
            DragFailure.NotDragging,
            drag.DropOnMap(0, 512, 512).Failure);

        Assert.True(drag.Begin(actor, apple).Succeeded);
        var again = drag.Begin(actor, second);

        Assert.Equal(DragFailure.AlreadyDragging, again.Failure);
        Assert.Equal(apple, drag.State.ObjectId);
        Assert.Equal(LocationKind.OnMap, world.Get(second).Location.Kind);
    }

    [Fact]
    public void ADragInFlightDefersASaveUntilTheGestureEnds()
    {
        var world = Build(out _, out var actor, out var apple, out _);
        var physics = new PhysicsSystem(world);
        var gate = new WorldSaveGate(world, physics, "ash.test-content.v1");
        var drag = new DragService(world);
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-drag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            Assert.True(drag.Begin(actor, apple).Succeeded);

            var deferred = gate.Request(path, currentMapId: 0);

            Assert.Equal(SaveDeferralReason.TransferInFlight, deferred.Reason);
            Assert.False(File.Exists(path));

            Assert.True(drag.DropInContainer(actor).Succeeded);
            physics.Advance();

            Assert.True(gate.Flush(currentMapId: 0)?.Saved);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ObjectStore Build(
        out WorldMap map,
        out ObjectId actor,
        out ObjectId apple,
        out ObjectId table)
    {
        var store = new ObjectStore();
        map = new WorldMap(store, 0, width: 16, depth: 16);
        actor = store.Create(new ObjectSpawn
        {
            TypeId = "actor.avatar",
            Name = "Avatar",
            ShapeId = "avatar.knight",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            StepHeight = 8,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = 8,
            Health = 10,
            MaxHealth = 10,
        });
        table = store.Create(new ObjectSpawn
        {
            TypeId = "prop.table",
            Name = "Table",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, new Vec3i(1024, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            Flags = ObjectFlags.Solid | ObjectFlags.Visible,
        });
        apple = store.Create(new ObjectSpawn
        {
            TypeId = "item.apple",
            Name = "Apple",
            ShapeId = "loot.generic",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Visible,
        });
        return store;
    }
}
