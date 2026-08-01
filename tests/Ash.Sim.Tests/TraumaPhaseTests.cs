using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class TraumaPhaseTests
{
    private const int PhysicsTicksPerBeat = 12;

    [Fact]
    public void ConditionsResumeWithExactTimingAfterSaveAndLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ash-conditions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            using var world = PlayableSliceWorld.CreateDemo();
            world.Conditions.Apply(
                world.PlayerId,
                ObjectId.None,
                new TraumaEffect(
                    TraumaEffectKind.Stun,
                    Duration: 2,
                    DurationUnit: TraumaDurationUnit.Rounds),
                world.Clock.Tick);
            var expected = Assert.Single(world.Conditions.Capture());
            Assert.True(world.RequestSave(path).Succeeded);

            using var loaded = PlayableSliceWorld.Load(path);
            var restored = Assert.Single(loaded.Conditions.Capture());

            Assert.Equal(expected, restored);
            Assert.True(loaded.Conditions.PreventsAction(loaded.PlayerId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void APeriodicTickResumesRatherThanRestartingAfterLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ash-bleed-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            using var world = PlayableSliceWorld.CreateDemo();
            world.Conditions.Apply(
                world.PlayerId,
                ObjectId.None,
                new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 1),
                world.Clock.Tick);
            AdvanceBeats(world, 10);
            var health = world.PlayerHealth;
            Assert.True(world.RequestSave(path).Succeeded);

            using var loaded = PlayableSliceWorld.Load(path);
            AdvanceBeats(loaded, ConditionTiming.BeatsPerRound - 10 - 1);
            Assert.Equal(health, loaded.PlayerHealth);
            AdvanceBeats(loaded, 1);
            Assert.Equal(health - 1, loaded.PlayerHealth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BleedingDealsDamageOnExactRoundBeats()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var start = world.PlayerHealth;
        world.Conditions.Apply(
            world.PlayerId,
            ObjectId.None,
            new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 1),
            world.Clock.Tick);

        AdvanceBeats(world, ConditionTiming.BeatsPerRound - 1);
        Assert.Equal(start, world.PlayerHealth);
        AdvanceBeats(world, 1);
        Assert.Equal(start - 1, world.PlayerHealth);
        AdvanceBeats(world, ConditionTiming.BeatsPerRound);
        Assert.Equal(start - 2, world.PlayerHealth);
    }

    [Fact]
    public void EveryStructuredTraumaKindHasAnExecutablePolicy()
    {
        foreach (var kind in Enum.GetValues<TraumaEffectKind>())
        {
            using var world = PlayableSliceWorld.CreateDemo();
            var rat = world.Monsters.Single(monster =>
                monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
            PlaceBeside(world, rat);

            var exception = Record.Exception(() => world.Trauma.Apply(
                rat.Id,
                world.PlayerId,
                [new TraumaEffect(
                    kind,
                    Magnitude: 1,
                    Duration: 1,
                    DurationUnit: TraumaDurationUnit.Rounds)]));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void ForcedMovementUsesSpatialTransferAndBrokenWeaponsStopBeingWeapons()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var before = world.PlayerPosition;

        var pushed = world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(TraumaEffectKind.ForcedMovement, Magnitude: 5)]);

        Assert.True(pushed.Moved);
        Assert.Equal(1, before.ManhattanDistance(world.PlayerPosition));

        Assert.True(world.ToggleRightHand().Succeeded);
        var weapon = world.PlayerSheet.Weapon;
        Assert.False(weapon.IsNone);
        var broken = world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(TraumaEffectKind.BreakItem, Detail: "Sword")]);

        Assert.Contains(weapon, broken.BrokenItems);
        Assert.True(world.Objects.Get(weapon).HasFlag(ObjectFlags.Broken));
        Assert.True(world.PlayerSheet.IsUnarmed);

        var scout = world.Monsters.Single(monster => monster.TypeId == "monster.goblin-scout");
        var blade = world.Objects.Enumerate().Single(item => item.Name == "Notched Blade");
        var dropped = world.Trauma.Apply(
            rat.Id,
            scout.Id,
            [new TraumaEffect(TraumaEffectKind.DropHeldItem, Detail: "Blade")]);
        Assert.Contains(blade.Id, dropped.DroppedItems);
        Assert.Equal(LocationKind.OnMap, world.Objects.Get(blade.Id).Location.Kind);
    }

    [Fact]
    public void LethalMultiEffectTraumaPublishesOneCoherentCorpseCommit()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var scout = world.Monsters.Single(monster => monster.TypeId == "monster.goblin-scout");
        var blade = world.Objects.Enumerate().Single(item => item.Name == "Notched Blade");
        var commits = 0;
        var revision = world.CurrentMap.Revision;
        world.Objects.Committed += commit =>
        {
            commits++;
            var corpse = world.Objects.Get(scout.Id);
            Assert.True(corpse.HasFlag(ObjectFlags.Corpse));
            Assert.False(corpse.HasFlag(ObjectFlags.Actor));
            Assert.True(world.Objects.Get(blade.Id).HasFlag(ObjectFlags.Broken));
            Assert.NotEqual(LocationKind.Equipped, world.Objects.Get(blade.Id).Location.Kind);
        };

        var result = world.Trauma.Apply(
            world.PlayerId,
            scout.Id,
            [
                new TraumaEffect(TraumaEffectKind.Stun, Duration: 1,
                    DurationUnit: TraumaDurationUnit.Rounds),
                new TraumaEffect(TraumaEffectKind.BreakItem, Detail: "Blade"),
                new TraumaEffect(TraumaEffectKind.ForcedMovement, Magnitude: 5),
                new TraumaEffect(TraumaEffectKind.Death),
            ]);

        Assert.True(result.CorpseCreated);
        Assert.Equal(1, commits);
        Assert.Equal(revision + 1, world.CurrentMap.Revision);
        Assert.Empty(world.Conditions.Of(scout.Id));
        world.CurrentMap.ValidateIndex();
        world.Objects.ValidateInvariants();
    }

    [Fact]
    public void FailedCompositeMutationRollsBackInjuryEquipmentAndPublication()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        Assert.True(world.ToggleRightHand().Succeeded);
        var weapon = world.PlayerSheet.Weapon;
        var beforeInjury = world.PlayerInjury;
        var beforeWeapon = world.Objects.Get(weapon);
        var damaged = Injury.Damage(beforeInjury, 1, new Dice(1)).State;
        var commits = 0;
        var revision = world.CurrentMap.Revision;
        world.Objects.Committed += _ => commits++;

        var staleSource = ObjectLocation.InContainer(world.PlayerId);
        Assert.Throws<ObjectTransferException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                [new ObjectTransferRequest(weapon, staleSource, world.Player.Location)],
                [weapon])));

        Assert.Throws<InvalidObjectIdException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                BreakItems: [ObjectId.None])));

        Assert.Throws<InvalidOperationException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                Transform: new CombatTransform(
                    world.PlayerId,
                    world.Player.TypeId,
                    world.Player.Name,
                    world.Player.ShapeId,
                    world.Player.Flags & ~ObjectFlags.Actor))));

        Assert.Throws<InvalidOperationException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                Conditions:
                [
                    new ActorConditionMutation(
                        world.PlayerId,
                        ObjectId.None,
                        new TraumaEffect(TraumaEffectKind.AdditionalHits),
                        world.Clock.Tick),
                ])));

        Assert.Equal(beforeInjury, world.PlayerInjury);
        Assert.Equal(beforeWeapon, world.Objects.Get(weapon));
        Assert.Equal(0, commits);
        Assert.Equal(revision, world.CurrentMap.Revision);
        world.CurrentMap.ValidateIndex();
        world.Objects.ValidateInvariants();
    }

    private static void PlaceBeside(PlayableSliceWorld world, WorldObject rat)
    {
        var destination = rat.Location.Position with
        {
            X = rat.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
        };
        var transfer = world.Transfers.Execute(new ObjectTransferRequest(
            world.PlayerId,
            world.Player.Location,
            ObjectLocation.OnMap(rat.Location.MapId, destination)));
        Assert.True(transfer.Succeeded, transfer.Message);
    }

    private static void AdvanceBeats(PlayableSliceWorld world, int beats)
    {
        for (var beat = 0; beat < beats; beat++)
        {
            var start = world.Clock.Tick;
            for (var tick = 0; tick <= PhysicsTicksPerBeat && world.Clock.Tick == start; tick++)
            {
                world.AdvancePhysics();
            }
        }
    }
}
