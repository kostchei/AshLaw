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
    public void ReapplyingTheSameConditionFromOneSourceReplacesIt()
    {
        var conditions = new ActorConditionService();
        var target = ObjectId.None;
        var source = ObjectId.None;
        conditions.Apply(target, source, new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 2), 1);
        conditions.Apply(target, source, new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 4), 2);

        var bleeding = Assert.Single(conditions.Of(target));
        Assert.Equal(4, bleeding.Magnitude);
        Assert.Equal(2, bleeding.AppliedTick);
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
