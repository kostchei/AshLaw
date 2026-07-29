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

        Assert.Equal(200, lines);
    }

    [Theory]
    // A signed modifier the parser has no rule for.
    [InlineData("Weak grip. No extra damage. +0 hits.", "Weak grip. -7 to morale. +0 hits.", "-7")]
    // A number bound to a mechanical unit.
    [InlineData("Blow to side. +4 hits. -40 to activity for 1 round.", "Blow to side. Slowed 3 rounds.", "3 rounds")]
    // A state-change verb with no supporting numbers.
    [InlineData("Minor fracture of ribs. +5 hits. -5 to activity.", "Minor fracture of ribs. Target blinded.", "blinded")]
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
            "Blow to side. +4 hits. -40 to activity for 1 round.",
            "Ghastly flank laceration. Artery nicked. +4 hits. -40 to activity for 1 round.");

        var rules = RulesDataLoader.LoadFromDirectory(copy.DirectoryPath);

        Assert.NotNull(rules);
    }

    /// <summary>
    /// These three lines state a broken bone that no other effect on the line
    /// represents. They were silently dropped before <see cref="TraumaEffectKind.BreakBone"/>
    /// existed.
    /// </summary>
    [Theory]
    [InlineData(CriticalTableId.Crush, "shoulder", TraumaEffectCondition.NoShield)]
    [InlineData(CriticalTableId.Unbalancing, "lower leg", TraumaEffectCondition.Always)]
    [InlineData(CriticalTableId.Puncture, "bone", TraumaEffectCondition.NoArmArmor)]
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
        // Crush Tier E index 2: "Blow to forearm. +5 hits. If no arm armor, stunned 1 round."
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Crush, CriticalTier.E, 2);
        Assert.Contains("If no arm armor", outcome.Text, StringComparison.Ordinal);

        Assert.Contains(
            new TraumaEffect(TraumaEffectKind.AdditionalHits, Magnitude: 5),
            outcome.Effects);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Stun,
                Duration: 1,
                DurationUnit: TraumaDurationUnit.Rounds,
                AppliesWhen: TraumaEffectCondition.NoArmArmor),
            outcome.Effects);
    }

    /// <summary>
    /// "N hits per round" is a bleed, not an additional-hits total; the two
    /// patterns overlap textually and must not both fire on the same span.
    /// </summary>
    [Fact]
    public void BleedIsNotAlsoCountedAsAdditionalHits()
    {
        // Slash Tier E index 2: "Minor chest wound. +3 hits. 1 hit per round. -5 to activity."
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Slash, CriticalTier.E, 2);
        Assert.Contains("1 hit per round", outcome.Text, StringComparison.Ordinal);

        var additional = outcome.Effects
            .Where(effect => effect.Kind == TraumaEffectKind.AdditionalHits)
            .ToArray();
        var bleeds = outcome.Effects
            .Where(effect => effect.Kind == TraumaEffectKind.Bleeding)
            .ToArray();

        Assert.Equal(3, Assert.Single(additional).Magnitude);
        Assert.Equal(1, Assert.Single(bleeds).Magnitude);
    }

    [Fact]
    public void UnsupportedConditionIsRejected()
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "ct_1_crush_critical_table.csv",
            "If no arm armor, stunned 1 round.",
            "If no greaves, stunned 1 round.");

        var exception = Assert.Throws<RulesDataException>(
            () => RulesDataLoader.LoadFromDirectory(copy.DirectoryPath));

        // Without this check the clause would parse as unconditional, so the stun
        // would apply to every target rather than only to unarmoured ones.
        Assert.Contains("If no", exception.Message, StringComparison.Ordinal);
    }
}
