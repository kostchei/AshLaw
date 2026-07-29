namespace Ash.Rules.Tests;

public sealed class ClassAdvancementTests
{
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

    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryClassHasOneAdvancementAtEveryLevel(CharacterClass characterClass)
    {
        var plan = ClassAdvancementCatalog.For(characterClass);

        Assert.Equal(characterClass, plan.Class);
        Assert.NotEmpty(plan.PinnedAbilities[1]);

        for (var level = ClassAdvancementPlan.MinimumLevel;
             level <= ClassAdvancementPlan.MaximumLevel;
             level++)
        {
            var advancement = plan.AtLevel(level);
            Assert.Equal(level, advancement.Level);

            if (advancement.Kind == ClassLevelAdvanceKind.PinnedAbility)
            {
                Assert.NotEmpty(advancement.Abilities);
                Assert.DoesNotContain(level, plan.OpenTalentLevels);
            }
            else
            {
                Assert.Empty(advancement.Abilities);
                Assert.Contains(level, plan.OpenTalentLevels);
            }
        }

        Assert.Equal(
            ClassAdvancementPlan.MaximumLevel,
            plan.PinnedAbilities.Count + plan.OpenTalentLevels.Count);
    }

    [Theory]
    [MemberData(nameof(AllClasses))]
    public void EveryTalentRollResolvesExactlyOnce(CharacterClass characterClass)
    {
        var plan = ClassAdvancementCatalog.For(characterClass);

        var probabilityWays = 0;
        for (var roll = 2; roll <= 12; roll++)
        {
            var outcome = plan.ResolveTalent(roll);
            Assert.InRange(roll, outcome.MinimumRoll, outcome.MaximumRoll);
        }

        foreach (var outcome in plan.TalentOutcomes)
        {
            probabilityWays += outcome.ProbabilityWays;
        }

        Assert.Equal(36, probabilityWays);
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ResolveTalent(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ResolveTalent(13));
    }

    [Theory]
    [InlineData(CharacterClass.Bard, "charm")]
    [InlineData(CharacterClass.Barbarian, "wilderness")]
    [InlineData(CharacterClass.Sorcerer, "spellfire")]
    [InlineData(CharacterClass.Paladin, "chivalric")]
    [InlineData(CharacterClass.CelestialWarlock, "celestial")]
    [InlineData(CharacterClass.Monk, "open hand")]
    [InlineData(CharacterClass.Ranger, "track")]
    [InlineData(CharacterClass.Assassin, "assassination")]
    public void ExtraClassPinsItsDefiningLevelOneIdentity(
        CharacterClass characterClass,
        string definingText)
    {
        var levelOne = ClassAdvancementCatalog.For(characterClass).AtLevel(1);
        var text = string.Join(
            " ",
            levelOne.Abilities.Select(ability => $"{ability.Name} {ability.Description}"));

        Assert.Contains(definingText, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SorcererPlanCarriesEverySpellFamilyFromTheDesignDocument()
    {
        var plan = ClassAdvancementCatalog.For(CharacterClass.Sorcerer);
        var text = string.Join(
            " ",
            plan.PinnedAbilities.Values
                .SelectMany(abilities => abilities)
                .Select(ability => $"{ability.Name} {ability.Description}"));

        Assert.Contains("Flesh", text);
        Assert.Contains("Solid", text);
        Assert.Contains("Gas/Fluid", text);
        Assert.Contains("Soul", text);
        Assert.Contains("Counter", text);
        Assert.Contains("phantom", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Node Mastery", text);
    }

    [Fact]
    public void PaladinChivalryUsesTheSixPendragonVirtuesAndEightyPointThreshold()
    {
        var upheld = Enum.GetValues<ChivalricVirtue>()
            .ToDictionary(virtue => virtue, _ => 14);
        var broken = Enum.GetValues<ChivalricVirtue>()
            .ToDictionary(virtue => virtue, _ => 13);

        Assert.Equal(6, upheld.Count);
        Assert.True(PaladinChivalricCode.Check(upheld).IsAllowed);
        Assert.False(PaladinChivalricCode.Check(broken).IsAllowed);

        upheld.Remove(ChivalricVirtue.Merciful);
        Assert.Throws<ArgumentException>(() => PaladinChivalricCode.Check(upheld));
    }

    [Fact]
    public void RulesDataExposesAllClassAdvancementPlans()
    {
        var rules = RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);

        Assert.Equal(Enum.GetValues<CharacterClass>().Length, rules.ClassAdvancements.Count);
        Assert.Equal(
            "Celestial Warlock",
            rules.GetClassAdvancement(CharacterClass.CelestialWarlock).DisplayName);
    }
}
