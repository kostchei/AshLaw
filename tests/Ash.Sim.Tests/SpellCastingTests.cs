using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class SpellCastingTests
{
    [Fact]
    public void EachTargetingModeRefusesTheWrongKindOfTargetBeforeAnythingCommits()
    {
        using var scenario = Caster(new HittingResolver(), out _);
        var monster = scenario.SpawnMonster(8, 5, BehaviorProfile.Guard);

        Assert.Equal(
            SpellCastFailure.IllegalTarget,
            Begin(scenario, SpellCatalog.WitherFlesh.Id).Failure);
        Assert.Equal(
            SpellCastFailure.IllegalTarget,
            Begin(scenario, SpellCatalog.ShatterStone.Id, monster).Failure);
        Assert.Equal(
            SpellCastFailure.IllegalTarget,
            Begin(scenario, SpellCatalog.SpellfireDraw.Id, monster).Failure);
        Assert.Equal(
            SpellCastFailure.UnknownSpell,
            Begin(scenario, "spell.not-authored").Failure);

        // Nothing was spent by any of the refusals.
        Assert.Equal(
            6,
            scenario.Spells.ReagentsCarried(
                scenario.PlayerId, SpellCatalog.GraveSaltTypeId));
        Assert.Null(scenario.Spells.CastInProgress(scenario.PlayerId));
    }

    [Fact]
    public void ATargetBeyondRangeIsRefusedAndOneInsideItIsAccepted()
    {
        using var scenario = Caster(new HittingResolver(), out _);
        var far = scenario.SpawnMonster(5 + SpellCatalog.WitherFlesh.RangeTiles + 2, 5,
            BehaviorProfile.Guard);
        var near = scenario.SpawnMonster(9, 5, BehaviorProfile.Guard);

        Assert.Equal(
            SpellCastFailure.OutOfRange,
            Begin(scenario, SpellCatalog.WitherFlesh.Id, far).Failure);
        Assert.True(Begin(scenario, SpellCatalog.WitherFlesh.Id, near).Accepted);
    }

    [Fact]
    public void ReagentsArePaidAtReleaseAndACasterWithoutThemIsRefusedUpFront()
    {
        using var scenario = Caster(new HittingResolver(), out _, reagents: 1);
        var monster = scenario.SpawnMonster(9, 5, BehaviorProfile.Guard);

        // Shatter Stone costs two and there is one.
        Assert.Equal(
            SpellCastFailure.MissingReagents,
            Begin(scenario, SpellCatalog.ShatterStone.Id,
                aim: CombatScenario.Anchor(9, 5)).Failure);

        Assert.True(Begin(scenario, SpellCatalog.WitherFlesh.Id, monster).Accepted);
        Assert.Equal(
            1,
            scenario.Spells.ReagentsCarried(
                scenario.PlayerId, SpellCatalog.GraveSaltTypeId));

        scenario.AdvanceBeats(
            CombatClock.BeatsIn(SpellCatalog.WitherFlesh.WindUpMilliseconds));

        Assert.Equal(
            0,
            scenario.Spells.ReagentsCarried(
                scenario.PlayerId, SpellCatalog.GraveSaltTypeId));
        Assert.Single(scenario.Projectiles.InFlight);
    }

    [Fact]
    public void ACastWindsUpAndOnlyThenPutsSomethingInTheAir()
    {
        var resolver = new HittingResolver();
        using var scenario = Caster(resolver, out _);
        var monster = scenario.SpawnMonster(9, 5, BehaviorProfile.Guard);
        var windUp = CombatClock.BeatsIn(SpellCatalog.WitherFlesh.WindUpMilliseconds);

        var attempt = Begin(scenario, SpellCatalog.WitherFlesh.Id, monster);

        Assert.True(attempt.Accepted, attempt.Message);
        Assert.Equal(windUp, attempt.Cast!.Value.ReleaseTick - attempt.Cast.Value.StartTick);

        scenario.AdvanceBeats(windUp - 1);
        Assert.Empty(scenario.Projectiles.InFlight);
        Assert.NotNull(scenario.Spells.CastInProgress(scenario.PlayerId));

        var released = scenario.AdvanceBeat();

        Assert.Contains(released, value => value.Kind == CombatEventKind.SpellReleased);
        Assert.Contains(released, value => value.Kind == CombatEventKind.ProjectileLaunched);
        Assert.Null(scenario.Spells.CastInProgress(scenario.PlayerId));
        Assert.Single(scenario.Projectiles.InFlight);

        scenario.AdvanceBeats(4);

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(AttackCategoryId.SpellBolts, resolver.Requests[0].AttackCategory);
    }

    [Fact]
    public void AStunnedCasterLosesTheSpellRatherThanFinishingIt()
    {
        using var scenario = Caster(new HittingResolver(), out _);
        var monster = scenario.SpawnMonster(9, 5, BehaviorProfile.Guard);

        Assert.True(Begin(scenario, SpellCatalog.WitherFlesh.Id, monster).Accepted);
        scenario.Conditions.Apply(
            scenario.PlayerId,
            monster,
            new TraumaEffect(
                TraumaEffectKind.Stun,
                Magnitude: 1,
                Duration: 2,
                DurationUnit: TraumaDurationUnit.Rounds),
            scenario.Clock.Tick);

        var events = scenario.AdvanceBeats(
            CombatClock.BeatsIn(SpellCatalog.WitherFlesh.WindUpMilliseconds) + 1);

        Assert.Contains(
            events,
            value => value.Kind == CombatEventKind.SpellFailed &&
                value.PresentationKey == "spell.interrupted");
        Assert.Empty(scenario.Projectiles.InFlight);

        // The cost is not paid by a cast that never landed.
        Assert.Equal(
            6,
            scenario.Spells.ReagentsCarried(
                scenario.PlayerId, SpellCatalog.GraveSaltTypeId));
    }

    [Fact]
    public void ANaturalOneTakesTheSpellAwayUntilTheNextDawn()
    {
        using var scenario = Caster(new MishapResolver(), out _);
        var monster = scenario.SpawnMonster(9, 5, BehaviorProfile.Guard);

        Assert.True(Begin(scenario, SpellCatalog.WitherFlesh.Id, monster).Accepted);
        var events = scenario.AdvanceBeats(
            CombatClock.BeatsIn(SpellCatalog.WitherFlesh.WindUpMilliseconds) + 4);

        var mishap = Assert.Single(
            events.Where(value => value.Kind == CombatEventKind.SpellMishap));
        Assert.Equal(scenario.PlayerId, mishap.ActorId);
        Assert.True(
            scenario.Spells.IsLockedOut(scenario.PlayerId, SpellCatalog.WitherFlesh.Id));
        Assert.Equal(
            SpellCastFailure.LockedOut,
            Begin(scenario, SpellCatalog.WitherFlesh.Id, monster).Failure);

        // Only that spell, and only until dawn.
        Assert.False(
            scenario.Spells.IsLockedOut(scenario.PlayerId, SpellCatalog.ShatterStone.Id));
        scenario.SkipTo(WorldCalendar.NextDawnTick(scenario.Clock.Tick));
        scenario.Spells.AdvanceLockouts();
        Assert.False(
            scenario.Spells.IsLockedOut(scenario.PlayerId, SpellCatalog.WitherFlesh.Id));
    }

    [Fact]
    public void CastingWithinReachOfACreatureGivesItTheOpening()
    {
        using var scenario = Caster(new HittingResolver(), out _);
        var adjacent = scenario.SpawnMonster(6, 5, BehaviorProfile.Guard);

        Assert.True(RulesRepository.Rules.Spellcasting.ProvokesInMelee);
        var attempt = scenario.Combat.TryPlayerCast(
            SpellCatalog.SpellfireDraw.Id);

        Assert.True(attempt.Accepted, attempt.Message);
        Assert.Equal([adjacent], attempt.Provoked);
        Assert.NotNull(scenario.Combat.ActiveAttackOf(adjacent));
        Assert.Contains(
            scenario.Combat.DrainImmediateEvents(),
            value => value.Kind == CombatEventKind.AttackStarted &&
                value.ActorId == adjacent);
    }

    [Fact]
    public void ABallResolvesAgainstEveryCreatureInItsRadiusAndNotTheCaster()
    {
        var resolver = new HittingResolver();
        using var scenario = Caster(resolver, out _);
        var first = scenario.SpawnMonster(6, 5, BehaviorProfile.Guard);
        var second = scenario.SpawnMonster(5, 6, BehaviorProfile.Guard);
        var far = scenario.SpawnMonster(5, 10, BehaviorProfile.Guard);

        Assert.True(Begin(scenario, SpellCatalog.SpellfireDraw.Id).Accepted);
        scenario.AdvanceBeats(
            CombatClock.BeatsIn(SpellCatalog.SpellfireDraw.WindUpMilliseconds) + 2);

        // Two creatures stood inside the radius and one outside it, and the
        // caster is never in their number however close the burst is.
        Assert.Equal(2, resolver.Calls);
        Assert.Equal(AttackCategoryId.SpellBalls, resolver.Requests[0].AttackCategory);
        Assert.Equal(11, scenario.Objects.Get(first).Health);
        Assert.Equal(11, scenario.Objects.Get(second).Health);
        Assert.Equal(12, scenario.Objects.Get(far).Health);
        Assert.Equal(20, scenario.Objects.Get(scenario.PlayerId).Health);
    }

    [Fact]
    public void ACastInProgressAndItsLockoutSurviveSaveAndLoad()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var world = PlayableSliceWorld.CreateDemo(
                20260801, new HittingResolver());
            var rat = world.Monsters.First();
            var placed = world.Transfers.Execute(new ObjectTransferRequest(
                world.PlayerId,
                world.Player.Location,
                ObjectLocation.OnMap(
                    rat.Location.MapId,
                    rat.Location.Position with
                    {
                        X = rat.Location.Position.X -
                            (2 * PlayableSliceWorld.WorldUnitsPerTile),
                    })));
            Assert.True(placed.Succeeded, placed.Message);

            var attempt = world.Combat.TryPlayerCast(
                SpellCatalog.WitherFlesh.Id, rat.Id);
            Assert.True(attempt.Accepted, attempt.Message);
            var lockout = world.Spells.RecordMishap(
                world.PlayerId, SpellCatalog.ShatterStone.Id);

            Assert.True(world.RequestSave(path).Succeeded);
            using var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal(
                world.Spells.CastInProgress(world.PlayerId),
                loaded.Spells.CastInProgress(loaded.PlayerId));
            Assert.Equal([lockout], loaded.Spells.Lockouts);
            Assert.True(
                loaded.Spells.IsLockedOut(
                    loaded.PlayerId, SpellCatalog.ShatterStone.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EveryAuthoredSpellIsValidContentWithItsOwnProjectile()
    {
        Assert.NotEmpty(SpellCatalog.All);
        foreach (var spell in SpellCatalog.All)
        {
            spell.Validate();
            Assert.True(
                ProjectileCatalog.Default.TryGet(spell.ProjectileId, out var projectile));
            Assert.Equal(spell.Attack.Category, projectile.Attack.Category);
            Assert.True(
                SpellCatalog.TrySpellForProjectile(spell.ProjectileId, out var round));
            Assert.Equal(spell.Id, round.Id);
        }
    }

    private static SpellCastAttempt Begin(
        CombatScenario scenario,
        string spellId,
        ObjectId targetId = default,
        Vec3i? aim = null) =>
        scenario.Spells.Begin(scenario.PlayerId, spellId, targetId, aim);

    private static CombatScenario Caster(
        IAttackRulesResolver resolver,
        out ObjectId reagentStack,
        int reagents = 6)
    {
        var scenario = new CombatScenario(resolver);
        scenario.PlacePlayer(5, 5);
        reagentStack = scenario.GiveStack(
            scenario.PlayerId,
            SpellCatalog.GraveSaltTypeId,
            SpellCatalog.GraveSaltName,
            reagents);
        return scenario;
    }

    private sealed class HittingResolver : IAttackRulesResolver
    {
        public int Calls { get; private set; }

        public List<AttackRequest> Requests { get; } = [];

        public AttackResult Resolve(AttackRequest request)
        {
            Calls++;
            Requests.Add(request);
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = 1,
                Mishap = false,
            };
        }
    }

    private sealed class MishapResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = false,
            RawD20 = 1,
            NetRoll = 1,
            Margin = -10,
            ConcussionHits = 0,
            Mishap = true,
            Messages = ["the spell turns in the hand"],
        };
    }
}
