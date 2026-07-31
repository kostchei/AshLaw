namespace Ash.Rules.Tests;

public sealed class VaultCheckTests
{
    [Fact]
    public void TheHeightIsTwiceTheCheckPlusSix()
    {
        Assert.Equal(8, VaultCheck.StepHeightFor(1));
        Assert.Equal(26, VaultCheck.StepHeightFor(10));
        Assert.Equal(56, VaultCheck.StepHeightFor(25));
    }

    [Fact]
    public void TheBoundsAreExactlyWhatTheCheckCanReach()
    {
        // A check runs 1 to 25 across the d20 and the bonus table's +5 ceiling,
        // and those map to exactly the stated 8 and 56. The clamp is only there
        // for the negative checks a feeble creature can roll.
        Assert.Equal(VaultCheck.MinimumStepHeight, VaultCheck.StepHeightFor(1));
        Assert.Equal(VaultCheck.MaximumStepHeight, VaultCheck.StepHeightFor(25));
        Assert.Equal(VaultCheck.MinimumStepHeight, VaultCheck.StepHeightFor(-3));
        Assert.Equal(VaultCheck.MaximumStepHeight, VaultCheck.StepHeightFor(99));
    }

    [Fact]
    public void TheBetterOfStrengthAndDexterityCarriesTheAttempt()
    {
        Assert.Equal(
            Ability.Strength,
            VaultCheck.GoverningAbility(Scores(strength: 16, dexterity: 10)));
        Assert.Equal(
            Ability.Dexterity,
            VaultCheck.GoverningAbility(Scores(strength: 10, dexterity: 16)));

        // A tie goes to dexterity, so a thief keeps the advantage that is the
        // point of being one.
        Assert.Equal(
            Ability.Dexterity,
            VaultCheck.GoverningAbility(Scores(strength: 14, dexterity: 14)));
    }

    [Fact]
    public void OnlyAThiefRollingDexterityGetsAdvantage()
    {
        Assert.True(
            VaultCheck.HasAdvantage(CharacterClass.Rogue, Ability.Dexterity));

        // A thief heaving at something with raw strength is just a person
        // heaving at something.
        Assert.False(
            VaultCheck.HasAdvantage(CharacterClass.Rogue, Ability.Strength));
        Assert.False(
            VaultCheck.HasAdvantage(CharacterClass.Fighter, Ability.Dexterity));
    }

    [Fact]
    public void ARollAddsTheAbilityBonusAndReportsWhatItCleared()
    {
        var dice = new Dice(seed: 42);
        var result = VaultCheck.Roll(
            dice,
            Scores(strength: 16, dexterity: 10),
            CharacterClass.Fighter,
            Bonuses);

        Assert.Equal(Ability.Strength, result.Ability);
        Assert.False(result.HadAdvantage);
        Assert.Equal(3, result.Bonus);
        Assert.Single(result.Rolls);
        Assert.InRange(result.Roll, 1, 20);
        Assert.Equal(result.Roll + result.Bonus, result.Check);
        Assert.Equal(
            VaultCheck.StepHeightFor(result.Check),
            result.StepHeight);
        Assert.InRange(
            result.StepHeight,
            VaultCheck.MinimumStepHeight,
            VaultCheck.MaximumStepHeight);
    }

    [Fact]
    public void AThiefRollsTwiceAndKeepsTheBetter()
    {
        var dice = new Dice(seed: 42);
        var result = VaultCheck.Roll(
            dice,
            Scores(strength: 10, dexterity: 16),
            CharacterClass.Rogue,
            Bonuses);

        Assert.Equal(Ability.Dexterity, result.Ability);
        Assert.True(result.HadAdvantage);
        Assert.Equal(2, result.Rolls.Count);
        Assert.Equal(Math.Max(result.Rolls[0], result.Rolls[1]), result.Roll);
    }

    [Fact]
    public void EveryRollLandsInsideTheStatedBounds()
    {
        // The bounds are a promise about what the mechanic can produce, so
        // they are checked against the whole spread rather than one seed.
        var dice = new Dice(seed: 7);
        for (var attempt = 0; attempt < 500; attempt++)
        {
            foreach (var scores in new[]
            {
                Scores(strength: 3, dexterity: 3),
                Scores(strength: 20, dexterity: 20),
                Scores(strength: 10, dexterity: 14),
            })
            {
                var result = VaultCheck.Roll(
                    dice,
                    scores,
                    CharacterClass.Rogue,
                    Bonuses);
                Assert.InRange(
                    result.StepHeight,
                    VaultCheck.MinimumStepHeight,
                    VaultCheck.MaximumStepHeight);
            }
        }
    }

    [Fact]
    public void AdvantageConsumesTwoRollsSoASavedWorldResumesTheSameSequence()
    {
        // Both dice are always rolled, never short-circuited: the generator
        // state after a check has to depend only on what the check was, not on
        // which die happened to win.
        var plain = new Dice(seed: 99);
        VaultCheck.Roll(
            plain,
            Scores(strength: 16, dexterity: 10),
            CharacterClass.Fighter,
            Bonuses);

        var advantaged = new Dice(seed: 99);
        VaultCheck.Roll(
            advantaged,
            Scores(strength: 10, dexterity: 16),
            CharacterClass.Rogue,
            Bonuses);

        var oneMore = new Dice(seed: 99);
        oneMore.D20();
        oneMore.D20();
        Assert.Equal(oneMore.State, advantaged.State);
        Assert.NotEqual(plain.State, advantaged.State);
    }

    /// <summary>The shipped bonus curve, loaded the way the rules tests load it.</summary>
    private static AbilityBonusTable Bonuses { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName)).AbilityBonuses;

    private static AbilityScores Scores(int strength, int dexterity) =>
        new(strength, dexterity, 10, 10, 10, 10);
}
