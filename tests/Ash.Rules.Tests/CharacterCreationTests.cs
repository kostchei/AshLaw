namespace Ash.Rules.Tests;

public sealed class CharacterCreationTests
{
    [Theory]
    [InlineData(3, -4)]
    [InlineData(6, -2)]
    [InlineData(9, -1)]
    [InlineData(10, 0)]
    [InlineData(11, 0)]
    [InlineData(12, 1)]
    [InlineData(15, 2)]
    [InlineData(18, 4)]
    [InlineData(20, 5)]
    public void TheBonusCurveFloorsOddScores(int score, int expected)
    {
        Assert.Equal(expected, AbilityScores.BonusFor(score));
    }

    [Fact]
    public void ScoresOutsideThreeToTwentyAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbilityScores(2, 10, 10, 10, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AbilityScores(10, 21, 10, 10, 10, 10));
    }

    [Fact]
    public void TheSameSeedRollsTheSameCharacter()
    {
        var first = CharacterCreation.RollIronman(
            new Dice(20260731),
            CharacterClass.Fighter);
        var second = CharacterCreation.RollIronman(
            new Dice(20260731),
            CharacterClass.Fighter);

        Assert.Equal(first, second);
        Assert.Equal(
            CharacterCreation.RollUnearthedArcana(
                new Dice(99),
                CharacterClass.Wizard),
            CharacterCreation.RollUnearthedArcana(
                new Dice(99),
                CharacterClass.Wizard));
    }

    [Fact]
    public void ADiceStateCanBeSavedAndResumed()
    {
        var dice = new Dice(7);
        _ = dice.Pool(20, 6);
        var resumed = Dice.FromState(dice.State);

        Assert.Equal(dice.Pool(10, 20), resumed.Pool(10, 20));
    }

    [Fact]
    public void EveryIronmanSetIsPlayable()
    {
        var dice = new Dice(4242);
        for (var character = 0; character < 200; character++)
        {
            var rolled = CharacterCreation.RollIronman(
                dice,
                CharacterClass.Fighter);
            var scores = rolled.Scores.InOrder;

            Assert.True(
                scores.Count(score => score >= 15) >= 2,
                $"{rolled.Scores} has fewer than two scores of 15.");
            Assert.True(
                scores.Count(score => score < 6) <= 1,
                $"{rolled.Scores} has more than one score below 6.");
            Assert.InRange(rolled.Attempts, 1, 1000);
            Assert.All(scores, score => Assert.InRange(score, 3, 18));
        }
    }

    [Fact]
    public void AnUnplayableSetIsRerolledRatherThanKept()
    {
        // Two scores of 15 are rare, so most sets are discarded: the attempt
        // count is the evidence that rerolling happens at all.
        var dice = new Dice(11);
        var rolled = CharacterCreation.RollIronman(dice, CharacterClass.Rogue);

        Assert.True(
            rolled.Attempts > 1,
            "This seed was chosen because it takes more than one attempt.");
        Assert.False(CharacterCreation.IsPlayable([14, 14, 14, 14, 14, 14]));
        Assert.False(CharacterCreation.IsPlayable([18, 18, 5, 5, 10, 10]));
        Assert.True(CharacterCreation.IsPlayable([18, 15, 5, 10, 10, 10]));
    }

    [Theory]
    [InlineData(CharacterClass.Fighter, Ability.Strength, Ability.Intelligence)]
    [InlineData(CharacterClass.Rogue, Ability.Dexterity, Ability.Wisdom)]
    [InlineData(CharacterClass.Cleric, Ability.Wisdom, Ability.Dexterity)]
    [InlineData(CharacterClass.Wizard, Ability.Intelligence, Ability.Strength)]
    public void EachClassPutsItsBestDiceOnItsOwnStat(
        CharacterClass characterClass,
        Ability first,
        Ability last)
    {
        var priority = CharacterCreation.PriorityFor(characterClass);

        Assert.Equal(first, priority[0]);
        Assert.Equal(last, priority[^1]);
        Assert.Equal(AbilityScores.AbilityCount, priority.Count);
        Assert.Equal(priority.Count, priority.Distinct().Count());
    }

    [Fact]
    public void TheBiggestPoolBeatsTheSmallestAcrossManyCharacters()
    {
        var dice = new Dice(555);
        var primary = 0;
        var dump = 0;
        const int Characters = 300;
        for (var character = 0; character < Characters; character++)
        {
            var rolled = CharacterCreation.RollUnearthedArcana(
                dice,
                CharacterClass.Fighter);
            primary += rolled.Scores.Strength;
            dump += rolled.Scores.Intelligence;
        }

        // 8d6 keep 3 against 3d6 straight: the priority stat should average
        // several points higher, which is the whole point of the method.
        Assert.True(
            primary > dump + (3 * Characters),
            $"Primary averaged {primary / (double)Characters:F1}, " +
            $"dump {dump / (double)Characters:F1}.");
        Assert.Equal(
            [8, 7, 6, 5, 4, 3],
            CharacterCreation.UnearthedArcanaPools);
    }

    [Fact]
    public void AHumanTakesTwoTalentRolls()
    {
        var rolled = CharacterCreation.RollUnearthedArcana(
            new Dice(1),
            CharacterClass.Cleric);

        Assert.Equal(Ancestry.Human, rolled.Ancestry);
        Assert.Equal(2, rolled.TalentRolls);
        Assert.Equal(CharacterCreation.HumanTalentRolls, rolled.TalentRolls);
    }
}
