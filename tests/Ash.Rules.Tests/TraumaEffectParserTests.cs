namespace Ash.Rules.Tests;

/// <summary>
/// Covers build plan M0 task 5: trauma prose becomes structured effects at import,
/// and a mechanical phrase the parser cannot account for is a build error.
/// </summary>
public sealed class TraumaEffectParserTests
{
    private static RulesData Rules =>
        RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);

    [Fact]
    public void EveryVendoredTraumaLineParsesWithoutUnaccountedMechanics()
    {
        var rules = Rules;
        var lines = 0;

        foreach (var table in Enum.GetValues<CriticalTableId>())
        {
            foreach (var tier in Enum.GetValues<CriticalTier>())
            {
                for (var index = 1; index <= 10; index++)
                {
                    var outcome = rules.GetCriticalOutcome(table, tier, index);
                    Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
                    lines++;
                }
            }
        }

        Assert.Equal(400, lines);
    }

    [Theory]
    // A signed modifier the parser has no rule for.
    [InlineData("Chest blow. +5 hits. Knocked back 10 feet.", "Chest blow. -7 to morale. +5 hits.", "-7")]
    // A number bound to a mechanical unit.
    [InlineData("Chest blow. +5 hits. Knocked back 10 feet.", "Chest blow. Slowed 3 rounds. Knocked back 10 feet.", "3 rounds")]
    // A state-change verb with no supporting numbers.
    [InlineData("Chest blow. +5 hits. Knocked back 10 feet.", "Chest blow. Target frozen 3 rounds. Knocked back 10 feet.", "frozen")]
    public void UnhandledMechanicalPhraseFailsTheLoad(
        string original,
        string replacement,
        string expectedInMessage)
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace("ct_1_crush_critical_table.csv", original, replacement);

        var exception = Assert.Throws<RulesDataException>(
            () => RulesDataLoader.LoadFromDirectory(copy.DirectoryPath));

        Assert.Contains("produce no effect", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expectedInMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "ct_1_crush_critical_table.csv",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DescriptiveInjuryProseDoesNotFailTheLoad()
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "ct_1_crush_critical_table.csv",
            "Chest blow. +5 hits. Knocked back 10 feet.",
            "Ghastly flank laceration. Chest blow. +5 hits. Knocked back 10 feet.");

        var rules = RulesDataLoader.LoadFromDirectory(copy.DirectoryPath);

        Assert.NotNull(rules);
    }

    /// <summary>
    /// Lines stating a broken bone are captured as BreakBone effects.
    /// </summary>
    [Theory]
    [InlineData(CriticalTableId.Crush, "leg", TraumaEffectCondition.Always)]
    [InlineData(CriticalTableId.Unbalancing, "leg", TraumaEffectCondition.Always)]
    [InlineData(CriticalTableId.Cold, "bone", TraumaEffectCondition.Always)]
    public void BrokenBonesAreCapturedAsEffects(
        CriticalTableId table,
        string expectedPart,
        TraumaEffectCondition expectedCondition)
    {
        var rules = Rules;

        var matches =
            from tier in Enum.GetValues<CriticalTier>()
            from index in Enumerable.Range(1, 10)
            from effect in rules.GetCriticalOutcome(table, tier, index).Effects
            where effect.Kind == TraumaEffectKind.BreakBone &&
                effect.Detail == expectedPart &&
                effect.AppliesWhen == expectedCondition
            select effect;

        Assert.NotEmpty(matches);
    }

    [Fact]
    public void ConditionalClausesAttachTheirCondition()
    {
        // Impact Tier C index 2: "Blast to shield arm. +10 hits. Shield broken. If no shield: shoulder broken."
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Impact, CriticalTier.C, 2);
        Assert.Contains("If no shield", outcome.Text, StringComparison.Ordinal);

        Assert.Contains(
            new TraumaEffect(TraumaEffectKind.AdditionalHits, Magnitude: 10),
            outcome.Effects);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.BreakBone,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: "shoulder",
                AppliesWhen: TraumaEffectCondition.NoShield),
            outcome.Effects);
    }

    /// <summary>
    /// "N hits per round" is a bleed, not an additional-hits total; the two
    /// patterns overlap textually and must not both fire on the same span.
    /// </summary>
    [Fact]
    public void BleedIsNotAlsoCountedAsAdditionalHits()
    {
        // Slash Tier B index 2: "Minor chest wound. +2 hits. 1 hit per round. Target has Disadvantage on next attack roll before start of your next turn."
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Slash, CriticalTier.B, 2);
        Assert.Contains("1 hit per round", outcome.Text, StringComparison.Ordinal);

        var additional = outcome.Effects
            .Where(effect => effect.Kind == TraumaEffectKind.AdditionalHits)
            .ToArray();
        var bleeds = outcome.Effects
            .Where(effect => effect.Kind == TraumaEffectKind.Bleeding)
            .ToArray();

        Assert.Equal(2, Assert.Single(additional).Magnitude);
        Assert.Equal(1, Assert.Single(bleeds).Magnitude);
    }

    [Fact]
    public void UnsupportedConditionIsRejected()
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "ct_1_crush_critical_table.csv",
            "If no helm",
            "If no greaves");

        var exception = Assert.Throws<RulesDataException>(
            () => RulesDataLoader.LoadFromDirectory(copy.DirectoryPath));

        // Without this check the clause would parse as unconditional, so the stun
        // would apply to every target rather than only to unarmoured ones.
        Assert.Contains("If no", exception.Message, StringComparison.Ordinal);
    }
}
