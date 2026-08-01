namespace Ash.Rules.Tests;

public sealed class CharacterCreationTests
{
    private static CharacterCreationData Data { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName));

    private static AbilityBonusTable Bonuses => Data.AbilityBonuses;

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
        Assert.Equal(expected, Bonuses.BonusFor(score));
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
        var first = CharacterCreation.Choose(
            Data,
            CharacterCreation.RollIronman(Data, new Dice(20260731)),
            CharacterClass.Fighter);
        var second = CharacterCreation.Choose(
            Data,
            CharacterCreation.RollIronman(Data, new Dice(20260731)),
            CharacterClass.Fighter);

        Assert.Equal(first, second);
        Assert.Equal(
            CharacterCreation.RollUnearthedArcana(
                Data,
                new Dice(99),
                CharacterClass.Wizard),
            CharacterCreation.RollUnearthedArcana(
                Data,
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
            var rolled = CharacterCreation.RollIronman(Data, dice);
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
        var rolled = CharacterCreation.RollIronman(Data, dice);

        Assert.True(
            rolled.Attempts > 1,
            "This seed was chosen because it takes more than one attempt.");
        Assert.False(Data.Ironman.IsPlayable([14, 14, 14, 14, 14, 14]));
        Assert.False(Data.Ironman.IsPlayable([18, 18, 5, 5, 10, 10]));
        Assert.True(Data.Ironman.IsPlayable([18, 15, 5, 10, 10, 10]));
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
        var priority = Data.UnearthedArcana.PriorityFor(characterClass);

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
                Data,
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
            Data.UnearthedArcana.Pools);
    }

    [Fact]
    public void IronmanRollsFirstAndChoosesSecond()
    {
        var rolled = CharacterCreation.RollIronman(Data, new Dice(2026));

        // The scores exist before any class does: what you can be is decided
        // by looking at them.
        Assert.True(Data.Ironman.IsPlayable(rolled.Scores.InOrder));

        var fighter = CharacterCreation.Choose(
            Data,
            rolled,
            CharacterClass.Fighter);
        var wizard = CharacterCreation.Choose(
            Data,
            rolled,
            CharacterClass.Wizard);

        // Choosing does not re-roll: the same dice, two different lives.
        Assert.Equal(rolled.Scores, fighter.Scores);
        Assert.Equal(rolled.Scores, wizard.Scores);
        Assert.Equal(rolled.Attempts, fighter.Attempts);
        Assert.Equal(CharacterClass.Fighter, fighter.Class);
        Assert.Equal(CharacterClass.Wizard, wizard.Class);
        Assert.Equal(CharacterCreationMethod.Ironman, fighter.Method);
        Assert.Equal(2, fighter.TalentRolls);
    }

    [Fact]
    public void AHumanTakesTwoTalentRolls()
    {
        var rolled = CharacterCreation.RollUnearthedArcana(
            Data,
            new Dice(1),
            CharacterClass.Cleric);

        Assert.Equal(Ancestry.Human, rolled.Ancestry);
        Assert.Equal(2, rolled.TalentRolls);
        Assert.Equal(Data.AncestryOf(Ancestry.Human).TalentRolls, rolled.TalentRolls);
    }

    [Theory]
    [InlineData(CharacterClass.Fighter)]
    [InlineData(CharacterClass.Rogue)]
    public void HumanTalentsAreTwoResolvedDiceRollsNotChoices(
        CharacterClass characterClass)
    {
        var dice = new Dice(20260801);
        var character = CharacterCreation.RollUnearthedArcana(
            Data,
            dice,
            characterClass);

        var talented = CharacterCreation.RollTalents(Data, dice, character);

        Assert.Equal(2, talented.TalentRolls);
        Assert.Equal(2, talented.Talents.Count);
        Assert.All(talented.Talents, talent =>
        {
            Assert.InRange(talent.Roll, 2, 12);
            var authored = Data.TalentTableFor(characterClass).ForRoll(talent.Roll);
            Assert.Equal(authored.Id, talent.Id);
            Assert.Equal(authored.Name, talent.Name);
        });
    }

    [Fact]
    public void TheSameCreationSequenceRollsTheSameTalents()
    {
        static CreatedCharacter Roll(CharacterCreationData data)
        {
            var dice = new Dice(77);
            var character = CharacterCreation.RollUnearthedArcana(
                data,
                dice,
                CharacterClass.Fighter);
            return CharacterCreation.RollTalents(data, dice, character);
        }

        Assert.Equal(Roll(Data), Roll(Data));
    }

    [Fact]
    public void AStatTalentOffersOnlyFullValueChoicesUnderTwenty()
    {
        var scores = new AbilityScores(
            strength: 19,
            dexterity: 18,
            constitution: 20,
            intelligence: 10,
            wisdom: 10,
            charisma: 10);
        var talent = new ClassTalentRoll(
            8,
            "martial_attribute_boost",
            "Martial Attribute Boost",
            "test");

        var choices = CharacterCreation.LegalTalentChoices(
            CharacterClass.Fighter,
            scores,
            talent);

        Assert.Contains(choices, choice => choice.Description == "+2 DEX");
        Assert.DoesNotContain(choices, choice => choice.Description == "+2 STR");
        Assert.DoesNotContain(choices, choice =>
            choice.FirstAbility == Ability.Constitution ||
            choice.SecondAbility == Ability.Constitution);
        Assert.Contains(choices, choice =>
            choice.FirstAbility == Ability.Strength &&
            choice.SecondAbility == Ability.Dexterity &&
            choice.FirstIncrease == 1 &&
            choice.SecondIncrease == 1);
    }

    [Fact]
    public void ThePlayerChoiceDoesNotChangeTheRandomTalentRow()
    {
        var talent = new ClassTalentRoll(
            8,
            "martial_attribute_boost",
            "Martial Attribute Boost",
            "test");
        var character = new CreatedCharacter(
            new AbilityScores(19, 18, 20, 10, 10, 10),
            CharacterClass.Fighter,
            Ancestry.Human,
            CharacterCreationMethod.Ironman,
            TalentRolls: 1,
            Attempts: 1,
            FirstTalent: talent);
        var choice = CharacterCreation.LegalTalentChoices(
                character.Class,
                character.Scores,
                talent)
            .Single(value => value.Description == "+1 STR, +1 DEX");

        var resolved = CharacterCreation.ResolveTalent(character, 0, choice);

        Assert.Equal(20, resolved.Scores.Strength);
        Assert.Equal(19, resolved.Scores.Dexterity);
        Assert.Equal(20, resolved.Scores.Constitution);
        Assert.Equal(talent.Roll, resolved.FirstTalent!.Roll);
        Assert.Equal(talent.Id, resolved.FirstTalent.Id);
        Assert.Equal(choice, resolved.FirstTalent.Choice);
        Assert.True(resolved.TalentsResolved);
    }

    [Fact]
    public void RolledWeaponAndSkillRowsLetThePlayerChooseTheirTarget()
    {
        var scores = new AbilityScores(12, 12, 12, 12, 12, 12);
        var fighter = CharacterCreation.LegalTalentChoices(
            CharacterClass.Fighter,
            scores,
            new ClassTalentRoll(4, "weapon_specialization", "Weapon", "test"));
        var rogue = CharacterCreation.LegalTalentChoices(
            CharacterClass.Rogue,
            scores,
            new ClassTalentRoll(4, "agile_lethality", "Agile", "test"));

        Assert.Contains(fighter, choice =>
            choice.WeaponFamily == WeaponFamily.Sword);
        Assert.Contains(fighter, choice =>
            choice.WeaponFamily == WeaponFamily.Dagger);
        Assert.Contains(rogue, choice => choice.RogueSkill == RogueSkill.Traps);
        Assert.Contains(rogue, choice => choice.RogueSkill == RogueSkill.Stealth);
    }
}
