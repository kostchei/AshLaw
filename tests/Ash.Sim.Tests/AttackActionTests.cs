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

    private static PlayableSliceWorld MeleeWorld(IAttackRulesResolver resolver)
    {
        var world = PlayableSliceWorld.CreateDemo(attackResolver: resolver);
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
}
