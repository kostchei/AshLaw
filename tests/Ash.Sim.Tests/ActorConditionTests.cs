using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class ActorConditionTests
{
    [Fact]
    public void TimedConditionPreventsActionsUntilItsExactExpiryBeat()
    {
        var conditions = new ActorConditionService();
        var target = ObjectId.None;
        var source = ObjectId.None;
        conditions.Apply(target, source, new TraumaEffect(
            TraumaEffectKind.Stun,
            Duration: 1,
            DurationUnit: TraumaDurationUnit.Rounds), now: 10);

        Assert.True(conditions.PreventsAction(target));
        conditions.AdvanceTo(10 + ConditionTiming.BeatsPerRound - 1);
        Assert.True(conditions.PreventsAction(target));
        conditions.AdvanceTo(10 + ConditionTiming.BeatsPerRound);
        Assert.False(conditions.PreventsAction(target));
    }

    [Fact]
    public void ReapplyingBleedingStacksMagnitudeWithoutPostponingItsTick()
    {
        var conditions = new ActorConditionService();
        var target = ObjectId.None;
        var source = ObjectId.None;
        conditions.Apply(target, source, new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 2), 1);
        conditions.Apply(target, source, new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 4), 2);

        var bleeding = Assert.Single(conditions.Of(target));
        Assert.Equal(6, bleeding.Magnitude);
        Assert.Equal(2, bleeding.AppliedTick);
        Assert.Equal(1 + ConditionTiming.BeatsPerRound, bleeding.NextPeriodicTick);
        Assert.Equal(
            ActorConditionStackingPolicy.StackMagnitudeBySource,
            bleeding.StackingPolicy);
        Assert.Equal(ActorConditionRemovalPolicy.UntilHealed, bleeding.RemovalPolicy);
    }

    [Fact]
    public void TimedReplacementAndAnatomicalInjuriesFollowExplicitPolicies()
    {
        var conditions = new ActorConditionService();
        var actor = ObjectId.None;
        var source = ObjectId.None;
        conditions.Apply(actor, source, new TraumaEffect(
            TraumaEffectKind.Stun,
            Duration: 1,
            DurationUnit: TraumaDurationUnit.Rounds), 4);
        conditions.Apply(actor, source, new TraumaEffect(
            TraumaEffectKind.Stun,
            Duration: 2,
            DurationUnit: TraumaDurationUnit.Rounds), 5);
        conditions.Apply(actor, source, new TraumaEffect(
            TraumaEffectKind.BreakBone,
            DurationUnit: TraumaDurationUnit.UntilHealed,
            Detail: "weapon arm"), 6);
        conditions.Apply(actor, source, new TraumaEffect(
            TraumaEffectKind.BreakBone,
            DurationUnit: TraumaDurationUnit.UntilHealed,
            Detail: "lower leg"), 7);

        var active = conditions.Of(actor);
        Assert.Equal(3, active.Count);
        var stun = Assert.Single(active.Where(value => value.Kind == TraumaEffectKind.Stun));
        Assert.Equal(5 + 2 * ConditionTiming.BeatsPerRound, stun.ExpiresAtTick);
        Assert.Equal(ActorConditionRemovalPolicy.Timed, stun.RemovalPolicy);
        Assert.Equal(2, active.Count(value => value.Kind == TraumaEffectKind.BreakBone));
        Assert.Equal(3, conditions.MovementStepsPerRound(actor, 6));
    }

    [Fact]
    public void OneUseEffectsConsumeOnceAndHealingClearsLastingHarm()
    {
        var conditions = new ActorConditionService();
        var actor = ObjectId.None;
        conditions.Apply(actor, actor, new TraumaEffect(TraumaEffectKind.Sap), 0);
        conditions.Apply(actor, actor, new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 2), 0);
        conditions.Apply(actor, actor, new TraumaEffect(TraumaEffectKind.Injured), 0);

        Assert.True(conditions.Consume(actor, TraumaEffectKind.Sap));
        Assert.False(conditions.Consume(actor, TraumaEffectKind.Sap));
        Assert.Equal(2, conditions.RemoveHealed(actor));
        Assert.Empty(conditions.Of(actor));
    }

    [Fact]
    public void SuffocationTicksAsExhaustionWhileBleedingTicksAsDamage()
    {
        var conditions = new ActorConditionService();
        var actor = ObjectId.None;
        conditions.Apply(actor, actor, new TraumaEffect(
            TraumaEffectKind.Bleeding, Magnitude: 2), 0);
        conditions.Apply(actor, actor, new TraumaEffect(
            TraumaEffectKind.Suffocating, Magnitude: 1), 0);

        var before = conditions.AdvanceTo(ConditionTiming.BeatsPerRound - 1);
        Assert.Empty(before.PeriodicEffects);
        var due = conditions.AdvanceTo(ConditionTiming.BeatsPerRound);
        Assert.Collection(
            due.PeriodicEffects,
            effect =>
            {
                Assert.Equal(TraumaEffectKind.Bleeding, effect.Kind);
                Assert.Equal(2, effect.Magnitude);
            },
            effect =>
            {
                Assert.Equal(TraumaEffectKind.Suffocating, effect.Kind);
                Assert.Equal(1, effect.Magnitude);
            });
    }

    [Fact]
    public void ProneInjuryAndSlowReduceMovementWhileRestraintStopsIt()
    {
        var conditions = new ActorConditionService();
        var actor = ObjectId.None;
        conditions.Apply(actor, actor, new TraumaEffect(TraumaEffectKind.Prone), 0);
        Assert.Equal(3, conditions.MovementStepsPerRound(actor, 6));

        conditions.Apply(actor, actor, new TraumaEffect(TraumaEffectKind.Slow, Magnitude: 10), 0);
        Assert.Equal(1, conditions.MovementStepsPerRound(actor, 6));

        conditions.Apply(actor, actor, new TraumaEffect(TraumaEffectKind.Restrained), 0);
        Assert.Equal(0, conditions.MovementStepsPerRound(actor, 6));
    }
}
