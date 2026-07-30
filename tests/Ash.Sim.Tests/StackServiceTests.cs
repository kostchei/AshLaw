using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class StackServiceTests
{
    [Fact]
    public void OnlyIdenticalGoodsStackTogether()
    {
        var store = Build(out _, out var pack, out _);
        var coins = store.Create(Coins("Gold Coins", pack, 10));
        var moreCoins = store.Create(Coins("Gold Coins", pack, 5));
        var worn = store.Create(Coins("Gold Coins", pack, 5) with
        {
            Condition = 40,
        });
        var otherType = store.Create(
            Coins("Silver Coins", pack, 5) with { TypeId = "item.silver" });
        var single = store.Create(new ObjectSpawn
        {
            TypeId = "item.sword",
            Name = "Sword",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.InContainer(pack),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });

        Assert.True(
            StackService.AreCompatible(store.Get(coins), store.Get(moreCoins)));
        Assert.False(
            StackService.AreCompatible(store.Get(coins), store.Get(worn)));
        Assert.False(
            StackService.AreCompatible(store.Get(coins), store.Get(otherType)));
        Assert.False(
            StackService.AreCompatible(store.Get(coins), store.Get(single)));
        Assert.False(
            StackService.AreCompatible(store.Get(coins), store.Get(coins)));
    }

    [Fact]
    public void MergingEmptiesTheSourceAndDestroysItInOneCommit()
    {
        var store = Build(out _, out var pack, out _);
        var into = store.Create(Coins("Gold Coins", pack, 10));
        var from = store.Create(Coins("Gold Coins", pack, 15));
        var stacks = new StackService(store);
        var commits = 0;
        store.Committed += _ => commits++;

        var merged = stacks.Merge(from, into);

        Assert.True(merged.Succeeded, merged.Message);
        Assert.Equal(25, store.Get(into).Quantity);
        Assert.False(store.TryGet(from, out _));
        Assert.Equal(1, commits);
        Assert.Equal(25, merged.Moved + store.Get(into).Quantity - merged.Moved);
        store.ValidateInvariants();
    }

    [Fact]
    public void AMergeThatOverflowsLeavesTheRemainderBehind()
    {
        var store = Build(out _, out var pack, out _);
        var into = store.Create(Coins("Gold Coins", pack, 90));
        var from = store.Create(Coins("Gold Coins", pack, 40));
        var stacks = new StackService(store);

        var merged = stacks.Merge(from, into);

        Assert.True(merged.Succeeded, merged.Message);
        Assert.Equal(10, merged.Moved);
        Assert.Equal(30, merged.Remaining);
        Assert.Equal(100, store.Get(into).Quantity);
        Assert.Equal(30, store.Get(from).Quantity);
        Assert.Equal(130, store.Get(into).Quantity + store.Get(from).Quantity);
        store.ValidateInvariants();
    }

    [Fact]
    public void AFullDestinationRefusesTheMergeAndChangesNothing()
    {
        var store = Build(out _, out var pack, out _);
        var into = store.Create(Coins("Gold Coins", pack, 100));
        var from = store.Create(Coins("Gold Coins", pack, 7));
        var stacks = new StackService(store);

        var merged = stacks.Merge(from, into);

        Assert.False(merged.Succeeded);
        Assert.Equal(StackFailure.Rejected, merged.Failure);
        Assert.Equal(100, store.Get(into).Quantity);
        Assert.Equal(7, store.Get(from).Quantity);
    }

    [Fact]
    public void SplittingCreatesANewStackAndKeepsTheTotal()
    {
        var store = Build(out var map, out var pack, out var chest);
        var coins = store.Create(Coins("Gold Coins", pack, 30));
        var stacks = new StackService(store);

        var split = stacks.Split(
            coins,
            12,
            ObjectLocation.InContainer(chest));

        Assert.True(split.Succeeded, split.Message);
        Assert.NotEqual(coins, split.Stack);
        Assert.Equal(12, store.Get(split.Stack).Quantity);
        Assert.Equal(18, store.Get(coins).Quantity);
        Assert.Equal(
            ObjectLocation.InContainer(chest),
            store.Get(split.Stack).Location);
        Assert.Equal("Gold Coins", store.Get(split.Stack).Name);
        Assert.Equal(
            store.Get(coins).MaxQuantity,
            store.Get(split.Stack).MaxQuantity);
        store.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void SplitCountsOutsideTheStackAreRefused()
    {
        var store = Build(out _, out var pack, out var chest);
        var coins = store.Create(Coins("Gold Coins", pack, 5));
        var single = store.Create(new ObjectSpawn
        {
            TypeId = "item.sword",
            Name = "Sword",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.InContainer(pack),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });
        var stacks = new StackService(store);
        var destination = ObjectLocation.InContainer(chest);

        Assert.Equal(
            StackFailure.InvalidCount,
            stacks.Split(coins, 0, destination).Failure);
        Assert.Equal(
            StackFailure.InvalidCount,
            stacks.Split(coins, 5, destination).Failure);
        Assert.Equal(
            StackFailure.InvalidCount,
            stacks.Split(coins, 9, destination).Failure);
        Assert.Equal(
            StackFailure.NotStackable,
            stacks.Split(single, 1, destination).Failure);
        Assert.Equal(5, store.Get(coins).Quantity);
        Assert.Equal(2, store.GetContents(pack).Count);
    }

    [Fact]
    public void ASplitIntoAFullContainerIsRefusedBeforeAnythingChanges()
    {
        var store = Build(out _, out var pack, out _);
        var tin = store.Create(new ObjectSpawn
        {
            TypeId = "container.tin",
            Name = "Tin",
            ShapeId = "container.chest",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 24,
            Flags = ObjectFlags.Container | ObjectFlags.Visible,
            SlotCapacity = 1,
        });
        store.Create(Coins("Gold Coins", tin, 3));
        var coins = store.Create(Coins("Gold Coins", pack, 20));
        var stacks = new StackService(store);

        var split = stacks.Split(coins, 5, ObjectLocation.InContainer(tin));

        Assert.False(split.Succeeded);
        Assert.Equal(StackFailure.Rejected, split.Failure);
        Assert.Contains("full", split.Message);
        Assert.Equal(20, store.Get(coins).Quantity);
        Assert.Single(store.GetContents(tin));
    }

    [Fact]
    public void PartialTransferSplitsAndThenMergesIntoWhatIsAlreadyThere()
    {
        var store = Build(out _, out var pack, out var chest);
        var carried = store.Create(Coins("Gold Coins", pack, 40));
        var stored = store.Create(Coins("Gold Coins", chest, 25));
        var stacks = new StackService(store);

        var moved = stacks.TransferQuantity(
            carried,
            15,
            ObjectLocation.InContainer(chest));

        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(stored, moved.Stack);
        Assert.Equal(40, store.Get(stored).Quantity);
        Assert.Equal(25, store.Get(carried).Quantity);

        // One stack in the chest, not two halves side by side.
        Assert.Single(store.GetContents(chest));
        Assert.Equal(65, store.Get(stored).Quantity + store.Get(carried).Quantity);
        store.ValidateInvariants();
    }

    [Fact]
    public void MovingAWholeStackOntoACompatibleOneMergesInsteadOfPilingUp()
    {
        var store = Build(out _, out var pack, out var chest);
        var carried = store.Create(Coins("Gold Coins", pack, 12));
        var stored = store.Create(Coins("Gold Coins", chest, 30));
        var stacks = new StackService(store);

        var moved = stacks.TransferQuantity(
            carried,
            12,
            ObjectLocation.InContainer(chest));

        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(42, store.Get(stored).Quantity);
        Assert.False(store.TryGet(carried, out _));
        Assert.Single(store.GetContents(chest));
    }

    [Fact]
    public void AWholeStackWithNoMergeTargetJustMoves()
    {
        var store = Build(out _, out var pack, out var chest);
        var carried = store.Create(Coins("Gold Coins", pack, 12));
        var stacks = new StackService(store);

        var moved = stacks.TransferQuantity(
            carried,
            12,
            ObjectLocation.InContainer(chest));

        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(carried, moved.Stack);
        Assert.Equal(
            ObjectLocation.InContainer(chest),
            store.Get(carried).Location);
        Assert.Equal(12, store.Get(carried).Quantity);
    }

    [Fact]
    public void PartialTransferOntoTheMapLandsAsItsOwnStack()
    {
        var store = Build(out var map, out var pack, out _);
        var coins = store.Create(Coins("Gold Coins", pack, 30));
        var stacks = new StackService(store);
        var destination = ObjectLocation.OnMap(0, new Vec3i(1280, 512, 0));

        var moved = stacks.TransferQuantity(coins, 9, destination);

        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(21, store.Get(coins).Quantity);
        Assert.Equal(9, store.Get(moved.Stack).Quantity);
        Assert.Equal(destination, store.Get(moved.Stack).Location);
        Assert.Contains(map.QueryAll(), value => value.Id == moved.Stack);
        map.ValidateIndex();
        new PhysicsSystem(store).ValidateInvariants();
    }

    [Fact]
    public void QuantitiesAndStackLimitsSurviveASaveAndLoad()
    {
        var store = Build(out _, out var pack, out var chest);
        var coins = store.Create(Coins("Gold Coins", pack, 37));
        var arrows = store.Create(
            Coins("Arrows", chest, 12) with { TypeId = "item.arrow" });

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(
                    ObjectWorldSave.Capture(store, "ash.test-content.v1", 3, 0)),
                "ash.test-content.v1"));

        Assert.Equal(store.Get(coins), loaded.Objects.Get(coins));
        Assert.Equal(37, loaded.Objects.Get(coins).Quantity);
        Assert.Equal(100, loaded.Objects.Get(coins).MaxQuantity);
        Assert.Equal(12, loaded.Objects.Get(arrows).Quantity);
        loaded.Maps.Single().Dispose();
    }

    private static ObjectStore Build(
        out WorldMap map,
        out ObjectId pack,
        out ObjectId chest)
    {
        var store = new ObjectStore();
        map = new WorldMap(store, 0, width: 8, depth: 8);
        pack = store.Create(new ObjectSpawn
        {
            TypeId = "actor.avatar",
            Name = "Avatar",
            ShapeId = "avatar.knight",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = 12,
            Health = 10,
            MaxHealth = 10,
        });
        chest = store.Create(new ObjectSpawn
        {
            TypeId = "container.chest",
            Name = "Chest",
            ShapeId = "container.chest",
            Location = ObjectLocation.OnMap(0, new Vec3i(1024, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            Flags =
                ObjectFlags.Container | ObjectFlags.Solid | ObjectFlags.Visible,
            SlotCapacity = 10,
        });
        return store;
    }

    private static ObjectSpawn Coins(
        string name,
        ObjectId parent,
        int quantity) =>
        new()
        {
            TypeId = "item.gold",
            Name = name,
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(parent),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Stackable |
                ObjectFlags.Visible,
            Quantity = quantity,
            MaxQuantity = 100,
        };
}
