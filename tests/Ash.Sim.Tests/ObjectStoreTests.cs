using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class ObjectStoreTests
{
    [Fact]
    public void DestroyedHandleIsStaleWhenItsSlotIsReused()
    {
        var store = new ObjectStore();
        var original = store.Create(Item("first"));

        store.Destroy(original);

        Assert.False(store.TryGet(original, out _));
        Assert.Throws<InvalidObjectIdException>(() => store.Get(original));

        var replacement = store.Create(Item("replacement"));
        Assert.Equal(original.Index, replacement.Index);
        Assert.NotEqual(original.Generation, replacement.Generation);
        Assert.NotEqual(original, replacement);
        Assert.Equal("replacement", store.Get(replacement).Name);
    }

    [Fact]
    public void OneHandleSurvivesEveryLocationKind()
    {
        var store = new ObjectStore();
        var actor = store.Create(Container(
            "actor",
            ObjectFlags.Actor | ObjectFlags.Container | ObjectFlags.Visible));
        var chest = store.Create(Container("chest", ObjectFlags.Container));
        var item = store.Create(Item("sword"));

        store.Move(item, ObjectLocation.InContainer(chest));
        Assert.Equal(item, store.GetContents(chest).Single());
        Assert.Throws<InvalidOperationException>(
            () => store.Get(item).Location.Position);

        store.Move(item, ObjectLocation.Equipped(actor, slot: 2));
        Assert.Equal(actor, store.Get(item).Location.Parent);
        Assert.Equal(2, store.Get(item).Location.Slot);

        store.Move(item, ObjectLocation.InTransfer(42));
        Assert.Equal(42u, store.Get(item).Location.TransferId);

        var finalPosition = new Vec3i(2048, 1024, 7);
        store.Move(item, ObjectLocation.OnMap(3, finalPosition));
        var final = store.Get(item);
        Assert.Equal(item, final.Id);
        Assert.Equal((ushort)3, final.Location.MapId);
        Assert.Equal(finalPosition, final.Location.Position);
    }

    [Fact]
    public void ContainerCapacityAndParentCyclesAreRejectedBeforeMutation()
    {
        var store = new ObjectStore();
        var outer = store.Create(Container("outer", ObjectFlags.Container, 1));
        var inner = store.Create(
            Container(
                "inner",
                ObjectFlags.Container,
                1,
                ObjectLocation.InContainer(outer)));
        var item = store.Create(Item("gem"));

        Assert.Throws<InvalidOperationException>(
            () => store.Move(item, ObjectLocation.InContainer(outer)));
        Assert.Equal(LocationKind.OnMap, store.Get(item).Location.Kind);

        Assert.Throws<InvalidOperationException>(
            () => store.Move(outer, ObjectLocation.InContainer(inner)));
        Assert.Equal(LocationKind.OnMap, store.Get(outer).Location.Kind);
    }

    [Fact]
    public void TransformingMonsterToCorpsePreservesIdentityAndLoot()
    {
        var store = new ObjectStore();
        var monster = store.Create(new ObjectSpawn
        {
            TypeId = "goblin",
            Name = "Goblin",
            ShapeId = "monster.goblin",
            Location = ObjectLocation.OnMap(0, new Vec3i(256, 256, 0)),
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Monster |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            ContainerCapacity = 4,
            Health = 2,
            MaxHealth = 2,
            Height = 56,
        });
        var loot = store.Create(
            Item("token", ObjectLocation.InContainer(monster)));

        Assert.Equal(0, store.Damage(monster, 2));
        store.Transform(
            monster,
            "remains.goblin",
            "Remains of Goblin",
            "container.corpse",
            ObjectFlags.Container |
            ObjectFlags.Corpse |
            ObjectFlags.Usable |
            ObjectFlags.Visible,
            height: 24);

        var corpse = store.Get(monster);
        Assert.Equal(monster, corpse.Id);
        Assert.True(corpse.HasFlag(ObjectFlags.Corpse));
        Assert.False(corpse.HasFlag(ObjectFlags.Monster));
        Assert.Equal(loot, store.GetContents(monster).Single());
    }

    [Fact]
    public void ContainerWithChildrenRequiresExplicitContentPolicy()
    {
        var store = new ObjectStore();
        var container = store.Create(Container("bag", ObjectFlags.Container));
        var item = store.Create(
            Item("apple", ObjectLocation.InContainer(container)));

        Assert.Throws<InvalidOperationException>(
            () => store.Destroy(container));
        Assert.Equal(container, store.Get(container).Id);
        Assert.Equal(item, store.GetContents(container).Single());
    }

    [Fact]
    public void EnumerationOfOneThousandObjectsIsStableBySlot()
    {
        var store = new ObjectStore();
        var created = Enumerable.Range(0, 1000)
            .Select(index => store.Create(Item($"item-{index:D4}")))
            .ToArray();

        Assert.Equal(1000, store.Count);
        Assert.Equal(created, store.Enumerate().Select(item => item.Id));
        store.ValidateInvariants();
    }

    private static ObjectSpawn Item(
        string name,
        ObjectLocation? location = null) =>
        new()
        {
            TypeId = $"item.{name}",
            Name = name,
            ShapeId = "loot.generic",
            Location = location ??
                ObjectLocation.OnMap(0, new Vec3i(0, 0, 0)),
            Flags = ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Visible,
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
        };

    private static ObjectSpawn Container(
        string name,
        ObjectFlags flags,
        int capacity = 8,
        ObjectLocation? location = null) =>
        new()
        {
            TypeId = $"container.{name}",
            Name = name,
            ShapeId = "container.generic",
            Location = location ??
                ObjectLocation.OnMap(0, new Vec3i(0, 0, 0)),
            Flags = flags | ObjectFlags.Container,
            ContainerCapacity = capacity,
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
        };
}
