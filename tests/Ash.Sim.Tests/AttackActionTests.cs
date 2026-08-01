using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class AttackActionTests
{
    private const int PhysicsTicksPerBeat = 12;

    [Fact]
    public void PlayerAttackHasANonZeroWindUpAndResolvesAtTheExactImpactBeat()
    {
        var resolver = new CountingResolver(damage: 1);
        using var world = MeleeWorld(resolver);
        var rat = Rat(world);
        var health = rat.Health;

        Assert.True(world.AttackAdjacentMonster().Succeeded);
        Assert.Contains(
            world.LastCombatEvents,
            value => value.Kind == CombatEventKind.AttackStarted);
        var action = Assert.IsType<AttackAction>(world.Combat.ActiveAttackOf(world.PlayerId));
        Assert.Equal(world.Clock.Tick, action.StartTick);
        Assert.Equal(action.StartTick + 1, action.ImpactTick);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(health, world.Objects.Get(rat.Id).Health);

        var ticksUntilImpact = world.Clock.PhysicsTicksPerBeat -
            (int)(world.Physics.Tick % world.Clock.PhysicsTicksPerBeat);
        for (var tick = 0; tick < ticksUntilImpact - 1; tick++)
        {
            world.AdvancePhysics();
        }
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(health, world.Objects.Get(rat.Id).Health);

        var events = AdvanceOneBeat(world);
        Assert.Equal(1, resolver.Calls);
        Assert.Equal(health - 1, world.Objects.Get(rat.Id).Health);
        Assert.Contains(events, value => value.Kind == CombatEventKind.Swing);
        Assert.Equal(AttackActionPhase.Resolved,
            world.Combat.LastAttackOf(world.PlayerId)?.Phase);
    }

    [Fact]
    public void PlayerAndAiImpactsUseTheSameInjectedRulesBoundaryOnceEach()
    {
        var resolver = new RecordingResolver();
        using var world = MeleeWorld(resolver, seed: 7);
        var rat = Rat(world);
        Assert.True(world.ToggleRightHand().Succeeded);

        var aiImpact = AdvanceUntilSwing(world, rat.Id, maxBeats: 40);

        Assert.Equal(rat.Id, aiImpact.ActorId);
        Assert.Single(resolver.Requests);
        Assert.Equal(
            AttackCategoryId.ToothAndClaw,
            resolver.Requests[0].AttackCategory);
        Assert.Equal(
            AttackActionPhase.Resolved,
            world.Combat.LastAttackOf(rat.Id)?.Phase);

        Assert.True(world.AttackAdjacentMonster().Succeeded);
        var playerImpact = AdvanceUntilSwing(world, world.PlayerId, maxBeats: 2);

        Assert.Equal(world.PlayerId, playerImpact.ActorId);
        Assert.Equal(2, resolver.Requests.Count);
        Assert.Equal(
            AttackCategoryId.OneHandedSlashing,
            resolver.Requests[1].AttackCategory);
        Assert.Equal(
            AttackActionPhase.Resolved,
            world.Combat.LastAttackOf(world.PlayerId)?.Phase);
    }

    [Fact]
    public void LeavingReachWhiffsAndNeverCallsTheResolver()
    {
        var resolver = new CountingResolver(damage: 1);
        using var world = MeleeWorld(resolver);

        Assert.True(world.AttackAdjacentMonster().Succeeded);
        Assert.True(world.MovePlayer(-1, 0).Succeeded);
        var events = AdvanceOneBeat(world);

        Assert.Equal(0, resolver.Calls);
        Assert.Contains(events, value => value.Kind == CombatEventKind.Whiffed);
        Assert.Equal(AttackActionPhase.Whiffed,
            world.Combat.LastAttackOf(world.PlayerId)?.Phase);
        Assert.False(world.Combat.PlayerCanSwing);
    }

    [Fact]
    public void ChangingTheWeaponDuringWindUpInterruptsTheAttack()
    {
        var resolver = new CountingResolver(damage: 1);
        using var world = MeleeWorld(resolver);
        Assert.True(world.ToggleRightHand().Succeeded);

        Assert.True(world.AttackAdjacentMonster().Succeeded);
        Assert.True(world.ToggleRightHand().Succeeded);
        var events = AdvanceOneBeat(world);

        Assert.Equal(0, resolver.Calls);
        Assert.Contains(events, value => value.Kind == CombatEventKind.Interrupted);
        Assert.Equal(AttackActionPhase.Interrupted,
            world.Combat.LastAttackOf(world.PlayerId)?.Phase);
    }

    [Fact]
    public void SameBeatImpactsResolveByAttackerIdAndCancelAKilledAttacker()
    {
        var resolver = new CountingResolver(damage: 100);
        using var world = MeleeWorld(resolver);
        var rat = Rat(world);
        Assert.True(world.PlayerId.CompareTo(rat.Id) < 0);

        world.Combat.ScheduleAttackIntent(world.PlayerId, rat.Id);
        world.Combat.ScheduleAttackIntent(rat.Id, world.PlayerId);
        var events = AdvanceOneBeat(world);

        Assert.Equal(1, resolver.Calls);
        Assert.Equal(
            [CombatEventKind.Swing, CombatEventKind.Interrupted],
            events.Where(value => value.Kind is CombatEventKind.Swing or CombatEventKind.Interrupted)
                .Select(value => value.Kind));
        Assert.Equal(AttackActionPhase.Resolved,
            world.Combat.LastAttackOf(world.PlayerId)?.Phase);
        Assert.Equal(AttackActionPhase.Interrupted,
            world.Combat.LastAttackOf(rat.Id)?.Phase);
    }

    [Fact]
    public void IdenticalStateAndInputTimingProduceIdenticalImpactState()
    {
        var leftResolver = new CountingResolver(damage: 1);
        var rightResolver = new CountingResolver(damage: 1);
        using var left = MeleeWorld(leftResolver);
        using var right = MeleeWorld(rightResolver);

        Assert.True(left.AttackAdjacentMonster().Succeeded);
        Assert.True(right.AttackAdjacentMonster().Succeeded);
        var leftEvents = AdvanceOneBeat(left);
        var rightEvents = AdvanceOneBeat(right);

        Assert.Equal(left.Combat.LastAttackOf(left.PlayerId),
            right.Combat.LastAttackOf(right.PlayerId));
        Assert.Equal(Rat(left).Injury, Rat(right).Injury);
        Assert.Equal(left.Dice.State, right.Dice.State);
        Assert.Equal(leftEvents, rightEvents);
    }

    [Fact]
    public void ImpactEventsRetainTheCompleteResolverEvidenceAndAppliedCondition()
    {
        using var world = MeleeWorld(new CriticalResolver());
        var rat = Rat(world);
        Assert.True(world.ToggleRightHand().Succeeded);

        Assert.True(world.AttackAdjacentMonster().Succeeded);
        var events = AdvanceOneBeat(world);

        var impact = Assert.Single(events, value =>
            value.Kind == CombatEventKind.Impact && value.ActorId == world.PlayerId);
        var evidence = Assert.IsType<CombatResolutionEvidence>(impact.Resolution);
        Assert.Equal(world.PlayerId, impact.ActorId);
        Assert.Equal(rat.Id, impact.TargetId);
        Assert.Equal(world.Sheets.For(world.PlayerId).Weapon, evidence.WeaponId);
        Assert.Equal(AttackCategoryId.OneHandedSlashing, evidence.AttackCategory);
        Assert.Equal(17, evidence.NetRoll);
        Assert.Equal(ArmorType.None, evidence.Armor);
        Assert.Equal(CriticalTier.C, evidence.CriticalTier);
        Assert.Equal(CriticalTableId.Slash, evidence.CriticalTable);
        Assert.Equal("A testing critical.", evidence.TraumaText);
        Assert.Contains(evidence.AppliedEffects,
            value => value.Kind == TraumaEffectKind.Stun);
        Assert.Contains("resolver trace", evidence.CalculationTrace);
        Assert.Contains("d20", impact.CalculationText);
        Assert.Contains(events, value =>
            value.Kind == CombatEventKind.Critical &&
            value.PresentationKey == "attack.critical");
        Assert.Contains(events, value =>
            value.Kind == CombatEventKind.ConditionApplied &&
            value.EffectKind == TraumaEffectKind.Stun &&
            value.PresentationKey == "condition.applied");
        Assert.True(world.Conditions.Has(rat.Id, TraumaEffectKind.Stun));
    }

    [Fact]
    public void CombatHistoryIsBoundedAndKeepsDeterministicSequenceOrder()
    {
        using var world = MeleeWorld(new CountingResolver(damage: 0));
        var rat = Rat(world);

        for (var attack = 0; attack < PlayableSliceWorld.CombatHistoryCapacity; attack++)
        {
            world.Combat.ScheduleAttackIntent(world.PlayerId, rat.Id);
            _ = world.Combat.DrainImmediateEvents();
            _ = AdvanceOneBeat(world);
        }

        Assert.Equal(PlayableSliceWorld.CombatHistoryCapacity, world.CombatHistory.Count);
        Assert.True(world.CombatHistory[0].Sequence > 1);
        Assert.All(
            world.CombatHistory.Zip(world.CombatHistory.Skip(1)),
            pair => Assert.Equal(pair.First.Sequence + 1, pair.Second.Sequence));
        Assert.Contains(world.CombatHistory,
            value => value.Kind == CombatEventKind.Impact);
    }

    [Fact]
    public void MidWindUpSaveResumesTheExactImpactRecoveryAndFutureRolls()
    {
        var firstPath = Path.GetTempFileName();
        var secondPath = Path.GetTempFileName();
        try
        {
            using var original = MeleeWorld(new RulesAttackRulesResolver());
            Assert.True(original.ToggleRightHand().Succeeded);
            Assert.True(original.AttackAdjacentMonster().Succeeded);
            var expectedAction = Assert.IsType<AttackAction>(
                original.Combat.ActiveAttackOf(original.PlayerId));
            var expectedCooldown = original.Combat.PlayerCooldownRemainingMilliseconds;

            Assert.True(original.RequestSave(firstPath).Succeeded);
            using var loaded = PlayableSliceWorld.Load(firstPath);

            Assert.Equal(expectedAction, loaded.Combat.ActiveAttackOf(loaded.PlayerId));
            Assert.Equal(expectedCooldown, loaded.Combat.PlayerCooldownRemainingMilliseconds);
            Assert.Equal(original.Dice.State, loaded.Dice.State);

            Assert.True(loaded.RequestSave(secondPath).Succeeded);
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));

            var originalEvents = AdvanceOneBeat(original)
                .Select(value => value with { Sequence = 0 })
                .ToArray();
            var loadedEvents = AdvanceOneBeat(loaded)
                .Select(value => value with { Sequence = 0 })
                .ToArray();

            Assert.Equal(originalEvents, loadedEvents);
            Assert.Equal(Rat(original).Injury, Rat(loaded).Injury);
            Assert.Equal(original.Dice.State, loaded.Dice.State);
            Assert.Equal(
                original.Combat.PlayerCooldownRemainingMilliseconds,
                loaded.Combat.PlayerCooldownRemainingMilliseconds);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private static PlayableSliceWorld MeleeWorld(
        IAttackRulesResolver resolver,
        ulong seed = 20260731)
    {
        var world = PlayableSliceWorld.CreateDemo(seed, resolver);
        var rat = Rat(world);
        var destination = rat.Location.Position with
        {
            X = rat.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
        };
        var moved = world.Transfers.Execute(new ObjectTransferRequest(
            world.PlayerId,
            world.Player.Location,
            ObjectLocation.OnMap(rat.Location.MapId, destination)));
        Assert.True(moved.Succeeded, moved.Message);
        return world;
    }

    private static WorldObject Rat(PlayableSliceWorld world) =>
        world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);

    private static IReadOnlyList<CombatEvent> AdvanceOneBeat(PlayableSliceWorld world)
    {
        var start = world.Clock.Tick;
        IReadOnlyList<CombatEvent> events = [];
        for (var tick = 0; tick <= PhysicsTicksPerBeat && world.Clock.Tick == start; tick++)
        {
            world.AdvancePhysics();
            events = world.LastCombatEvents;
        }
        return events;
    }

    private static CombatEvent AdvanceUntilSwing(
        PlayableSliceWorld world,
        ObjectId attackerId,
        int maxBeats)
    {
        for (var beat = 0; beat < maxBeats; beat++)
        {
            var impact = AdvanceOneBeat(world)
                .FirstOrDefault(value =>
                    value.Kind == CombatEventKind.Swing &&
                    value.ActorId == attackerId);
            if (impact.Kind == CombatEventKind.Swing)
            {
                return impact;
            }
        }

        throw new InvalidOperationException(
            $"No impact from {attackerId} occurred within {maxBeats} beats.");
    }

    private sealed class CountingResolver(int damage) : IAttackRulesResolver
    {
        public int Calls { get; private set; }

        public AttackResult Resolve(AttackRequest request)
        {
            Calls++;
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = damage,
                Mishap = false,
            };
        }
    }

    private sealed class RecordingResolver : IAttackRulesResolver
    {
        public List<AttackRequest> Requests { get; } = [];

        public AttackResult Resolve(AttackRequest request)
        {
            Requests.Add(request);
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = 0,
                Mishap = false,
            };
        }
    }

    private sealed class CriticalResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = true,
            RawD20 = request.RawD20,
            NetRoll = 17,
            Margin = 6,
            ConcussionHits = 2,
            CriticalTier = CriticalTier.C,
            CriticalTable = CriticalTableId.Slash,
            TraumaText = "A testing critical.",
            TraumaEffects =
            [
                new TraumaEffect(
                    TraumaEffectKind.Stun,
                    Magnitude: 1,
                    Duration: 1,
                    DurationUnit: TraumaDurationUnit.Rounds),
            ],
            Mishap = false,
            Messages = ["resolver trace"],
        };
    }
}
