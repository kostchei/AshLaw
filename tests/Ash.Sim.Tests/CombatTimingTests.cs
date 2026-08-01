using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class CombatTimingTests
{
    /// <summary>Twelve 60 Hz physics ticks make one 200 ms combat beat.</summary>
    private const int PhysicsTicksPerBeat = 12;

    [Fact]
    public void ACombatBeatIsTwelvePhysicsTicksAtSixtyHertz()
    {
        var clock = new CombatClock();

        Assert.Equal(PhysicsTicksPerBeat, clock.PhysicsTicksPerBeat);
        Assert.Equal(0L, clock.Tick);

        for (var tick = 1; tick < PhysicsTicksPerBeat; tick++)
        {
            Assert.False(clock.Advance(tick));
            Assert.Equal(0L, clock.Tick);
        }

        Assert.True(clock.Advance(PhysicsTicksPerBeat));
        Assert.Equal(1L, clock.Tick);
    }

    [Fact]
    public void AClockResumesOnTheBeatItsPhysicsTickNamesRatherThanFromZero()
    {
        // A world saved 100 beats in must not restart the fight's pacing: the
        // physics tick is the authority, so the clock reads the beat off it.
        var clock = new CombatClock(
            startPhysicsTick: 100 * PhysicsTicksPerBeat);

        Assert.Equal(100L, clock.Tick);
        Assert.False(clock.Advance(100 * PhysicsTicksPerBeat + 1));
        Assert.True(clock.Advance(101 * PhysicsTicksPerBeat));
        Assert.Equal(101L, clock.Tick);
    }

    [Fact]
    public void APhysicsRateThatCannotHoldWholeBeatsIsRefused()
    {
        // 144 Hz gives 28.8 physics ticks per 200 ms. Rounding it would drift
        // every swing, so the rate is refused rather than approximated.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CombatClock(physicsTicksPerSecond: 144));
    }

    [Fact]
    public void ADurationOffTheBeatIsRefused()
    {
        Assert.Equal(30, CombatClock.BeatsIn(6000));
        Assert.Equal(1, CombatClock.BeatsIn(200));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CombatClock.BeatsIn(250));
    }

    [Fact]
    public void RecognitionTakesBetweenOneAndSixSecondsThenTheNpcCriesOut()
    {
        // Every seed the dice can be started on must land inside 1d6 seconds:
        // five beats at the fastest, thirty at the slowest.
        for (ulong seed = 1; seed <= 24; seed++)
        {
            using var world = MeleeWorld(seed);
            var noticed = world.Clock.Tick;
            var alert = RunUntil(world, CombatEventKind.Alerted, maxBeats: 40);

            // The rat spends its first advanced beat noticing something is
            // there, then 1d6 seconds working out what.
            Assert.InRange(
                alert.Tick - noticed,
                1L + CombatClock.BeatsIn(1000),
                1L + CombatClock.BeatsIn(6000));
            Assert.Equal(CreatureVoice.Eep, alert.Voice);
            Assert.Contains("eep!", alert.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EncounterRollsActivityAndCharismaAdjustedReaction()
    {
        // Seed 8 rolls 5+3 for activity (searching), then 6+5 plus the
        // Avatar's +1 Charisma modifier: 12, friendly.
        using var world = MeleeWorld(seed: 8, playerDistanceTiles: 3);
        var rat = Rat(world);

        AdvanceBeats(world, 1);

        Assert.Equal(
            MonsterActivity.SearchingOrGathering,
            world.Combat.ActivityOf(rat.Id));
        Assert.Equal(
            MonsterReaction.Friendly,
            world.Combat.ReactionOf(rat.Id));
        Assert.Equal(Awareness.Recognizing, world.Combat.AwarenessOf(rat.Id));
    }

    [Fact]
    public void ASuccessfulAttackMakesAReactionHostile()
    {
        // The same seed would roll friendly if left alone. Attacking first
        // establishes hostility without making the unused reaction roll.
        using var world = MeleeWorld(seed: 8);
        var rat = Rat(world);

        Assert.True(world.AttackAdjacentMonster().Succeeded);

        Assert.Equal(
            MonsterReaction.Hostile,
            world.Combat.ReactionOf(rat.Id));
        Assert.Equal(Awareness.Recognizing, world.Combat.AwarenessOf(rat.Id));
    }

    [Fact]
    public void TheNpcActsExactlyOneBeatAfterItCriesOut()
    {
        using var world = MeleeWorld(seed: 7);
        var alert = RunUntil(world, CombatEventKind.Alerted, maxBeats: 40);
        Assert.Equal(
            Awareness.Alerted,
            world.Combat.AwarenessOf(alert.ActorId));

        var blow = RunUntil(world, CombatEventKind.Swing, maxBeats: 40);

        // 200 ms, and not a frame less: the cue is a warning with time behind it.
        Assert.Equal(alert.Tick + (2 * CombatClock.BeatsIn(200)), blow.Tick);
        Assert.Equal(
            Awareness.Engaged,
            world.Combat.AwarenessOf(alert.ActorId));
        Assert.InRange(blow.Damage, 0, int.MaxValue);
    }

    [Fact]
    public void AnNpcSwingsOnceEverySixSeconds()
    {
        using var world = MeleeWorld(seed: 7);
        var first = RunUntil(world, CombatEventKind.Swing, maxBeats: 40);
        var second = RunUntil(world, CombatEventKind.Swing, maxBeats: 40);

        Assert.Equal(first.Tick + CombatClock.BeatsIn(6000), second.Tick);
    }

    [Fact]
    public void AnNpcThatLosesTheThreadRollsRecognitionAgain()
    {
        using var world = MeleeWorld(seed: 3, playerDistanceTiles: 6);
        var rat = Rat(world);

        Assert.True(AdvanceBeats(world, 1));
        Assert.Equal(Awareness.Recognizing, world.Combat.AwarenessOf(rat.Id));

        // Step out of the awareness radius before the count finishes.
        Assert.True(world.MovePlayer(-1, 0).Succeeded);
        Assert.True(AdvanceBeats(world, 1));

        Assert.Equal(Awareness.Unaware, world.Combat.AwarenessOf(rat.Id));
    }

    [Fact]
    public void ThePlayerSwingsOnTheSameSixSecondRoundTheNpcsDo()
    {
        using var world = MeleeWorld(seed: 7);
        var rat = Rat(world);

        Assert.True(world.AttackAdjacentMonster().Succeeded);

        // Winding: the attack is refused, and the status line says how long for.
        var refused = world.AttackAdjacentMonster();
        Assert.False(refused.Succeeded);
        Assert.Contains("coming back round", refused.Message, StringComparison.Ordinal);
        Assert.False(world.Combat.PlayerCanSwing);
        Assert.Equal(6000, world.Combat.PlayerCooldownRemainingMilliseconds);

        // One beat short of the round, still winding.
        AdvanceBeats(world, CombatClock.BeatsIn(6000) - 1);
        Assert.False(world.Combat.PlayerCanSwing);
        Assert.Equal(200, world.Combat.PlayerCooldownRemainingMilliseconds);

        AdvanceBeats(world, 1);
        Assert.True(world.Combat.PlayerCanSwing);
        Assert.Equal(0, world.Combat.PlayerCooldownRemainingMilliseconds);
        var beforeSecond = world.Objects.Get(rat.Id).Health;
        Assert.True(world.AttackAdjacentMonster().Succeeded);
        CombatRound.WaitForPlayerImpact(world);
        Assert.InRange(world.Objects.Get(rat.Id).Health, 0, beforeSecond);
    }

    [Fact]
    public void MovingAndAttackingDrawOnSeparateAllowances()
    {
        // The round holds a maximum move and a maximum number of attacks. You
        // may move and attack in the same six seconds, and neither spends the
        // other's budget.
        using var world = MeleeWorld(seed: 7);
        Assert.True(world.AttackAdjacentMonster().Succeeded);
        var swingRemaining = world.Combat.PlayerCooldownRemainingMilliseconds;
        var stepsRemaining = world.Combat.PlayerStepsRemainingInRound;

        Assert.True(world.MovePlayer(-1, 0).Succeeded);
        Assert.True(world.MovePlayer(1, 0).Succeeded);

        // Two steps spent, and the swing timer untouched by them.
        Assert.Equal(
            stepsRemaining - 2,
            world.Combat.PlayerStepsRemainingInRound);
        Assert.Equal(
            swingRemaining,
            world.Combat.PlayerCooldownRemainingMilliseconds);
    }

    [Fact]
    public void TheRoundAllowsOnlySoManyTilesOfMovement()
    {
        using var world = MeleeWorld(seed: 7, playerDistanceTiles: 8);
        Assert.Equal(
            MovementAllowance.StepsPerRound,
            world.Combat.PlayerStepsRemainingInRound);

        for (var step = 0; step < MovementAllowance.StepsPerRound; step++)
        {
            Assert.True(world.MovePlayer(1, 0).Succeeded);
        }

        var spent = world.MovePlayer(1, 0);
        Assert.False(spent.Succeeded);
        Assert.Contains(
            "as much ground as the round allows",
            spent.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, world.Combat.PlayerStepsRemainingInRound);
    }

    [Fact]
    public void TheMoveRefillsAsStepsAgeOutRatherThanOnARoundBoundary()
    {
        // A bucket refilling on a boundary would let a player spend the last
        // of one round and the first of the next back to back, and cross twice
        // the ground the round allows. A rolling window cannot.
        using var world = MeleeWorld(seed: 7, playerDistanceTiles: 8);
        for (var step = 0; step < MovementAllowance.StepsPerRound; step++)
        {
            Assert.True(world.MovePlayer(1, 0).Succeeded);
        }

        Assert.False(world.MovePlayer(-1, 0).Succeeded);

        // The oldest step ages out one round after it was taken, and only that
        // one comes back.
        AdvanceBeats(world, CombatClock.BeatsIn(AttackSpeed.RoundMilliseconds));
        Assert.Equal(
            MovementAllowance.StepsPerRound,
            world.Combat.PlayerStepsRemainingInRound);
    }

    [Fact]
    public void AMoveBlockedByTheWorldCostsNothingFromTheRoundsMove()
    {
        using var world = MeleeWorld(seed: 7);
        var before = world.Combat.PlayerStepsRemainingInRound;

        // The rat is solid and directly east: this step is refused by the
        // world, not by the budget, so it must not be charged.
        Assert.False(world.MovePlayer(1, 0).Succeeded);

        Assert.Equal(before, world.Combat.PlayerStepsRemainingInRound);
    }

    [Fact]
    public void AHostileNpcPursuesThenTakesABeatToChangeToAnAttack()
    {
        // Seed 7 gives a hostile reaction. The rat starts three tiles away.
        using var world = MeleeWorld(seed: 7, playerDistanceTiles: 3);
        var rat = Rat(world);
        var start = world.GetGridPosition(rat.Id);
        var alert = RunUntil(world, CombatEventKind.Alerted, maxBeats: 40);

        // The decision does not move the rat on the alert beat. One 200 ms
        // beat later its pursuit action executes through normal movement.
        Assert.Equal(start, world.GetGridPosition(rat.Id));
        Assert.Empty(AdvanceBeat(world));
        Assert.Equal(alert.Tick + 1, world.Clock.Tick);
        Assert.Equal(
            2,
            world.PlayerPosition.ManhattanDistance(
                world.GetGridPosition(rat.Id)));
        Assert.Equal(Awareness.Engaged, world.Combat.AwarenessOf(rat.Id));

        // It takes its next paced step, then spends one whole beat changing
        // from pursuit to attack before the blow can land.
        var blow = RunUntil(world, CombatEventKind.Swing, maxBeats: 10);

        Assert.Equal(rat.Id, blow.ActorId);
    }

    [Fact]
    public void ANonHostileNpcDoesNotCloseTheDistance()
    {
        using var world = MeleeWorld(seed: 8, playerDistanceTiles: 3);
        var rat = Rat(world);
        var start = world.GetGridPosition(rat.Id);

        AdvanceBeats(world, 50);

        Assert.Equal(MonsterReaction.Friendly, world.Combat.ReactionOf(rat.Id));
        Assert.Equal(Awareness.Engaged, world.Combat.AwarenessOf(rat.Id));
        Assert.Equal(start, world.GetGridPosition(rat.Id));
    }

    [Fact]
    public void PursuitCannotEnterAnOccupiedSpatialCell()
    {
        using var world = MeleeWorld(seed: 7, playerDistanceTiles: 3);
        var rat = Rat(world);
        var start = world.GetGridPosition(rat.Id);
        var blockerPosition = rat.Location.Position with
        {
            X = rat.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
        };
        world.Objects.Create(new ObjectSpawn
        {
            TypeId = "test.pursuit-blocker",
            Name = "Pursuit Blocker",
            ShapeId = "shape",
            Location = ObjectLocation.OnMap(
                PlayableSliceWorld.DemoMapId,
                blockerPosition),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            Flags = ObjectFlags.Fixed | ObjectFlags.Solid,
        });

        var blockedCell = new GridPosition(start.X - 1, start.Y);
        RunUntil(world, CombatEventKind.Alerted, maxBeats: 40);

        // The direct line is closed, so the rat routes round the obstacle
        // instead of standing against it. It must still never occupy the
        // blocked cell, and the index must stay coherent through every step.
        for (var beat = 0; beat < 12; beat++)
        {
            AdvanceBeat(world);
            Assert.NotEqual(blockedCell, world.GetGridPosition(rat.Id));
            world.CurrentMap.ValidateIndex();
        }

        var player = world.PlayerPosition;
        Assert.NotEqual(start, world.GetGridPosition(rat.Id));
        Assert.True(
            world.GetGridPosition(rat.Id).ManhattanDistance(player) <
                start.ManhattanDistance(player),
            "A routed pursuit must close on the player rather than stall on the " +
            "obstacle in the direct line.");
    }

    [Fact]
    public void AHostileMonsterSearchesThenReturnsToItsHomeTerritory()
    {
        using var world = NpcWorld(
            seed: 7,
            typeId: "monster.goblin-scout",
            playerDistanceTiles: 3);
        var scout = world.Monsters.Single(value =>
            value.TypeId == "monster.goblin-scout");
        var home = world.GetGridPosition(scout.Id);

        RunUntil(world, CombatEventKind.Alerted, maxBeats: 40);
        AdvanceBeat(world);
        PlacePlayerFarFrom(world, scout.Id, minimumDistanceTiles: 10);

        AdvanceBeat(world);
        Assert.Equal(Awareness.Searching, world.Combat.AwarenessOf(scout.Id));

        var returned = false;
        for (var beat = 0; beat < 80; beat++)
        {
            AdvanceBeat(world);
            if (world.Combat.AwarenessOf(scout.Id) == Awareness.Unaware)
            {
                returned = true;
                break;
            }
        }

        Assert.True(returned);
        Assert.Equal(home, world.GetGridPosition(scout.Id));
    }

    [Fact]
    public void ProvokingAPackHunterAlertsNearbyPackmates()
    {
        using var world = MeleeWorld(seed: 8);
        var rat = Rat(world);
        var ally = SpawnPackmate(world, rat);

        Assert.True(world.AttackAdjacentMonster().Succeeded);

        Assert.Equal(MonsterReaction.Hostile, world.Combat.ReactionOf(ally.Id));
        Assert.Equal(Awareness.Alerted, world.Combat.AwarenessOf(ally.Id));
        AdvanceBeat(world);
        Assert.Equal(Awareness.Engaged, world.Combat.AwarenessOf(ally.Id));
    }

    [Fact]
    public void EveryCreatureThatCanRaiseAnAlarmHasAVoice()
    {
        using var world = PlayableSliceWorld.CreateDemo();

        foreach (var monster in world.Monsters)
        {
            // Throws if a type id has no voice: a silent alert would give the
            // player nothing to react to.
            Assert.True(Enum.IsDefined(CreatureVoices.For(monster.TypeId)));
        }

        Assert.Equal(
            CreatureVoice.Hail,
            CreatureVoices.For(world.Player.TypeId));
        Assert.Throws<InvalidOperationException>(
            () => CreatureVoices.For("monster.unvoiced"));
    }

    [Fact]
    public void TheRoundHoldsOneAttackUntilAFighterMastersAWeaponAtSeventh()
    {
        Assert.Equal(
            1,
            AttackSpeed.AttacksPerRound(
                new AttackSpeedInputs(CharacterClass.Fighter, 1, true)));

        // Seventh level alone is not the rule; mastery is the whole condition.
        Assert.Equal(
            1,
            AttackSpeed.AttacksPerRound(
                new AttackSpeedInputs(CharacterClass.Fighter, 7, false)));
        Assert.Equal(
            1,
            AttackSpeed.AttacksPerRound(
                new AttackSpeedInputs(CharacterClass.Rogue, 20, true)));

        var master = new AttackSpeedInputs(CharacterClass.Fighter, 7, true);
        Assert.Equal(2, AttackSpeed.AttacksPerRound(master));
        Assert.Equal(3000, AttackSpeed.SwingIntervalMilliseconds(master));
        Assert.Equal(
            6000,
            AttackSpeed.SwingIntervalMilliseconds(
                new AttackSpeedInputs(CharacterClass.Fighter, 6, true)));
    }

    /// <summary>The cave rat, which is the only monster near the Avatar's start.</summary>
    private static WorldObject Rat(PlayableSliceWorld world) =>
        world.Monsters.Single(monster => monster.TypeId == "monster.cave-rat");

    /// <summary>
    /// The demo world with the Avatar placed a given number of tiles west of
    /// the cave rat. The rest of the demo is left standing, so the test paces a
    /// fight in the world the game actually runs — every other monster is well
    /// outside the awareness radius and stays unaware throughout.
    /// </summary>
    /// <remarks>
    /// The Avatar is placed rather than walked. Walking there would burn
    /// rounds inside the awareness radius, and the rat would have started
    /// recognising the player before the test had even begun measuring.
    /// </remarks>
    private static PlayableSliceWorld MeleeWorld(
        ulong seed,
        int playerDistanceTiles = 1)
        => NpcWorld(seed, "monster.cave-rat", playerDistanceTiles);

    private static PlayableSliceWorld NpcWorld(
        ulong seed,
        string typeId,
        int playerDistanceTiles)
    {
        var world = PlayableSliceWorld.CreateDemo(seed, new TimingAttackResolver());
        var npc = world.Monsters.Single(value => value.TypeId == typeId);
        var npcCell = world.GetGridPosition(npc.Id);
        var target = npc.Location.Position with
        {
            X = npc.Location.Position.X -
                (playerDistanceTiles * PlayableSliceWorld.WorldUnitsPerTile),
        };
        var placed = world.Transfers.Execute(
            new ObjectTransferRequest(
                world.PlayerId,
                world.Player.Location,
                ObjectLocation.OnMap(PlayableSliceWorld.DemoMapId, target)));
        Assert.True(placed.Succeeded, placed.Message);
        Assert.Equal(
            playerDistanceTiles,
            world.PlayerPosition.ManhattanDistance(npcCell));
        return world;
    }

    private static void PlacePlayerFarFrom(
        PlayableSliceWorld world,
        ObjectId npcId,
        int minimumDistanceTiles)
    {
        var npc = world.GetGridPosition(npcId);
        var candidates =
            from y in Enumerable.Range(0, world.CurrentMap.Depth)
            from x in Enumerable.Range(0, world.CurrentMap.Width)
            let cell = new GridPosition(x, y)
            let terrain = world.CurrentMap.GetTerrain(x, y)
            where terrain.Flags.HasFlag(TerrainFlags.Walkable)
            where cell.ManhattanDistance(npc) >= minimumDistanceTiles
            orderby cell.ManhattanDistance(npc) descending
            select new Vec3i(
                (x + 1) * PlayableSliceWorld.WorldUnitsPerTile,
                (y + 1) * PlayableSliceWorld.WorldUnitsPerTile,
                terrain.FloorZ);

        foreach (var position in candidates)
        {
            var placed = world.Transfers.Execute(
                new ObjectTransferRequest(
                    world.PlayerId,
                    world.Player.Location,
                    ObjectLocation.OnMap(world.CurrentMapId, position)));
            if (placed.Succeeded)
            {
                return;
            }
        }

        throw new InvalidOperationException("No distant legal player cell was found.");
    }

    private static WorldObject SpawnPackmate(
        PlayableSliceWorld world,
        WorldObject source)
    {
        for (var distance = 2; distance <= 5; distance++)
        {
            foreach (var (deltaX, deltaY) in new[]
                     {
                         (distance, 0), (-distance, 0),
                         (0, distance), (0, -distance),
                     })
            {
                var location = ObjectLocation.OnMap(
                    world.CurrentMapId,
                    source.Location.Position with
                    {
                        X = source.Location.Position.X +
                            (deltaX * PlayableSliceWorld.WorldUnitsPerTile),
                        Y = source.Location.Position.Y +
                            (deltaY * PlayableSliceWorld.WorldUnitsPerTile),
                    });
                var spawn = new ObjectSpawn
                {
                    TypeId = source.TypeId,
                    Name = "Second Cave Rat",
                    ShapeId = source.ShapeId,
                    Location = location,
                    Footprint = source.Footprint,
                    Height = source.Height,
                    Flags = source.Flags,
                    Strength = source.Strength,
                    Dexterity = source.Dexterity,
                    Constitution = source.Constitution,
                    Intelligence = source.Intelligence,
                    Wisdom = source.Wisdom,
                    Charisma = source.Charisma,
                    Class = source.Class,
                    Level = source.Level,
                    Health = source.Health,
                    MaxHealth = source.MaxHealth,
                    Wounds = source.Wounds,
                    MaxWounds = source.MaxWounds,
                };
                if (world.CurrentMap.ValidatePlacement(
                        spawn.AsProbe(location),
                        location).Allowed)
                {
                    return world.Objects.Get(world.Objects.Create(spawn));
                }
            }
        }

        throw new InvalidOperationException("No legal nearby packmate cell was found.");
    }

    /// <summary>Keeps timing tests about clocks and movement, not lethal rolls.</summary>
    private sealed class TimingAttackResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = true,
            RawD20 = request.RawD20,
            NetRoll = request.RawD20,
            Margin = 1,
            ConcussionHits = 1,
            Mishap = false,
        };
    }

    /// <summary>
    /// Advances one combat beat and returns what it produced. Physics ticks run
    /// until the clock's beat actually changes, because a beat is twelve
    /// physics ticks and the events belong to the tick that completed it — the
    /// eleven ticks after it carry none.
    /// </summary>
    private static IReadOnlyList<CombatEvent> AdvanceBeat(
        PlayableSliceWorld world)
    {
        var start = world.Clock.Tick;
        for (var tick = 0; tick <= PhysicsTicksPerBeat; tick++)
        {
            world.AdvancePhysics();
            if (world.Clock.Tick != start)
            {
                return world.LastCombatEvents;
            }
        }

        throw new InvalidOperationException(
            $"The combat beat did not advance within {PhysicsTicksPerBeat} " +
            "physics ticks.");
    }

    private static bool AdvanceBeats(PlayableSliceWorld world, int beats)
    {
        for (var beat = 0; beat < beats; beat++)
        {
            AdvanceBeat(world);
        }

        return true;
    }

    private static CombatEvent RunUntil(
        PlayableSliceWorld world,
        CombatEventKind kind,
        int maxBeats)
    {
        for (var beat = 0; beat < maxBeats; beat++)
        {
            foreach (var combat in AdvanceBeat(world))
            {
                if (combat.Kind == kind)
                {
                    return combat;
                }
            }
        }

        throw new InvalidOperationException(
            $"No {kind} event happened within {maxBeats} combat beats.");
    }
}
