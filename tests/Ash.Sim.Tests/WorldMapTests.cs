using Ash.Core;

namespace Ash.Sim.Tests;

public sealed class WorldMapTests
{
    [Fact]
    public void FootprintSpansBucketsButRegionQueriesReturnOneStableObject()
    {
        var store = new ObjectStore();
        var wide = store.Create(Spawn(
            "wide",
            mapId: 3,
            new Vec3i(256, 256, 16),
            new ObjectFootprint(384, 128)));
        var narrow = store.Create(Spawn(
            "narrow",
            mapId: 3,
            new Vec3i(512, 256, 0),
            new ObjectFootprint(64, 64)));
        using var map = new WorldMap(store, 3, width: 8, depth: 8);

        var acrossBuckets = map.Query(
            new WorldRectangle(-256, 512, 0, 512));

        Assert.Equal([wide], map.Query(
                new WorldRectangle(-128, 0, 128, 256))
            .Select(value => value.Id));
        Assert.Equal([wide, narrow], acrossBuckets.Select(value => value.Id));
        Assert.Equal(
            [wide],
            map.Query(
                    new WorldVolume(
                        new WorldRectangle(-256, 512, 0, 512),
                        ZMin: 40,
                        ZMax: 48))
                .Select(value => value.Id));
        map.ValidateIndex();
    }

    [Fact]
    public void SuccessfulTransferUpdatesRevisionAndFailedTransferDoesNot()
    {
        var store = new ObjectStore();
        var actor = store.Create(new ObjectSpawn
        {
            TypeId = "actor",
            Name = "Actor",
            ShapeId = "actor",
            Location = ObjectLocation.OnMap(0, new Vec3i(256, 256, 0)),
            Flags = ObjectFlags.Actor | ObjectFlags.Container,
            ContainerCapacity = 2,
        });
        var item = store.Create(Spawn(
            "item",
            mapId: 0,
            new Vec3i(512, 256, 0),
            flags: ObjectFlags.Item | ObjectFlags.Visible));
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var transfers = new ObjectTransferService(store);
        var initialRevision = map.Revision;

        var contained = transfers.Execute(
            new ObjectTransferRequest(
                item,
                store.Get(item).Location,
                ObjectLocation.InContainer(actor)));

        Assert.True(contained.Succeeded, contained.Message);
        Assert.Equal(initialRevision + 1, map.Revision);
        Assert.DoesNotContain(map.QueryAll(), value => value.Id == item);
        map.ValidateIndex();

        var beforeFailure = map.Revision;
        var rejected = transfers.Execute(
            new ObjectTransferRequest(
                item,
                ObjectLocation.OnMap(0, Vec3i.Zero),
                ObjectLocation.OnMap(0, new Vec3i(768, 256, 0))));

        Assert.False(rejected.Succeeded);
        Assert.Equal(
            ObjectTransferFailure.SourceChanged,
            rejected.Failure);
        Assert.Equal(beforeFailure, map.Revision);
        Assert.Equal(
            ObjectLocation.InContainer(actor),
            store.Get(item).Location);

        var restored = transfers.Execute(
            new ObjectTransferRequest(
                item,
                store.Get(item).Location,
                ObjectLocation.OnMap(0, new Vec3i(768, 256, 0))));
        Assert.True(restored.Succeeded, restored.Message);
        Assert.Equal(
            [item],
            map.QueryAnchor(new Vec3i(768, 256, 0))
                .Select(value => value.Id));
        map.ValidateIndex();
    }

    [Fact]
    public void BatchCommitPublishesOneRevisionWithTheCompleteFinalState()
    {
        var store = new ObjectStore();
        var leftPosition = new Vec3i(256, 256, 0);
        var rightPosition = new Vec3i(512, 256, 0);
        var left = store.Create(Spawn("left", 0, leftPosition));
        var right = store.Create(Spawn("right", 0, rightPosition));
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var transfers = new ObjectTransferService(store);
        var revision = map.Revision;

        var result = transfers.Execute(
            new ObjectTransferRequest(
                left,
                store.Get(left).Location,
                ObjectLocation.OnMap(0, rightPosition)),
            new ObjectTransferRequest(
                right,
                store.Get(right).Location,
                ObjectLocation.OnMap(0, leftPosition)));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(revision + 1, map.Revision);
        Assert.Equal(
            [right],
            map.QueryAnchor(leftPosition).Select(value => value.Id));
        Assert.Equal(
            [left],
            map.QueryAnchor(rightPosition).Select(value => value.Id));
        map.ValidateIndex();
    }

    [Fact]
    public void DestroyAndSlotReuseLeaveNoGhostHandle()
    {
        var store = new ObjectStore();
        var original = store.Create(Spawn(
            "original",
            mapId: 0,
            new Vec3i(256, 256, 0)));
        using var map = new WorldMap(store, 0, width: 4, depth: 4);

        store.Destroy(original);
        var replacement = store.Create(Spawn(
            "replacement",
            mapId: 0,
            new Vec3i(256, 256, 0)));

        Assert.Equal(original.Index, replacement.Index);
        Assert.NotEqual(original.Generation, replacement.Generation);
        Assert.DoesNotContain(map.QueryAll(), value => value.Id == original);
        Assert.Equal(
            [replacement],
            map.QueryAnchor(new Vec3i(256, 256, 0))
                .Select(value => value.Id));
        map.ValidateIndex();
    }

    [Fact]
    public void MapAndCapabilityFiltersUseAuthoritativeCurrentState()
    {
        var store = new ObjectStore();
        var solid = store.Create(Spawn(
            "solid",
            mapId: 0,
            new Vec3i(256, 256, 0),
            flags: ObjectFlags.Solid | ObjectFlags.Visible));
        store.Create(Spawn(
            "other-map",
            mapId: 1,
            new Vec3i(256, 256, 0),
            flags: ObjectFlags.Solid | ObjectFlags.Visible));
        using var map = new WorldMap(store, 0, width: 4, depth: 4);

        Assert.Equal(
            [solid],
            map.QueryAll(ObjectFlags.Solid).Select(value => value.Id));

        store.Transform(
            solid,
            "non-solid",
            "non-solid",
            "shape",
            ObjectFlags.Visible);

        Assert.Empty(map.QueryAll(ObjectFlags.Solid));
        Assert.Equal([solid], map.QueryAll().Select(value => value.Id));
        map.ValidateIndex();
    }

    [Fact]
    public void ThousandObjectQueriesMatchBruteForceInDeterministicOrder()
    {
        var store = new ObjectStore();
        for (var index = 0; index < 1000; index++)
        {
            store.Create(Spawn(
                $"object-{index:D4}",
                mapId: (ushort)(index % 7 == 0 ? 1 : 0),
                new Vec3i(
                    (index % 40) * 256,
                    ((index / 40) % 30) * 256,
                    (index % 4) * 8),
                new ObjectFootprint(
                    index % 5 == 0 ? 320 : 96,
                    index % 3 == 0 ? 288 : 96),
                index % 2 == 0
                    ? ObjectFlags.Visible | ObjectFlags.Solid
                    : ObjectFlags.Visible));
        }

        using var map = new WorldMap(store, 0, width: 40, depth: 30);
        var regions = new[]
        {
            new WorldRectangle(0, 10 * 256, 0, 10 * 256),
            new WorldRectangle(7 * 256, 25 * 256, 3 * 256, 20 * 256),
            new WorldRectangle(-512, 42 * 256, -512, 32 * 256),
        };

        foreach (var region in regions)
        {
            var expected = store.Enumerate()
                .Where(value =>
                    value.Location.Kind == LocationKind.OnMap &&
                    value.Location.MapId == 0 &&
                    value.HasFlag(ObjectFlags.Solid) &&
                    WorldMap.BoundsFor(value).Overlaps(region))
                .Select(value => value.Id)
                .Order()
                .ToArray();
            var actual = map.Query(region, ObjectFlags.Solid)
                .Select(value => value.Id)
                .ToArray();

            Assert.Equal(expected, actual);
        }

        map.ValidateIndex();
    }

    [Fact]
    public void TerrainEditsAreMapStateAndAdvanceTheRevision()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 3, depth: 2);
        var initialRevision = map.Revision;
        var raised = new TerrainCell(
            FloorZ: 24,
            TerrainFlags.Walkable |
            TerrainFlags.ProvidesSupport |
            TerrainFlags.Hazard);

        map.SetTerrain(2, 1, raised);

        Assert.Equal(raised, map.GetTerrain(2, 1));
        Assert.Equal(initialRevision + 1, map.Revision);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => map.GetTerrain(3, 1));
    }

    [Fact]
    public void OverflowingDestinationRejectsBeforeStoreOrIndexMutation()
    {
        var store = new ObjectStore();
        var item = store.Create(Spawn(
            "item",
            0,
            new Vec3i(256, 256, 0)));
        using var map = new WorldMap(store, 0, width: 4, depth: 4);
        var transfers = new ObjectTransferService(store);
        var source = store.Get(item).Location;
        var revision = map.Revision;

        var result = transfers.Execute(
            new ObjectTransferRequest(
                item,
                source,
                ObjectLocation.OnMap(
                    0,
                    new Vec3i(int.MinValue, 256, 0))));

        Assert.False(result.Succeeded);
        Assert.Equal(
            ObjectTransferFailure.InvalidDestination,
            result.Failure);
        Assert.Equal(source, store.Get(item).Location);
        Assert.Equal(revision, map.Revision);
        map.ValidateIndex();
    }

    private static ObjectSpawn Spawn(
        string name,
        ushort mapId,
        Vec3i position,
        ObjectFootprint? footprint = null,
        ObjectFlags flags = ObjectFlags.Visible) =>
        new()
        {
            TypeId = $"test.{name}",
            Name = name,
            ShapeId = "shape",
            Location = ObjectLocation.OnMap(mapId, position),
            Footprint = footprint ?? new ObjectFootprint(128, 128),
            Height = 32,
            Flags = flags,
        };
}
