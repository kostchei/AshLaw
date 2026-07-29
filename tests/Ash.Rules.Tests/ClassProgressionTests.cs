namespace Ash.Rules.Tests;

public sealed class ClassProgressionTests
{
    private static ClassProgressionTable Progression =>
        RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory).ClassProgression;

    public static TheoryData<CharacterClass> AllClasses
    {
        get
        {
            var data = new TheoryData<CharacterClass>();
            foreach (var characterClass in Enum.GetValues<CharacterClass>())
            {
                data.Add(characterClass);
            }

            return data;
        }
    }

    /// <summary>
    /// M0 task 8: the published Lv 1-30 table is authority; the bracket rules in
    /// <c>class_progression_tables.md</c> §2 must reproduce it exactly. All 120
    /// cells are asserted, not a sample.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllClasses))]
    public void BracketRulesDeriveThePublishedTable(CharacterClass characterClass)
    {
        var progression = Progression;
        var rules = progression.GetRules(characterClass);

        for (var level = ClassProgressionTable.MinimumLevel;
             level <= ClassProgressionTable.MaximumLevel;
             level++)
        {
            var published = progression.GetAttackModifier(characterClass, level);
            var derived = rules.DeriveAttackModifier(level);

            Assert.True(
                published == derived,
                $"{characterClass} level {level}: published table says +{published}, " +
                $"bracket rules derive +{derived}.");
        }
    }

    /// <summary>
    /// The cap levels quoted in §2's summary table and §4's highlights.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Fighter, 20, 26)]
    [InlineData(CharacterClass.Cleric, 15, 29)]
    [InlineData(CharacterClass.Rogue, 10, 26)]
    [InlineData(CharacterClass.Wizard, 6, 19)]
    public void PublishedTableReachesEachHardCapAtTheDocumentedLevel(
        CharacterClass characterClass,
        int expectedCap,
        int expectedCapLevel)
    {
        var progression = Progression;

        Assert.Equal(expectedCap, progression.GetRules(characterClass).HardCap);
        Assert.Equal(expectedCapLevel, progression.GetCapLevel(characterClass));
        Assert.Equal(
            expectedCap,
            progression.GetAttackModifier(characterClass, ClassProgressionTable.MaximumLevel));
    }

    [Theory]
    [MemberData(nameof(AllClasses))]
    public void ProgressionIsMonotonicAndNeverExceedsTheHardCap(CharacterClass characterClass)
    {
        var progression = Progression;
        var cap = progression.GetRules(characterClass).HardCap;
        var previous = 0;

        for (var level = ClassProgressionTable.MinimumLevel;
             level <= ClassProgressionTable.MaximumLevel;
             level++)
        {
            var modifier = progression.GetAttackModifier(characterClass, level);
            Assert.InRange(modifier, previous, cap);
            previous = modifier;
        }
    }

    /// <summary>
    /// Spot-checks the §5.1 corroboration in the build plan: class caps map onto
    /// MERP OB values at the documented 5x scale factor.
    /// </summary>
    [Theory]
    [InlineData(CharacterClass.Fighter, 100)]
    [InlineData(CharacterClass.Cleric, 75)]
    [InlineData(CharacterClass.Rogue, 50)]
    [InlineData(CharacterClass.Wizard, 30)]
    public void HardCapsMatchTheirMerpOffensiveBonusEquivalents(
        CharacterClass characterClass,
        int expectedMerpOffensiveBonus)
    {
        var cap = Progression.GetRules(characterClass).HardCap;

        Assert.Equal(expectedMerpOffensiveBonus, cap * 5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void OutOfRangeLevelsThrow(int level)
    {
        var progression = Progression;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => progression.GetAttackModifier(CharacterClass.Fighter, level));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => progression.GetRules(CharacterClass.Fighter).DeriveAttackModifier(level));
    }
}
