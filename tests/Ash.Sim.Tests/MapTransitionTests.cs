using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class MapTransitionTests
{
    private static readonly CharacterTier Tier =
        new(Level: 1, CharacterClass.Fighter);

    [Fact]
    public void ATravellerWalksFromOneSubzoneIntoTheNext()
    {
        var store = new ObjectStore();
        var first = WorldPlanner.PlanSubzone(21, 0, Tier);
        var second = WorldPlanner.PlanSubzone(21, 1, Tier);
        using var here = SubzoneBuilder.Build(store, first).Map;
        using var there = SubzoneBuilder.Build(store, second).Map;
        var traveller = SpawnTraveller(store, first);
        var transitions = new MapTransitionService(store);

        var arrival = transitions.Enter(traveller, second);

        Assert.True(arrival.Succeeded, arrival.Message);
        Assert.Equal(second.MapId, arrival.MapId);

        var moved = store.Get(traveller);
        Assert.Equal(second.MapId, moved.Location.MapId);
        Assert.Equal(MotionState.Resting, moved.Motion);

        // It arrives standing on the destination's floor, not hanging at the
        // height the search started from.
        Assert.Equal(second.Beats[0].FloorZ, moved.Location.Position.Z);
        Assert.NotEqual(MapTransitionService.SearchCeilingZ, moved.Location.Position.Z);

        here.ValidateIndex();
        there.ValidateIndex();

        // The subzone it left keeps its own contents and loses only the
        // traveller; the one it entered has gained it.
        Assert.DoesNotContain(
            here.QueryAll(),
            value => value.Id == traveller);
        Assert.Contains(there.QueryAll(), value => value.Id == traveller);
    }

    /// <summary>
    /// A pack travels because its owner does: carried and worn things are
    /// located relative to their owner, not to a map.
    /// </summary>
    [Fact]
    public void CarriedAndWornThingsTravelWithTheirOwner()
    {
        var store = new ObjectStore();
        var first = WorldPlanner.PlanSubzone(5, 0, Tier);
        var second = WorldPlanner.PlanSubzone(5, 1, Tier);
        using var here = SubzoneBuilder.Build(store, first).Map;
        using var there = SubzoneBuilder.Build(store, second).Map;
        var traveller = SpawnTraveller(store, first);
        var packed = store.Create(new ObjectSpawn
        {
            TypeId = "item.apple",
            Name = "Apple",
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(traveller),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        });
        var worn = store.Create(new ObjectSpawn
        {
            TypeId = "item.rusty-sword",
            Name = "Rusty Sword",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.Equipped(
                traveller,
                EquipmentSlot.RightHand),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
            EquipmentSlots = EquipmentSlotMask.RightHand,
        });

        Assert.True(
            new MapTransitionService(store)
                .Enter(traveller, second)
                .Succeeded);

        Assert.Equal(
            ObjectLocation.InContainer(traveller),
            store.Get(packed).Location);
        Assert.Equal(
            ObjectLocation.Equipped(traveller, EquipmentSlot.RightHand),
            store.Get(worn).Location);
        there.ValidateIndex();
    }

    /// <summary>
    /// The world has no rule for something resting on a body that leaves the
    /// map, so the move is refused rather than stranding a support reference
    /// that points at another map.
    /// </summary>
    [Fact]
    public void ATravellerHoldingSomethingUpCannotLeave()
    {
        var store = new ObjectStore();
        var first = WorldPlanner.PlanSubzone(13, 0, Tier);
        var second = WorldPlanner.PlanSubzone(13, 1, Tier);
        using var here = SubzoneBuilder.Build(store, first).Map;
        using var there = SubzoneBuilder.Build(store, second).Map;
        var traveller = SpawnTraveller(store, first);
        var stood = store.Get(traveller);
        var crate = store.Create(new ObjectSpawn
        {
            TypeId = "prop.crate",
            Name = "Crate",
            ShapeId = "container.chest",
            Location = ObjectLocation.OnMap(
                first.MapId,
                stood.Location.Position with
                {
                    Z = stood.Location.Position.Z + stood.Height,
                }),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags =
                ObjectFlags.Visible |
                ObjectFlags.Solid |
                ObjectFlags.AffectedByGravity,
        });

        // Let it settle onto the traveller, so the support back-link the
        // transition checks is the one physics actually resolved.
        new PhysicsSystem(store).Advance();
        Assert.Equal([crate], here.SupportedObjects(traveller));

        var refused = new MapTransitionService(store).Enter(traveller, second);

        Assert.False(refused.Succeeded);
        Assert.Equal(
            MapTransitionFailure.CarryingDependants,
            refused.Failure);
        Assert.Equal(first.MapId, store.Get(traveller).Location.MapId);
    }

    [Fact]
    public void ArrivingSomewhereWithNoFloorIsRefused()
    {
        var store = new ObjectStore();
        var plan = WorldPlanner.PlanSubzone(2, 0, Tier);
        using var here = SubzoneBuilder.Build(store, plan).Map;
        var second = WorldPlanner.PlanSubzone(2, 1, Tier);
        using var there = SubzoneBuilder.Build(store, second).Map;
        var traveller = SpawnTraveller(store, plan);

        // (0, 0) is solid rock in every generated subzone.
        var refused = new MapTransitionService(store)
            .Move(traveller, second.MapId, 0, 0);

        Assert.False(refused.Succeeded);
        Assert.Equal(MapTransitionFailure.NoFooting, refused.Failure);
    }

    [Fact]
    public void ArrivingOutsideTheMapOrOnNoMapAtAllIsRefused()
    {
        var store = new ObjectStore();
        var plan = WorldPlanner.PlanSubzone(2, 0, Tier);
        using var here = SubzoneBuilder.Build(store, plan).Map;
        var traveller = SpawnTraveller(store, plan);
        var transitions = new MapTransitionService(store);

        Assert.Equal(
            MapTransitionFailure.OffMap,
            transitions.Move(traveller, plan.MapId, plan.Width, 0).Failure);
        Assert.Equal(
            MapTransitionFailure.UnknownMap,
            transitions.Move(traveller, 999, 1, 1).Failure);
    }

    private static ObjectId SpawnTraveller(ObjectStore store, SubzonePlan plan)
    {
        // The atmospheric beat is the one with nothing standing in it.
        var beat = plan.Beats.First(candidate =>
            candidate.Kind == BeatKind.Atmospheric);
        var cellX = beat.Cells.XMin;
        var cellY = beat.Cells.YMin;
        return store.Create(new ObjectSpawn
        {
            TypeId = "actor.avatar",
            Name = "Avatar",
            ShapeId = "avatar.knight",
            Location = ObjectLocation.OnMap(
                plan.MapId,
                new Vec3i(
                    (cellX + 1) * WorldMap.WorldUnitsPerTile,
                    (cellY + 1) * WorldMap.WorldUnitsPerTile,
                    beat.FloorZ)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            StepHeight = PlayableSliceWorld.StepHeightUnits,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.ProvidesSupport |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
            Strength = 12,
            Dexterity = 12,
            Constitution = 14,
            Intelligence = 10,
            Wisdom = 10,
            Charisma = 12,
            Class = CharacterClass.Fighter,
            Level = 1,
            Health = 12,
            MaxHealth = 12,
        });
    }
}
