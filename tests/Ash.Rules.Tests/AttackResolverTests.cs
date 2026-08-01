namespace Ash.Rules.Tests;

public sealed class AttackResolverTests
{
    private readonly RulesData _rules =
        RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);

    [Fact]
    public void RawTwentyUsesTraumaIndexTen()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.OneHandedSlashing,
                0,
                0,
                ArmorType.None));

        Assert.Equal(CriticalTier.B, result.CriticalTier);
        Assert.Equal(CriticalTableId.Slash, result.CriticalTable);
        Assert.Equal(10, result.TraumaIndex);
    }

    [Fact]
    public void CriticalTierIsCappedAtE()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.OneHandedSlashing,
                30,
                0,
                ArmorType.None));

        Assert.Equal(CriticalTier.E, result.CriticalTier);
    }

    [Fact]
    public void ModifiersCanRaiseTheNetResultAboveTwenty()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.TwoHandedWeapons,
                10,
                0,
                ArmorType.None,
                CriticalTableId.Slash));

        Assert.Equal(30, result.NetRoll);
        Assert.Equal(48, result.ConcussionHits);
        Assert.Equal(CriticalTier.E, result.CriticalTier);
    }

    [Fact]
    public void SmallToothAndClawCapsTheResultAndCritical()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.ToothAndClaw,
                30,
                0,
                ArmorType.None,
                CriticalTableId.Puncture,
                AttackSize: AttackSize.Small));

        Assert.Equal(21, result.NetRoll);
        Assert.Equal(16, result.ConcussionHits);
        Assert.Equal(CriticalTier.B, result.CriticalTier);
        Assert.Contains(
            result.Messages,
            message => message.Contains(
                "Small attack caps the net roll at 21",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AWeaponCriticalCapCanBeLowerThanTheAttackSizeCap()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.ToothAndClaw,
                30,
                0,
                ArmorType.None,
                CriticalTableId.Unbalancing,
                AttackSize: AttackSize.Small,
                MaximumCriticalTier: CriticalTier.A));

        Assert.Equal(CriticalTier.A, result.CriticalTier);
    }

    [Fact]
    public void AHighDaggerResultCannotExceedTierC()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.OneHandedSlashing,
                30,
                0,
                ArmorType.None,
                CriticalTableId.Puncture,
                MaximumCriticalTier: CriticalTier.C));

        Assert.Equal(50, result.NetRoll);
        Assert.Equal(CriticalTier.C, result.CriticalTier);
    }

    [Fact]
    public void NaturalOneSpellMishapOverridesAHighNetRoll()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                1,
                AttackCategoryId.SpellBolts,
                50,
                0,
                ArmorType.None,
                IsSpell: true));

        Assert.True(result.Mishap);
        Assert.False(result.Hit);
        Assert.Equal(0, result.ConcussionHits);
        Assert.Null(result.CriticalTier);
        Assert.Contains("Spell Mishap", result.Messages.Single(message => message.Contains("Mishap")));
    }

    [Fact]
    public void AmbiguousPhysicalCriticalRequiresAnExplicitTable()
    {
        var request = new AttackRequest(
            20,
            AttackCategoryId.TwoHandedWeapons,
            5,
            0,
            ArmorType.Plate);

        var exception = Assert.Throws<RulesResolutionException>(
            () => AttackResolver.Resolve(_rules, request));

        Assert.Contains("Supply AttackRequest.CriticalTable", exception.Message);
    }

    [Fact]
    public void AmbiguousPhysicalCriticalUsesWeaponProvidedTable()
    {
        var result = AttackResolver.Resolve(
            _rules,
            new AttackRequest(
                20,
                AttackCategoryId.TwoHandedWeapons,
                5,
                0,
                ArmorType.Plate,
                CriticalTableId.Crush));

        Assert.Equal(CriticalTableId.Crush, result.CriticalTable);
        Assert.Equal(CriticalTier.B, result.CriticalTier);
        Assert.Equal(10, result.TraumaIndex);
    }

    [Fact]
    public void ResolutionAndTraceAreCultureIndependent()
    {
        var request = new AttackRequest(
            17,
            AttackCategoryId.GrapplingUnbalancing,
            5,
            0,
            ArmorType.Leather);
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
            var french = AttackResolver.Resolve(_rules, request);

            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var english = AttackResolver.Resolve(_rules, request);

            Assert.Equal(french.NetRoll, english.NetRoll);
            Assert.Equal(french.ConcussionHits, english.ConcussionHits);
            Assert.Equal(french.CriticalTier, english.CriticalTier);
            Assert.Equal(french.Messages, english.Messages);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void RawRollOutsideD20RangeIsRejected(int rawD20)
    {
        var request = new AttackRequest(
            rawD20,
            AttackCategoryId.OneHandedSlashing,
            0,
            0,
            ArmorType.None);

        Assert.Throws<ArgumentOutOfRangeException>(() => AttackResolver.Resolve(_rules, request));
    }
}
