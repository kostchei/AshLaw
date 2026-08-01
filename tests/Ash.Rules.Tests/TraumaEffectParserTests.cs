namespace Ash.Rules.Tests;

/// <summary>
/// Covers build plan M0 task 5: trauma prose becomes structured effects at import,
/// and a mechanical phrase the parser cannot account for is a build error.
/// </summary>
public sealed class TraumaEffectParserTests
{
    private static RulesData Rules =>
        RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);

    public static TheoryData<CriticalTableId, int, string> UserSpecifiedExamples =>
        new()
        {
            { CriticalTableId.Crush, 5, "No critical effect." },
            { CriticalTableId.Slash, 5, "No critical effect." },
            { CriticalTableId.Puncture, 5, "No critical effect." },
            { CriticalTableId.Unbalancing, 5, "No critical effect." },

            { CriticalTableId.Crush, 6, "Minor fracture of ribs. Graze." },
            { CriticalTableId.Slash, 6, "Minor calf wound. 1 hit per round." },
            { CriticalTableId.Puncture, 6, "Glancing blow to side. +3 hits." },
            { CriticalTableId.Unbalancing, 6, "Arm strike. +2 hits." },

            { CriticalTableId.Crush, 7, "Blow to side. +4 hits. Sap." },
            { CriticalTableId.Slash, 7, "Blow to upper leg. Graze." },
            { CriticalTableId.Puncture, 7, "Thigh strike. +3 hits. If no leg armor: 3 hits per round." },
            { CriticalTableId.Unbalancing, 7, "Leg strike. +4 hits. If no leg armor: Sap." },

            { CriticalTableId.Crush, 8, "Blow to forearm. +5 hits. If no arm armor: Restrained 1 round." },
            { CriticalTableId.Slash, 8, "Minor chest wound. Graze. 1 hit per round." },
            { CriticalTableId.Puncture, 8, "Minor forearm wound. +2 hits. If no arm armor: Restrained 1 round." },
            { CriticalTableId.Unbalancing, 8, "Chest strike. Graze and Push." },

            { CriticalTableId.Crush, 9, "Blow to shield shoulder breaks shield. If no shield: shoulder broken, arm useless." },
            { CriticalTableId.Slash, 9, "Minor forearm wound. Graze. 2 hits per round. Restrained 1 round." },
            { CriticalTableId.Puncture, 9, "Strike along side of chest. 1 hit per round. Restrained 1 round." },
            { CriticalTableId.Unbalancing, 9, "Blow to shield arm. Graze. Shield torn away. If no shield: Restrained 2 rounds." },

            { CriticalTableId.Crush, 10, "Blow breaks bone in leg. Graze x2. Injured: Disadvantage on one ability score and half Speed until healed. Restrained 2 rounds." },
            { CriticalTableId.Slash, 10, "Medium thigh wound. Graze. 1 hit per round. Restrained 2 rounds." },
            { CriticalTableId.Puncture, 10, "Strike to lower leg. Tendons torn. +3 hits. 2 levels of Exhaustion until healed. Restrained 1 round." },
            { CriticalTableId.Unbalancing, 10, "Elbow strike. Forearm numbed. Graze x2. Drop weapon. Sap." },

            { CriticalTableId.Crush, 16, "Neck strike crushes throat. Cannot breathe. Restrained and Suffocating." },
            { CriticalTableId.Slash, 16, "Sever weapon arm. 15 hits per round. Arm useless. Prone and Incapacitated." },
            { CriticalTableId.Puncture, 16, "Nailed in lower back. Prone and Dying." },
            { CriticalTableId.Unbalancing, 16, "Head slammed into stone. Prone and Dying." },
        };

    [Theory]
    [MemberData(nameof(UserSpecifiedExamples))]
    public void UserSpecifiedConversionsAreExact(
        CriticalTableId table,
        int index,
        string expected)
    {
        Assert.Equal(expected, Rules.GetCriticalOutcome(table, index).Text);
    }

    [Fact]
    public void EveryVendoredTraumaLineParsesWithoutUnaccountedMechanics()
    {
        var rules = Rules;
        var lines = 0;

        foreach (var table in Enum.GetValues<CriticalTableId>())
        {
            for (var index = 1; index <= 18; index++)
            {
                var outcome = rules.GetCriticalOutcome(table, index);
                Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
                lines++;
            }
        }

        Assert.Equal(144, lines);
    }

    public static TheoryData<CriticalTableId, int, string, string> PhysicalUpperBands =>
        new()
        {
            { CriticalTableId.Crush, 11, "81-86", "Blow to weapon arm. Graze x2. Restrained 2 rounds. If no arm armor: tendon damaged, arm broken & useless." },
            { CriticalTableId.Crush, 12, "87-89", "Shatter knee. Graze x2. Prone and Restrained 3 rounds. Injured: Disadvantage on two ability scores and half Speed until healed." },
            { CriticalTableId.Crush, 13, "91-96", "Blow to side of head. +20 hits. Stable at 0 hit points and 0 wounds. Wakes in 1d4 hours with 1 hit point. Injured: Disadvantage on one ability score and half Speed until healed. If no helm: skull crushed." },
            { CriticalTableId.Crush, 14, "97-99", "Blast to chest sends ribcage through lungs. Prone and Dying." },
            { CriticalTableId.Crush, 15, "101-106", "Blow breaks hip. +15 hits. Prone and Restrained 3 rounds." },
            { CriticalTableId.Crush, 16, "107-109", "Neck strike crushes throat. Cannot breathe. Restrained and Suffocating." },
            { CriticalTableId.Crush, 17, "111-116", "Shatter elbow in weapon arm. Arm useless. Restrained 5 rounds." },
            { CriticalTableId.Crush, 18, "117-119", "Blow to side crushes chest cavity. Prone and Dying." },

            { CriticalTableId.Slash, 11, "81-86", "Slash weapon arm. Graze x2. 1 hit per round. If no arm armor: muscle & tendon damage, arm useless." },
            { CriticalTableId.Slash, 12, "87-89", "Destroys one eye. Graze x2. Restrained 30 rounds." },
            { CriticalTableId.Slash, 13, "91-96", "Strike to side of head. +15 hits. Stable at 0 hit points and 0 wounds. Wakes in 1d4 hours with 1 hit point. Injured: Disadvantage on two ability scores and half Speed until healed. If no helm: dies instantly." },
            { CriticalTableId.Slash, 14, "97-99", "Sever lower leg. 20 hits per round. Prone and Incapacitated." },
            { CriticalTableId.Slash, 15, "101-106", "Major abdominal wound. Graze x2. 8 hits per round. Restrained 4 rounds." },
            { CriticalTableId.Slash, 16, "107-109", "Sever weapon arm. 15 hits per round. Arm useless. Prone and Incapacitated." },
            { CriticalTableId.Slash, 17, "111-116", "Sever hand. 12 hits per round. Prone and Restrained 6 rounds." },
            { CriticalTableId.Slash, 18, "117-119", "Sever spine. +20 hits. Prone and Paralyzed from the neck down permanently." },

            { CriticalTableId.Puncture, 11, "81-86", "Strike to weapon arm. Graze x2. If no arm armor: bone broken and Restrained 3 rounds." },
            { CriticalTableId.Puncture, 12, "87-89", "Strike through lower leg. Sever muscle. Restrained 3 rounds. Injured: Disadvantage on two ability scores and half Speed until healed." },
            { CriticalTableId.Puncture, 13, "91-96", "Strike through both lungs. Prone and Dying." },
            { CriticalTableId.Puncture, 14, "97-99", "Strike to side of head. Graze x2. Stable at 0 hit points and 0 wounds. Wakes in 1d4 hours with 1 hit point. Injured: Disadvantage on two ability scores and half Speed until healed. If no helm: dies instantly." },
            { CriticalTableId.Puncture, 15, "101-106", "Major abdominal wound. Graze x2. 6 hits per round. Restrained 4 rounds." },
            { CriticalTableId.Puncture, 16, "107-109", "Nailed in lower back. Prone and Dying." },
            { CriticalTableId.Puncture, 17, "111-116", "Strike through leg. Artery severed. 12 hits per round. Prone and Dying." },
            { CriticalTableId.Puncture, 18, "117-119", "Strike through kidneys. Graze x2. Prone and Dying." },

            { CriticalTableId.Unbalancing, 11, "81-86", "Shot to side. Push. Drop anything carried in hands. Restrained 3 rounds." },
            { CriticalTableId.Unbalancing, 12, "87-89", "Side strike. Prone and Restrained 6 rounds." },
            { CriticalTableId.Unbalancing, 13, "91-96", "Hard head strike. Push and Restrained 6 rounds. If no helm: Stable at 0 hit points and 0 wounds. Wakes in 1d4 hours with 1 hit point. Injured: Disadvantage on two ability scores and half Speed until healed." },
            { CriticalTableId.Unbalancing, 14, "97-99", "Brutal strike to belly. Prone. Drop anything carried in hands. Restrained 15 rounds." },
            { CriticalTableId.Unbalancing, 15, "101-106", "Blow breaks leg. Graze x2. Injured: Disadvantage on two ability scores and half Speed until healed. Restrained 1 round." },
            { CriticalTableId.Unbalancing, 16, "107-109", "Head slammed into stone. Prone and Dying." },
            { CriticalTableId.Unbalancing, 17, "111-116", "Great side shot. Push. Prone. Lower leg broken. Restrained 7 rounds. Injured: Disadvantage on one ability score and half Speed until healed." },
            { CriticalTableId.Unbalancing, 18, "117-119", "Shield shoulder struck. Restrained 9 rounds. If no shield: Prone and Incapacitated; arm broken & useless." },
        };

    [Theory]
    [MemberData(nameof(PhysicalUpperBands))]
    public void PhysicalUpperBandsKeepTheirMerpOutcome(
        CriticalTableId table,
        int index,
        string merpBand,
        string expected)
    {
        var outcome = Rules.GetCriticalOutcome(table, index);

        Assert.Equal(expected, outcome.Text);
        Assert.Matches(@"^\d{2,3}-\d{2,3}$", merpBand);
    }

    [Theory]
    [InlineData(CriticalTableId.Crush)]
    [InlineData(CriticalTableId.Slash)]
    [InlineData(CriticalTableId.Puncture)]
    [InlineData(CriticalTableId.Unbalancing)]
    public void PhysicalPaddingIndicesHaveNoCriticalEffect(CriticalTableId table)
    {
        for (var index = 1; index <= 5; index++)
        {
            var outcome = Rules.GetCriticalOutcome(table, index);
            Assert.Equal("No critical effect.", outcome.Text);
            Assert.Empty(outcome.Effects);
        }
    }

    [Theory]
    [InlineData(CriticalTableId.Crush)]
    [InlineData(CriticalTableId.Slash)]
    [InlineData(CriticalTableId.Puncture)]
    [InlineData(CriticalTableId.Unbalancing)]
    public void EveryPublishedPhysicalBandProducesStructuredMechanics(
        CriticalTableId table)
    {
        for (var index = 6; index <= 18; index++)
        {
            var outcome = Rules.GetCriticalOutcome(table, index);
            Assert.NotEmpty(outcome.Effects);
        }
    }

    [Fact]
    public void MasteriesAndConditionsAreEffectsRatherThanNarrativeOnly()
    {
        var crush = Rules.GetCriticalOutcome(CriticalTableId.Crush, 7);
        Assert.Contains(
            new TraumaEffect(TraumaEffectKind.AdditionalHits, Magnitude: 4),
            crush.Effects);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Sap), crush.Effects);
        Assert.DoesNotContain(
            crush.Effects,
            effect => effect.Kind is TraumaEffectKind.ForcedMovement or TraumaEffectKind.Push);

        var unbalancing = Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 12);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Prone), unbalancing.Effects);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Restrained,
                Duration: 6,
                DurationUnit: TraumaDurationUnit.Rounds),
            unbalancing.Effects);

        var pushed = Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 8);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Graze), pushed.Effects);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Push), pushed.Effects);
    }

    [Fact]
    public void SapUsesTheStandardNextAttackWindowRatherThanARoundCount()
    {
        var outcomes = new[]
        {
            Rules.GetCriticalOutcome(CriticalTableId.Crush, 7),
            Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 7),
            Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 10),
        };

        Assert.All(outcomes, outcome => Assert.Contains(
            outcome.Effects,
            effect => effect.Kind == TraumaEffectKind.Sap));
        Assert.All(
            outcomes.SelectMany(outcome => outcome.Effects)
                .Where(effect => effect.Kind == TraumaEffectKind.Sap),
            effect => Assert.Equal(TraumaDurationUnit.None, effect.DurationUnit));
    }

    [Fact]
    public void RevisedDamageInjuryAndRecoveryRulesAreStructured()
    {
        var crushTen = Rules.GetCriticalOutcome(CriticalTableId.Crush, 10);
        Assert.Contains(
            new TraumaEffect(TraumaEffectKind.Graze, Magnitude: 2),
            crushTen.Effects);
        Assert.DoesNotContain(
            crushTen.Effects,
            effect => effect.Kind == TraumaEffectKind.AdditionalHits);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Injured,
                Magnitude: 1,
                DurationUnit: TraumaDurationUnit.UntilHealed),
            crushTen.Effects);

        var punctureTen = Rules.GetCriticalOutcome(CriticalTableId.Puncture, 10);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Exhaustion,
                Magnitude: 2,
                DurationUnit: TraumaDurationUnit.UntilHealed),
            punctureTen.Effects);

        var headStrike = Rules.GetCriticalOutcome(CriticalTableId.Slash, 13);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.StableAtZero,
                Magnitude: 1,
                Duration: 1,
                DurationUnit: TraumaDurationUnit.D4Hours,
                Detail: "0 hit points and 0 wounds"),
            headStrike.Effects);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Injured,
                Magnitude: 2,
                DurationUnit: TraumaDurationUnit.UntilHealed),
            headStrike.Effects);
        Assert.DoesNotContain(
            headStrike.Effects,
            effect => effect.Kind == TraumaEffectKind.Unconscious);
    }

    [Fact]
    public void PhysicalDisplacementUsesPushRatherThanFreeformMovement()
    {
        var displaced = new[]
        {
            Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 8),
            Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 11),
            Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 13),
            Rules.GetCriticalOutcome(CriticalTableId.Unbalancing, 17),
        };

        Assert.All(displaced, outcome =>
            Assert.Contains(new TraumaEffect(TraumaEffectKind.Push), outcome.Effects));
        Assert.All(displaced, outcome =>
            Assert.DoesNotContain(
                outcome.Effects,
                effect => effect.Kind == TraumaEffectKind.ForcedMovement));
    }

    [Theory]
    // A signed modifier the parser has no rule for.
    [InlineData("Blow to side. +4 hits. Sap.", "Blow to side. -7 to morale. +4 hits.", "-7")]
    // A number bound to a mechanical unit.
    [InlineData("Blow to side. +4 hits. Sap.", "Blow to side. Slowed 3 rounds. +4 hits.", "3 rounds")]
    // A state-change verb with no supporting numbers.
    [InlineData("Blow to side. +4 hits. Sap.", "Blow to side. Target frozen 3 rounds. +4 hits.", "frozen")]
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
            "Blow to side. +4 hits. Sap.",
            "Ghastly flank laceration. Blow to side. +4 hits. Sap.");

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
        // Puncture lookup 7: immediate trauma damage plus conditional bleeding.
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Puncture, 7);
        Assert.Contains("3 hits per round", outcome.Text, StringComparison.Ordinal);

        var additional = outcome.Effects
            .Where(effect => effect.Kind == TraumaEffectKind.AdditionalHits)
            .ToArray();
        var bleeds = outcome.Effects
            .Where(effect => effect.Kind == TraumaEffectKind.Bleeding)
            .ToArray();

        Assert.Equal(3, Assert.Single(additional).Magnitude);
        Assert.Equal(3, Assert.Single(bleeds).Magnitude);
    }

    /// <summary>
    /// Catastrophic but survivable results enter the Dying state, while an
    /// explicitly instant death remains a Death effect.
    /// </summary>
    [Fact]
    public void DyingAndInstantDeathRemainDistinct()
    {
        // Crush Tier E index 6 -> lookup 14 (MERP band 97-99).
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Crush, CriticalTier.E, 6);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Prone), outcome.Effects);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Dying), outcome.Effects);
        Assert.DoesNotContain(outcome.Effects, effect => effect.Kind == TraumaEffectKind.Death);

        // Slash lookup 13 has a conditional instant death. It must not pick up a
        // spurious duration, and the NoHelm condition must survive parsing.
        var instant = Rules.GetCriticalOutcome(CriticalTableId.Slash, 13);
        Assert.Contains("dies instantly", instant.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Death,
                AppliesWhen: TraumaEffectCondition.NoHelm),
            instant.Effects);
    }

    [Fact]
    public void CrushedThroatUsesRestrainedAndSuffocation()
    {
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Crush, 16);

        Assert.Contains(new TraumaEffect(TraumaEffectKind.Restrained), outcome.Effects);
        Assert.Contains(new TraumaEffect(TraumaEffectKind.Suffocating), outcome.Effects);
        Assert.DoesNotContain(
            outcome.Effects,
            effect => effect.Kind is TraumaEffectKind.Stun or TraumaEffectKind.Death);
    }

    [Fact]
    public void SeveredSpineIsCapturedAsPermanentParalysis()
    {
        // Slash Tier E index 10 -> lookup 18 (MERP band 117-119).
        var outcome = Rules.GetCriticalOutcome(CriticalTableId.Slash, CriticalTier.E, 10);

        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Paralyzed,
                DurationUnit: TraumaDurationUnit.Permanent,
                Detail: "from the neck down"),
            outcome.Effects);
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
