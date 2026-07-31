namespace Ash.Rules.Tests;

public sealed class InjuryTests
{
    private static VitalityData Data { get; } =
        VitalityLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                VitalityLoader.FileName));

    private static AbilityBonusTable Bonuses { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName))
            .AbilityBonuses;

    private static AbilityScores Scores(int constitution = 10) =>
        new(10, 10, constitution, 10, 10, 10);

    private static InjuryState Whole(int concussion = 10, int wounds = 4) =>
        InjuryState.Whole(concussion, wounds);

    [Fact]
    public void ConcussionHitsAbsorbDamageBeforeWoundsDo()
    {
        var hurt = Injury.Damage(Whole(), 6, new Dice(1));

        Assert.Equal(6, hurt.ConcussionLost);
        Assert.Equal(0, hurt.WoundsLost);
        Assert.Equal(4, hurt.State.Concussion);
        Assert.Equal(4, hurt.State.Wounds);
        Assert.Equal(AbilityMask.None, hurt.State.Impairments);
        Assert.True(hurt.State.IsUpright);
    }

    [Fact]
    public void DamagePastTheHitsSpillsIntoWoundsOneForOne()
    {
        var hurt = Injury.Damage(Whole(concussion: 3), 5, new Dice(1));

        Assert.Equal(3, hurt.ConcussionLost);
        Assert.Equal(2, hurt.WoundsLost);
        Assert.Equal(0, hurt.State.Concussion);
        Assert.Equal(2, hurt.State.Wounds);
    }

    [Fact]
    public void EveryLostWoundCostsTheUseOfAnAbility()
    {
        var hurt = Injury.Damage(Whole(concussion: 0), 3, new Dice(20260731));

        Assert.Equal(3, hurt.WoundsLost);
        Assert.NotEqual(AbilityMask.None, hurt.State.Impairments);
        Assert.Equal(hurt.NewImpairments, hurt.State.Impairments);
        Assert.InRange(hurt.State.Impairments.Count(), 1, 3);
    }

    [Fact]
    public void RunningOutOfWoundsStartsTheDeathClockRatherThanEndingIt()
    {
        var hurt = Injury.Damage(Whole(concussion: 1), 99, new Dice(5));

        Assert.True(hurt.EnteredDeathClock);
        Assert.True(hurt.State.IsOnTheDeathClock);
        Assert.False(hurt.State.IsDead);
        Assert.Equal(0, hurt.State.Wounds);
    }

    [Fact]
    public void ABodyWithNoWoundLayerSimplyDies()
    {
        // A creature that was never given the wound layer has nothing between
        // its last concussion hit and the end. That is what the layer buys.
        var hurt = Injury.Damage(Whole(concussion: 2, wounds: 0), 2, new Dice(5));

        Assert.True(hurt.State.IsDead);
        Assert.False(hurt.EnteredDeathClock);
    }

    [Fact]
    public void ABodyWithNoWoundLayerStaysUpWhileItHasHitsLeft()
    {
        var hurt = Injury.Damage(Whole(concussion: 2, wounds: 0), 1, new Dice(5));

        Assert.True(hurt.State.IsUpright);
        Assert.Equal(1, hurt.State.Concussion);
    }

    [Fact]
    public void DamagingADeadBodyIsRefused()
    {
        var dead = Injury.Damage(Whole(concussion: 1, wounds: 0), 1, new Dice(3));

        Assert.Throws<InvalidOperationException>(
            () => Injury.Damage(dead.State, 1, new Dice(3)));
    }

    [Fact]
    public void ThreeFailuresEndTheDeathClock()
    {
        var state = Dying();
        var outcome = default(DeathSaveOutcome);

        // Constitution 3 is -4 against a DC of 10: this body needs a 14 or
        // better every round, so three failures arrive long before twelve
        // rounds do.
        var dice = new Dice(11);
        for (var round = 0; round < 12 && !state.IsDead; round++)
        {
            outcome = Injury.RollDeathSave(
                Data,
                state,
                dice,
                Scores(constitution: 3),
                Bonuses);
            state = outcome.State;
        }

        Assert.Equal(DeathClockOutcome.Died, outcome.Outcome);
        Assert.True(state.IsDead);
        Assert.Equal(3, state.DeathSaveFailures);
    }

    [Fact]
    public void SurvivingTheClockCostsTwoMoreAbilities()
    {
        // The wound that started the clock impaired something; clearing it
        // keeps this test about stabilising rather than about which ability the
        // dice happened to take, which ImpairedConstitutionRolls... covers.
        var state = Dying() with { Impairments = AbilityMask.None };
        var outcome = default(DeathSaveOutcome);

        // One generator across the rounds, as a real fight would have: this
        // seed gives three successes before a third failure at constitution 20.
        var dice = new Dice(1);
        for (var round = 0; round < 40 && state.IsOnTheDeathClock; round++)
        {
            outcome = Injury.RollDeathSave(
                Data,
                state,
                dice,
                Scores(constitution: 20),
                Bonuses);
            state = outcome.State;
        }

        Assert.Equal(DeathClockOutcome.Stabilised, outcome.Outcome);
        Assert.Equal(VitalityState.Stable, state.State);

        // Still out of wounds: stabilising is not standing up.
        Assert.Equal(0, state.Wounds);
        Assert.InRange(outcome.NewImpairments.Count(), 1, 2);
    }

    [Fact]
    public void ABodyThatIsNotOnTheClockCannotRollItsSaves()
    {
        Assert.Throws<InvalidOperationException>(
            () => Injury.RollDeathSave(
                Data,
                Whole(),
                new Dice(1),
                Scores(),
                Bonuses));
    }

    [Fact]
    public void APotionRestoresAWoundAndSpendsTheRestOnHits()
    {
        var hurt = Injury.Damage(Whole(concussion: 4), 6, new Dice(1)).State;
        Assert.Equal(0, hurt.Concussion);
        Assert.Equal(2, hurt.Wounds);

        var healed = Injury.Heal(Data, hurt, 5);

        Assert.Equal(1, healed.WoundsRestored);
        Assert.Equal(4, healed.ConcussionRestored);
        Assert.Equal(3, healed.State.Wounds);
        Assert.Equal(4, healed.State.Concussion);
    }

    [Fact]
    public void HealingTakesABodyOffTheDeathClockAndClearsItsSaves()
    {
        var dying = Injury.RollDeathSave(
            Data,
            Dying(),
            new Dice(11),
            Scores(constitution: 3),
            Bonuses).State;
        Assert.True(dying.DeathSaveFailures > 0 || dying.DeathSaveSuccesses > 0);

        var healed = Injury.Heal(Data, dying, 3);

        Assert.True(healed.State.IsUpright);
        Assert.Equal(0, healed.State.DeathSaveFailures);
        Assert.Equal(0, healed.State.DeathSaveSuccesses);
        Assert.Equal(1, healed.State.Wounds);
    }

    [Fact]
    public void ImpairmentLiftsOnlyWhenEveryWoundIsBack()
    {
        var hurt = Injury.Damage(Whole(concussion: 0, wounds: 2), 2, new Dice(3)).State;
        Assert.NotEqual(AbilityMask.None, hurt.Impairments);

        var partly = Injury.Heal(Data, hurt, 1);
        Assert.False(partly.ClearedImpairments);
        Assert.NotEqual(AbilityMask.None, partly.State.Impairments);

        var whole = Injury.Heal(Data, partly.State, 1);
        Assert.True(whole.ClearedImpairments);
        Assert.Equal(AbilityMask.None, whole.State.Impairments);
    }

    [Fact]
    public void HealingDoesNotRaiseTheDead()
    {
        var dead = Injury.Damage(Whole(concussion: 1, wounds: 0), 1, new Dice(3)).State;

        Assert.Throws<InvalidOperationException>(
            () => Injury.Heal(Data, dead, 5));
    }

    [Fact]
    public void ADayGivesBackAWoundAndTheDiceTheCareAllows()
    {
        var hurt = Injury.Damage(Whole(concussion: 8, wounds: 4), 10, new Dice(1)).State;
        Assert.Equal(0, hurt.Concussion);
        Assert.Equal(2, hurt.Wounds);

        var day = Injury.PassDay(
            Data,
            hurt,
            new Dice(2024),
            CharacterClass.Fighter,
            level: 5,
            Scores(constitution: 14),
            Bonuses,
            CareLevel.Treated,
            fullRest: true,
            medicalAttention: true);

        Assert.Equal(1, day.WoundsRestored);
        Assert.Equal(3, day.State.Wounds);

        // Level 5 lifts the daily cap to all three dice.
        Assert.Equal(3, day.RecoveryDice.Count);
        Assert.True(day.ConcussionRestored > 0);
    }

    [Fact]
    public void ALevelOneCharacterSpendsOneDieHoweverWellTendedTheyAre()
    {
        var hurt = Injury.Damage(Whole(concussion: 8, wounds: 4), 4, new Dice(1)).State;

        var day = Injury.PassDay(
            Data,
            hurt,
            new Dice(31),
            CharacterClass.Fighter,
            level: 1,
            Scores(constitution: 14),
            Bonuses,
            CareLevel.Treated,
            fullRest: true,
            medicalAttention: true);

        Assert.Single(day.RecoveryDice);
    }

    [Fact]
    public void AnUnwoundedDayIsNeverCheckedForInfection()
    {
        var bruised = Injury.Damage(Whole(concussion: 8, wounds: 4), 3, new Dice(1)).State;
        Assert.Equal(4, bruised.Wounds);

        var day = Injury.PassDay(
            Data,
            bruised,
            new Dice(5),
            CharacterClass.Fighter,
            level: 3,
            Scores(constitution: 12),
            Bonuses,
            CareLevel.Adventuring,
            fullRest: false,
            medicalAttention: false);

        Assert.Null(day.InfectionSave);
        Assert.Equal(0, day.InfectionDamage);
    }

    [Fact]
    public void CarryingAWoundThroughADayRisksAComplication()
    {
        var hurt = Injury.Damage(Whole(concussion: 8, wounds: 4), 10, new Dice(1)).State;

        // Constitution 3 against the adventuring DC of 15 fails on all but a
        // natural 19 or 20, so a complication is close to certain.
        var failures = 0;
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var day = Injury.PassDay(
                Data,
                hurt,
                new Dice(seed),
                CharacterClass.Fighter,
                level: 2,
                Scores(constitution: 3),
                Bonuses,
                CareLevel.Adventuring,
                fullRest: false,
                medicalAttention: false);

            Assert.NotNull(day.InfectionSave);
            if (day.InfectionDamage > 0)
            {
                failures++;
                Assert.InRange(day.InfectionDamage, 1, 6);
            }
        }

        Assert.True(failures > 10, $"only {failures} of 20 days went wrong.");
    }

    [Fact]
    public void ADayConsumesTheSameRollsWhateverStateItStartsIn()
    {
        // A day rolls every die the care offers even when the body is nearly
        // whole, so a saved world resumes the sequence it would have rolled.
        var nearlyWhole = Injury.Damage(Whole(concussion: 8, wounds: 4), 1, new Dice(1)).State;
        var badlyHurt = Injury.Damage(Whole(concussion: 8, wounds: 4), 7, new Dice(1)).State;

        var first = new Dice(777);
        var second = new Dice(777);
        Injury.PassDay(
            Data,
            nearlyWhole,
            first,
            CharacterClass.Cleric,
            3,
            Scores(constitution: 12),
            Bonuses,
            CareLevel.Rested,
            fullRest: true,
            medicalAttention: false);
        Injury.PassDay(
            Data,
            badlyHurt,
            second,
            CharacterClass.Cleric,
            3,
            Scores(constitution: 12),
            Bonuses,
            CareLevel.Rested,
            fullRest: true,
            medicalAttention: false);

        Assert.Equal(first.State, second.State);
    }

    [Fact]
    public void ADyingBodyCannotRestADayOut()
    {
        Assert.Throws<InvalidOperationException>(
            () => Injury.PassDay(
                Data,
                Dying(),
                new Dice(1),
                CharacterClass.Fighter,
                1,
                Scores(),
                Bonuses,
                CareLevel.Treated,
                fullRest: true,
                medicalAttention: true));
    }

    [Fact]
    public void ImpairedConstitutionRollsTheDeathClockAtDisadvantage()
    {
        var dying = Dying() with { Impairments = AbilityMask.Constitution };
        var save = Injury.RollDeathSave(
            Data,
            dying,
            new Dice(12345),
            Scores(constitution: 12),
            Bonuses);

        Assert.True(save.Check.HadDisadvantage);
        Assert.Equal(2, save.Check.Rolls.Count);
        Assert.Equal(save.Check.Rolls.Min(), save.Check.Roll);
    }

    /// <summary>A body beaten out of both layers, with the clock untouched.</summary>
    private static InjuryState Dying() =>
        Injury.Damage(Whole(concussion: 1, wounds: 1), 99, new Dice(5)).State;
}
