using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class ObjectTransferServiceTests
{
    [Fact]
    public void IdentityRoundTripCrossesEveryPlayableLocation()
    {
        var store = new ObjectStore();
        var transfers = new ObjectTransferService(store);
        var actor = store.Create(Actor(capacity: 3));
        var chest = store.Create(Container("chest", capacity: 3));
        var start = ObjectLocation.OnMap(2, new Vec3i(512, 768, 4));
        var sword = store.Create(Item(
            "sword",
            start,
            EquipmentSlotMask.MainHand,
            quality: 7,
            quantity: 2));

        Transfer(sword, ObjectLocation.InContainer(actor));
        Transfer(sword, ObjectLocation.InContainer(chest));
        Transfer(
            sword,
            ObjectLocation.Equipped(actor, EquipmentSlot.MainHand));
        Transfer(sword, start);

        var result = store.Get(sword);
        Assert.Equal(sword, result.Id);
        Assert.Equal(start, result.Location);
        Assert.Equal(7, result.Quality);
        Assert.Equal(2, result.Quantity);
        store.ValidateInvariants();

        void Transfer(ObjectId id, ObjectLocation destination)
        {
            var source = store.Get(id).Location;
            var transaction = transfers.Execute(
                new ObjectTransferRequest(id, source, destination));
            Assert.True(transaction.Succeeded, transaction.Message);
        }
    }

    [Fact]
    public void FullContainersCanSwapContentsInOneTransaction()
    {
        var store = new ObjectStore();
        var transfers = new ObjectTransferService(store);
        var left = store.Create(Container("left", capacity: 1));
        var right = store.Create(Container("right", capacity: 1));
        var apple = store.Create(
            Item("apple", ObjectLocation.InContainer(left)));
        var gem = store.Create(
            Item("gem", ObjectLocation.InContainer(right)));

        var result = transfers.Execute(
            new ObjectTransferRequest(
                apple,
                store.Get(apple).Location,
                ObjectLocation.InContainer(right)),
            new ObjectTransferRequest(
                gem,
                store.Get(gem).Location,
                ObjectLocation.InContainer(left)));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(gem, store.GetContents(left).Single());
        Assert.Equal(apple, store.GetContents(right).Single());
    }

    [Fact]
    public void OccupiedEquipmentSlotsCanSwapInOneTransaction()
    {
        var store = new ObjectStore();
        var transfers = new ObjectTransferService(store);
        var actor = store.Create(Actor(capacity: 2));
        var accepted =
            EquipmentSlotMask.MainHand | EquipmentSlotMask.OffHand;
        var sword = store.Create(
            Item(
                "sword",
                ObjectLocation.Equipped(actor, EquipmentSlot.MainHand),
                accepted));
        var shield = store.Create(
            Item(
                "shield",
                ObjectLocation.Equipped(actor, EquipmentSlot.OffHand),
                accepted));

        var result = transfers.Execute(
            new ObjectTransferRequest(
                sword,
                store.Get(sword).Location,
                ObjectLocation.Equipped(actor, EquipmentSlot.OffHand)),
            new ObjectTransferRequest(
                shield,
                store.Get(shield).Location,
                ObjectLocation.Equipped(actor, EquipmentSlot.MainHand)));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(
            (byte)EquipmentSlot.OffHand,
            store.Get(sword).Location.Slot);
        Assert.Equal(
            (byte)EquipmentSlot.MainHand,
            store.Get(shield).Location.Slot);
    }

    [Fact]
    public void RejectedBatchLeavesEverySourceUntouched()
    {
        var store = new ObjectStore();
        var transfers = new ObjectTransferService(store);
        var actor = store.Create(Actor(capacity: 2));
        var chest = store.Create(Container("chest", capacity: 1));
        var blocker = store.Create(
            Item("blocker", ObjectLocation.InContainer(chest)));
        var sword = store.Create(
            Item(
                "sword",
                ObjectLocation.InContainer(actor),
                EquipmentSlotMask.MainHand));
        var apple = store.Create(
            Item("apple", ObjectLocation.InContainer(actor)));
        var swordSource = store.Get(sword).Location;
        var appleSource = store.Get(apple).Location;

        var result = transfers.Execute(
            new ObjectTransferRequest(
                sword,
                swordSource,
                ObjectLocation.Equipped(actor, EquipmentSlot.MainHand)),
            new ObjectTransferRequest(
                apple,
                appleSource,
                ObjectLocation.InContainer(chest)));

        Assert.False(result.Succeeded);
        Assert.Equal(
            ObjectTransferFailure.ContainerCapacity,
            result.Failure);
        Assert.Equal(swordSource, store.Get(sword).Location);
        Assert.Equal(appleSource, store.Get(apple).Location);
        Assert.Equal(blocker, store.GetContents(chest).Single());
    }

    [Fact]
    public void EquipmentRestrictionsAndProjectedSlotConflictsRejectAtomically()
    {
        var store = new ObjectStore();
        var transfers = new ObjectTransferService(store);
        var actor = store.Create(Actor(capacity: 3));
        var sword = store.Create(
            Item(
                "sword",
                ObjectLocation.InContainer(actor),
                EquipmentSlotMask.MainHand));
        var apple = store.Create(
            Item("apple", ObjectLocation.InContainer(actor)));

        var restriction = transfers.Execute(
            new ObjectTransferRequest(
                apple,
                store.Get(apple).Location,
                ObjectLocation.Equipped(actor, EquipmentSlot.MainHand)));
        Assert.False(restriction.Succeeded);
        Assert.Equal(
            ObjectTransferFailure.EquipmentRestriction,
            restriction.Failure);

        var secondSword = store.Create(
            Item(
                "second-sword",
                ObjectLocation.OnMap(0, Vec3i.Zero),
                EquipmentSlotMask.MainHand));
        var conflict = transfers.Execute(
            new ObjectTransferRequest(
                sword,
                store.Get(sword).Location,
                ObjectLocation.Equipped(actor, EquipmentSlot.MainHand)),
            new ObjectTransferRequest(
                secondSword,
                store.Get(secondSword).Location,
                ObjectLocation.Equipped(actor, EquipmentSlot.MainHand)));

        Assert.False(conflict.Succeeded);
        Assert.Equal(
            ObjectTransferFailure.EquipmentSlotOccupied,
            conflict.Failure);
        Assert.Equal(
            LocationKind.InContainer,
            store.Get(sword).Location.Kind);
        Assert.Equal(
            LocationKind.OnMap,
            store.Get(secondSword).Location.Kind);
    }

    [Fact]
    public void TenThousandTransfersAndInjectedFailuresPreserveInvariants()
    {
        var store = new ObjectStore();
        var transfers = new ObjectTransferService(store);
        var containers = Enumerable.Range(0, 4)
            .Select(index => store.Create(
                Container($"bag-{index}", capacity: 32)))
            .ToArray();
        var items = Enumerable.Range(0, 24)
            .Select(index => store.Create(
                Item(
                    $"item-{index}",
                    ObjectLocation.InContainer(
                        containers[index % containers.Length]))))
            .ToArray();
        var random = new Random(8675309);

        for (var iteration = 0; iteration < 10_000; iteration++)
        {
            var item = items[random.Next(items.Length)];
            var source = store.Get(item).Location;
            var destination = random.Next(5) == 0
                ? ObjectLocation.OnMap(
                    0,
                    new Vec3i(
                        random.Next(32) * 256,
                        random.Next(32) * 256,
                        0))
                : ObjectLocation.InContainer(
                    containers[random.Next(containers.Length)]);
            var injectFailure = iteration % 7 == 0;
            var expected = injectFailure
                ? ObjectLocation.InTransfer((uint)iteration + 1)
                : source;

            var result = transfers.Execute(
                new ObjectTransferRequest(item, expected, destination));

            Assert.Equal(!injectFailure, result.Succeeded);
            if (injectFailure)
            {
                Assert.Equal(
                    ObjectTransferFailure.SourceChanged,
                    result.Failure);
                Assert.Equal(source, store.Get(item).Location);
            }

            if (iteration % 100 == 0)
            {
                store.ValidateInvariants();
            }
        }

        store.ValidateInvariants();
    }

    private static ObjectSpawn Actor(int capacity) =>
        new()
        {
            TypeId = "actor.test",
            Name = "Actor",
            ShapeId = "actor.test",
            Location = ObjectLocation.OnMap(0, Vec3i.Zero),
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Visible,
            ContainerCapacity = capacity,
        };

    private static ObjectSpawn Container(string name, int capacity) =>
        new()
        {
            TypeId = $"container.{name}",
            Name = name,
            ShapeId = "container.test",
            Location = ObjectLocation.OnMap(0, Vec3i.Zero),
            Flags = ObjectFlags.Container,
            ContainerCapacity = capacity,
        };

    private static ObjectSpawn Item(
        string name,
        ObjectLocation location,
        EquipmentSlotMask slots = EquipmentSlotMask.None,
        int quality = 0,
        int quantity = 1) =>
        new()
        {
            TypeId = $"item.{name}",
            Name = name,
            ShapeId = "item.test",
            Location = location,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
            EquipmentSlots = slots,
            Quality = quality,
            Quantity = quantity,
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
        };
}
