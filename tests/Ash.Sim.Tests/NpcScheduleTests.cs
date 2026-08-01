namespace Ash.Sim.Tests;

public sealed class NpcScheduleTests
{
    [Fact]
    public void EveryAuthoredRoutineCoversTheWholeDay()
    {
        Assert.NotEmpty(NpcRoutineCatalog.All);
        foreach (var routine in NpcRoutineCatalog.All)
        {
            routine.Validate();
            for (var hour = 0; hour < WorldCalendar.HoursPerDay; hour++)
            {
                Assert.NotNull(routine.EntryAt(hour));
            }
        }
    }

    [Fact]
    public void ARoutineWithAGapInItsDayIsRefusedAsContent()
    {
        var gapped = new NpcRoutine(
            "routine.broken",
            "Broken",
            [new ScheduleEntry(6, 22, ScheduleActivity.Post, [(0, 0)])]);

        var error = Assert.Throws<ArgumentException>(gapped.Validate);
        Assert.Contains("does not cover hour", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDayTurnsWithTheCombatBeatAndBeatZeroIsDawn()
    {
        Assert.Equal(WorldCalendar.DawnHour, WorldCalendar.HourOf(0));
        Assert.Equal(
            WorldCalendar.DawnHour + 1,
            WorldCalendar.HourOf(WorldCalendar.BeatsPerHour));
        Assert.Equal(WorldCalendar.DawnHour, WorldCalendar.HourOf(WorldCalendar.BeatsPerDay));
        Assert.Equal(WorldCalendar.BeatsPerDay, WorldCalendar.NextDawnTick(0));
        Assert.Equal(
            2 * WorldCalendar.BeatsPerDay,
            WorldCalendar.NextDawnTick(WorldCalendar.BeatsPerDay));
        Assert.True(WorldCalendar.InWindow(23, 22, 6));
        Assert.True(WorldCalendar.InWindow(3, 22, 6));
        Assert.False(WorldCalendar.InWindow(12, 22, 6));
    }

    [Fact]
    public void AScheduledNpcWalksToItsDestinationAndStopsThere()
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(1, 1);
        var forager = scenario.SpawnMonster(20, 20, BehaviorProfile.Guard);
        scenario.Schedules.Assign(forager, NpcRoutineCatalog.ForagerId);

        // The morning block sends a forager four tiles east of its anchor.
        scenario.SkipToHour(8);
        var arrived = scenario.Objects.Get(forager).Location.Position;
        for (var beat = 0; beat < 200; beat++)
        {
            scenario.AdvanceBeat();
            arrived = scenario.Objects.Get(forager).Location.Position;
            if (arrived == CombatScenario.Anchor(24, 20))
            {
                break;
            }
        }

        Assert.Equal(CombatScenario.Anchor(24, 20), arrived);
        var state = Assert.IsType<NpcScheduleSnapshot>(scenario.Schedules.StateOf(forager));
        Assert.Equal(ScheduleActivity.Work, state.Activity);
        Assert.Equal(ScheduleFallback.None, state.Fallback);
    }

    [Fact]
    public void AnUnreachableDestinationFallsToTheNextInTheChainRatherThanStalling()
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(1, 1);
        var forager = scenario.SpawnMonster(20, 20, BehaviorProfile.Guard);
        scenario.Schedules.Assign(forager, NpcRoutineCatalog.ForagerId);

        // Wall off the far destination but not the near one. The wall spans the
        // map, so there is no way round it; it is built from ordinary solid
        // objects, and the schedule finds out through the same pathfinder a
        // pursuit uses.
        for (var tileY = 0; tileY < 40; tileY++)
        {
            scenario.SpawnWall(23, tileY);
        }

        scenario.SkipToHour(8);
        scenario.AdvanceBeat();

        var state = Assert.IsType<NpcScheduleSnapshot>(scenario.Schedules.StateOf(forager));
        Assert.Equal(ScheduleFallback.Alternate, state.Fallback);
        Assert.Equal(CombatScenario.Anchor(22, 20), state.Destination);
    }

    [Fact]
    public void AnNpcSealedInHoldsPositionInsteadOfBlockingForever()
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(1, 1);
        var forager = scenario.SpawnMonster(20, 20, BehaviorProfile.Guard);

        // Anchored to a post it has been shut away from: every destination in
        // the chain is somewhere it cannot get to, which is the case AI-014 is
        // about.
        scenario.Schedules.Assign(
            forager, NpcRoutineCatalog.ForagerId, CombatScenario.Anchor(30, 30));
        foreach (var (tileX, tileY) in new[]
                 {
                     (19, 20), (21, 20), (20, 19), (20, 21),
                 })
        {
            scenario.SpawnWall(tileX, tileY);
        }

        scenario.SkipToHour(8);
        var start = scenario.Objects.Get(forager).Location.Position;
        var steps = new List<ScheduleStep>();
        for (var beat = 0; beat < 20; beat++)
        {
            scenario.AdvanceBeat();
            steps.AddRange(scenario.Schedules.Advance(scenario.Combat.IsEngagedWithPlayer));
        }

        var state = Assert.IsType<NpcScheduleSnapshot>(scenario.Schedules.StateOf(forager));
        Assert.Equal(ScheduleFallback.HoldPosition, state.Fallback);
        Assert.Equal(start, scenario.Objects.Get(forager).Location.Position);
        Assert.Contains(steps, step => step.Fallback == ScheduleFallback.HoldPosition);
        Assert.DoesNotContain(steps, step => step.Moved);
    }

    [Fact]
    public void TheDayChangesWhatAnNpcIsDoing()
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(1, 1);
        var sentry = scenario.SpawnMonster(20, 20, BehaviorProfile.Guard);
        scenario.Schedules.Assign(sentry, NpcRoutineCatalog.SentryId);

        scenario.SkipToHour(10);
        scenario.AdvanceBeat();
        Assert.Equal(
            ScheduleActivity.Post,
            scenario.Schedules.StateOf(sentry)!.Value.Activity);

        // 22:00 is the start of the night block.
        scenario.SkipToHour(23);
        scenario.AdvanceBeat();
        Assert.Equal(
            ScheduleActivity.Sleep,
            scenario.Schedules.StateOf(sentry)!.Value.Activity);
    }

    [Fact]
    public void AFightSuspendsTheDayRatherThanCompetingWithIt()
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(20, 22);
        var sentry = scenario.SpawnMonster(20, 20, BehaviorProfile.Guard);
        scenario.Schedules.Assign(sentry, NpcRoutineCatalog.ForagerId);
        scenario.Combat.Provoke(sentry);

        scenario.AdvanceBeats(30);

        // The creature is engaged, so the schedule reports nothing for it: one
        // system spends its movement, never two.
        Assert.True(scenario.Combat.IsEngagedWithPlayer(sentry));
        Assert.Empty(scenario.Schedules.Advance(scenario.Combat.IsEngagedWithPlayer));
    }

    [Fact]
    public void AScriptCanTakeAnNpcOffItsRoutineForAWhile()
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(1, 1);
        var forager = scenario.SpawnMonster(20, 20, BehaviorProfile.Guard);
        scenario.Schedules.Assign(forager, NpcRoutineCatalog.ForagerId);
        scenario.SkipToHour(8);
        scenario.Schedules.Suspend(forager, scenario.Clock.Tick + 100);

        var start = scenario.Objects.Get(forager).Location.Position;
        scenario.AdvanceBeats(50);

        Assert.Equal(start, scenario.Objects.Get(forager).Location.Position);
    }

    [Fact]
    public void ScheduleStateSurvivesSaveAndLoad()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var world = PlayableSliceWorld.CreateDemo(20260801);
            var monster = world.Monsters.First();
            var assigned = world.Schedules.Assign(monster.Id, NpcRoutineCatalog.SentryId);

            Assert.True(world.RequestSave(path).Succeeded);
            using var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal([assigned], loaded.Schedules.Scheduled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASavedScheduleNamingAnUnknownRoutineIsRefused()
    {
        using var scenario = new CombatScenario();
        var monster = scenario.SpawnMonster(10, 10, BehaviorProfile.Guard);

        Assert.Throws<ObjectWorldSaveException>(() =>
            scenario.Schedules.Restore(
            [
                new NpcScheduleSnapshot(
                    monster,
                    "routine.does-not-exist",
                    CombatScenario.Anchor(10, 10),
                    ScheduleActivity.Post,
                    ScheduleFallback.None,
                    CombatScenario.Anchor(10, 10),
                    0,
                    0),
            ]));
    }
}
