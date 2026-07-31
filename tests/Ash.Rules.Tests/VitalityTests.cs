namespace Ash.Rules.Tests;

public sealed class VitalityTests
{
    private static VitalityData Data { get; } =
        VitalityLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                VitalityLoader.FileName));

    private static CharacterCreationData Creation { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName));

    private static AbilityBonusTable Bonuses => Creation.AbilityBonuses;

    private static AbilityScores Scores(
        int strength = 10,
        int dexterity = 10,
        int constitution = 10,
        int intelligence = 10,
        int wisdom = 10,
        int charisma = 10) =>
        new(strength, dexterity, constitution, intelligence, wisdom, charisma);

    [Theory]
    [InlineData(CharacterClass.Wizard, 4)]
    [InlineData(CharacterClass.Rogue, 6)]
    [InlineData(CharacterClass.Cleric, 8)]
    [InlineData(CharacterClass.Fighter, 10)]
    public void EachClassHasItsOwnHitDie(CharacterClass characterClass, int sides)
    {
        Assert.Equal(sides, Data.HitDice.For(characterClass).DieSides);
    }

    [Fact]
    public void AFighterTakesTheWholeConstitutionBonusOnEveryDie()
    {
        // Constitution 18 is +4, and a fighter's body is the thing they train.
        Assert.Equal(
            4,
            Vitality.ConstitutionBonusPerDie(
                Data,
                CharacterClass.Fighter,
                Scores(constitution: 18),
                Bonuses));
    }

    [Theory]
    [InlineData(CharacterClass.Wizard)]
    [InlineData(CharacterClass.Rogue)]
    [InlineData(CharacterClass.Cleric)]
    public void EveryoneElseIsCappedAtTwo(CharacterClass characterClass)
    {
        Assert.Equal(
            2,
            Vitality.ConstitutionBonusPerDie(
                Data,
                characterClass,
                Scores(constitution: 18),
                Bonuses));
    }

    [Fact]
    public void ACappedBonusStillFallsBelowTheCapWhenTheScoreDoes()
    {
        Assert.Equal(
            1,
            Vitality.ConstitutionBonusPerDie(
                Data,
                CharacterClass.Wizard,
                Scores(constitution: 12),
                Bonuses));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    [InlineData(30, 10)]
    public void HitDiceStopAccumulatingAtTen(int level, int expected)
    {
        Assert.Equal(expected, Data.HitDice.DiceAt(level));
    }

    [Fact]
    public void OneDieIsRolledPerLevelAndEachCarriesTheBonus()
    {
        var pool = Vitality.RollConcussionHits(
            Data,
            new Dice(4242),
            CharacterClass.Fighter,
            level: 3,
            Scores(constitution: 16),
            Bonuses);

        Assert.Equal(3, pool.Dice.Count);
        Assert.Equal(3, pool.ConstitutionBonusPerDie);
        Assert.All(
            pool.Dice,
            die =>
            {
                Assert.InRange(die.Roll, 1, 10);
                Assert.Equal(die.Roll + 3, die.Hits);
            });
        Assert.Equal(pool.Dice.Sum(die => die.Hits), pool.Maximum);
    }

    [Fact]
    public void ADieNeverTakesConcussionHitsAway()
    {
        // Constitution 3 is -4, which would make a wizard's d4 worth nothing or
        // less. The floor is what stops a level costing a character hits.
        var pool = Vitality.RollConcussionHits(
            Data,
            new Dice(7),
            CharacterClass.Wizard,
            level: 6,
            Scores(constitution: 3),
            Bonuses);

        // A d4 at -4 cannot beat the floor on any face, so all six dice give
        // exactly one and the pool is exactly the number of levels.
        Assert.All(pool.Dice, die => Assert.Equal(1, die.Hits));
        Assert.Equal(6, pool.Maximum);
    }

    [Fact]
    public void TheSameSeedRollsTheSameBody()
    {
        var first = Vitality.RollConcussionHits(
            Data,
            new Dice(99),
            CharacterClass.Cleric,
            4,
            Scores(constitution: 14),
            Bonuses);
        var second = Vitality.RollConcussionHits(
            Data,
            new Dice(99),
            CharacterClass.Cleric,
            4,
            Scores(constitution: 14),
            Bonuses);

        Assert.Equal(first.Maximum, second.Maximum);
        Assert.Equal(first.Dice, second.Dice);
    }

    [Fact]
    public void WoundsAreLevelPlusTheBestAbilityBonusWhicheverItIs()
    {
        // Charisma 18 is +4, and it is the best thing about this character even
        // though nothing else about them is a wound rule.
        Assert.Equal(
            7,
            Vitality.MaximumWounds(Data, 3, Scores(charisma: 18), Bonuses));
    }

    [Fact]
    public void ABestAbilityTieGoesToTheEarlierAbility()
    {
        Assert.Equal(
            Ability.Strength,
            Vitality.BestAbility(Scores(strength: 16, wisdom: 16), Bonuses));
    }

    [Fact]
    public void AWretchedCharacterStillHasAWoundToLose()
    {
        // Level 1 with every bonus negative would give zero or fewer wounds,
        // which would send them straight from their last hit to the clock.
        Assert.Equal(
            1,
            Vitality.MaximumWounds(
                Data,
                1,
                Scores(3, 3, 3, 3, 3, 3),
                Bonuses));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(12, 3)]
    public void TheDailyRecoveryCapClimbsWithLevel(int level, int expected)
    {
        Assert.Equal(expected, Data.ConcussionRecovery.DailyCapAt(level));
    }

    [Fact]
    public void RestAndMedicineEachOfferAnotherDie()
    {
        var recovery = Data.ConcussionRecovery;
        Assert.Equal(1, recovery.OfferedDice(fullRest: false, medicalAttention: false));
        Assert.Equal(2, recovery.OfferedDice(fullRest: true, medicalAttention: false));
        Assert.Equal(2, recovery.OfferedDice(fullRest: false, medicalAttention: true));
        Assert.Equal(3, recovery.OfferedDice(fullRest: true, medicalAttention: true));
    }

    [Theory]
    [InlineData(CareLevel.Treated, 9)]
    [InlineData(CareLevel.Rested, 12)]
    [InlineData(CareLevel.Adventuring, 15)]
    public void TheInfectionDcFollowsTheStandardOfCare(CareLevel care, int dc)
    {
        Assert.Equal(dc, Data.Infection.DcFor(care));
    }

    [Fact]
    public void AClassWithNoHitDieIsRefusedRatherThanGuessedAt()
    {
        var json = File.ReadAllText(
                Path.Combine(
                    RulesTestRepository.Root,
                    "data",
                    VitalityLoader.FileName))
            .Replace("\"id\": \"wizard\"", "\"id\": \"cleric\"", StringComparison.Ordinal);

        var exception = Assert.Throws<RulesDataException>(
            () => VitalityLoader.Parse(json));
        Assert.Contains("more than one hit die", exception.Message, StringComparison.Ordinal);
    }
}
