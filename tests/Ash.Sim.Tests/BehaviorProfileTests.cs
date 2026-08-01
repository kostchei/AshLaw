namespace Ash.Sim.Tests;

/// <summary>
/// The M6 exit proof for behaviour: four profiles, each observably different
/// from the others in an automated scenario.
/// </summary>
/// <remarks>
/// Every fixture puts identical creatures in identical positions against an
/// identical player and changes exactly one thing — the profile. What differs in
/// the outcome is therefore the profile and nothing else.
/// </remarks>
public sealed class BehaviorProfileTests
{
    /// <summary>Long enough for a ten-tile approach at six tiles a round.</summary>
    private const int ApproachBeats = 80;

    [Fact]
    public void APackHunterClosesFromRangeAndAGuardTurnsBackToItsPost()
    {
        var pack = Chase(BehaviorProfile.PackHunter);
        var guard = Chase(BehaviorProfile.Guard);
        using var packWorld = pack.Scenario;
        using var guardWorld = guard.Scenario;

        Assert.True(
            pack.Distance <= 1,
            $"A pack hunter should have closed to melee, and stopped " +
            $"{pack.Distance} tiles away.");
        Assert.True(
            guard.Distance > 1,
            $"A guard should not have crossed the room, and reached " +
            $"{guard.Distance} tiles away.");
        Assert.True(
            guard.FurthestFromHome <=
                BehaviorProfileCatalog.Guard.PursuitRadiusTiles,
            $"A guard left its post by {guard.FurthestFromHome} tiles, past the " +
            $"{BehaviorProfileCatalog.Guard.PursuitRadiusTiles} it holds.");
        Assert.True(pack.FurthestFromHome > guard.FurthestFromHome);
    }

    [Fact]
    public void ASkirmisherBacksOutOfReachAfterABlowAndAGuardStaysInIt()
    {
        var skirmisher = MeleeDistances(BehaviorProfile.Skirmisher);
        var guard = MeleeDistances(BehaviorProfile.Guard);

        Assert.Contains(
            skirmisher,
            distance => distance >= BehaviorProfileCatalog.Skirmisher.WithdrawRangeTiles);
        Assert.All(
            guard,
            distance => Assert.True(
                distance <= 1,
                $"A guard left melee reach, reaching {distance} tiles."));
    }

    [Fact]
    public void ACowardBreaksWhenItsBodyRunsLowAndTheOthersDoNot()
    {
        foreach (var profile in new[]
                 {
                     BehaviorProfile.Guard,
                     BehaviorProfile.PackHunter,
                     BehaviorProfile.Skirmisher,
                 })
        {
            var steady = Wounded(profile);
            Assert.DoesNotContain(steady.Events, value => value.Kind == CombatEventKind.Fled);
        }

        var coward = Wounded(BehaviorProfile.Coward);

        Assert.Contains(coward.Events, value => value.Kind == CombatEventKind.Fled);
        Assert.True(
            coward.Distance > 1,
            $"A broken creature should have put ground between itself and the " +
            $"player, and was {coward.Distance} tiles away.");
    }

    [Fact]
    public void EveryProfileProducesADistinctSignatureInOneScenario()
    {
        // One table, four rows, four different answers: this is the exit-proof
        // claim stated as a single assertion rather than inferred across tests.
        var signatures = new[]
            {
                BehaviorProfile.Guard,
                BehaviorProfile.PackHunter,
                BehaviorProfile.Skirmisher,
                BehaviorProfile.Coward,
            }
            .ToDictionary(profile => profile, Signature);

        Assert.Equal(4, signatures.Values.Distinct().Count());
    }

    [Fact]
    public void ProfilesComeFromTheMonsterContentAndCanBeReplacedPerActor()
    {
        Assert.Equal(
            BehaviorProfile.PackHunter,
            BehaviorProfileCatalog.ProfileForType("monster.giant-rat"));
        Assert.Equal(
            BehaviorProfile.Skirmisher,
            BehaviorProfileCatalog.ProfileForType("monster.bandit"));
        Assert.Equal(
            BehaviorProfile.Skirmisher,
            BehaviorProfileCatalog.ProfileForType("monster.giant-centipede"));
        Assert.Equal(
            BehaviorProfile.Guard,
            BehaviorProfileCatalog.ProfileForType("monster.beastman"));

        using var scenario = new CombatScenario();
        var monster = scenario.SpawnMonster(6, 5, BehaviorProfile.Guard);

        Assert.Equal(BehaviorProfile.Guard, scenario.Combat.ProfileOf(monster));
        scenario.Combat.SetProfile(monster, BehaviorProfile.Coward);
        Assert.Equal(BehaviorProfile.Coward, scenario.Combat.ProfileOf(monster));
        Assert.True(scenario.Behaviors.ClearOverride(monster));
        Assert.Equal(BehaviorProfile.PackHunter, scenario.Combat.ProfileOf(monster));
    }

    /// <summary>
    /// A compact description of what a profile did, so four rows can be compared
    /// for distinctness without four bespoke assertions.
    /// </summary>
    private static (int Closed, bool Withdrew, bool Fled, bool LeftPost) Signature(
        BehaviorProfile profile)
    {
        var chase = Chase(profile);
        using (chase.Scenario)
        {
            var melee = MeleeDistances(profile);
            var wounded = Wounded(profile);
            return (
                chase.Distance <= 1 ? 1 : 0,
                melee.Any(distance =>
                    distance >= BehaviorProfileCatalog.Skirmisher.WithdrawRangeTiles),
                wounded.Events.Any(value => value.Kind == CombatEventKind.Fled),
                chase.FurthestFromHome >
                    BehaviorProfileCatalog.Guard.PursuitRadiusTiles);
        }
    }

    /// <summary>Provoked from across the room, then left to decide for itself.</summary>
    private static (CombatScenario Scenario, int Distance, int FurthestFromHome) Chase(
        BehaviorProfile profile)
    {
        var scenario = new CombatScenario();
        scenario.PlacePlayer(20, 20);
        var monster = scenario.SpawnMonster(20, 30, profile);
        var home = scenario.Objects.Get(monster).Location.Position;
        scenario.Combat.Provoke(monster);

        var furthest = 0;
        for (var beat = 0; beat < ApproachBeats; beat++)
        {
            scenario.AdvanceBeat();
            if (!scenario.Objects.TryGet(monster, out var value))
            {
                break;
            }

            furthest = Math.Max(
                furthest,
                CombatScenario.TileDistance(value.Location.Position, home));
        }

        return (scenario, scenario.DistanceToPlayer(monster), furthest);
    }

    /// <summary>Provoked from melee reach; every distance it held afterwards.</summary>
    private static IReadOnlyList<int> MeleeDistances(BehaviorProfile profile)
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(20, 20);
        var monster = scenario.SpawnMonster(20, 21, profile);
        scenario.Combat.Provoke(monster);

        var distances = new List<int>();
        for (var beat = 0; beat < ApproachBeats; beat++)
        {
            scenario.AdvanceBeat();
            distances.Add(scenario.DistanceToPlayer(monster));
        }

        return distances;
    }

    /// <summary>Provoked from melee reach with most of its body already gone.</summary>
    private static (IReadOnlyList<CombatEvent> Events, int Distance) Wounded(
        BehaviorProfile profile)
    {
        using var scenario = new CombatScenario();
        scenario.PlacePlayer(20, 20);
        var monster = scenario.SpawnMonster(20, 21, profile, health: 12);
        _ = scenario.Vitality.Damage(monster, 8);
        scenario.Combat.Provoke(monster);

        var events = scenario.AdvanceBeats(ApproachBeats);
        return (events, scenario.DistanceToPlayer(monster));
    }
}
