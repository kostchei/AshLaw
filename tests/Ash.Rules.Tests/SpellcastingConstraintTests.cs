namespace Ash.Rules.Tests;

public sealed class SpellcastingConstraintTests
{
    private static SpellcastingRules Spellcasting =>
        RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory).Spellcasting;

    /// <summary>Five virtue scores that satisfy the published 3-at-15 / 2-at-11 rule.</summary>
    private static readonly int[] CompliantVirtues = [16, 15, 15, 12, 11];

    [Fact]
    public void ArmorRestrictionsAreLoadedForEveryTradition()
    {
        var spellcasting = Spellcasting;

        Assert.Equal(
            Enum.GetValues<CastingTradition>().Length,
            spellcasting.ArmorRestrictions.Count);

        var wizard = spellcasting.GetArmorRestriction(CastingTradition.Wizard);
        Assert.Equal(AllowedArmor.None, wizard.Armor);
        Assert.Equal(AllowedShields.None, wizard.Shields);

        var druid = spellcasting.GetArmorRestriction(CastingTradition.Druid);
        Assert.Equal(AllowedArmor.NonMetal, druid.Armor);
        Assert.Equal(AllowedShields.WoodenOnly, druid.Shields);

        var cleric = spellcasting.GetArmorRestriction(CastingTradition.Cleric);
        Assert.Equal(AllowedArmor.All, cleric.Armor);
        Assert.Equal(AllowedShields.All, cleric.Shields);
    }

    // "Wizard spells cannot be cast in any armor or with shields."
    [Theory]
    [InlineData(ArmorType.None, ShieldKind.None, true)]
    [InlineData(ArmorType.Leather, ShieldKind.None, false)]
    [InlineData(ArmorType.Chain, ShieldKind.None, false)]
    [InlineData(ArmorType.Plate, ShieldKind.None, false)]
    [InlineData(ArmorType.None, ShieldKind.Wooden, false)]
    [InlineData(ArmorType.None, ShieldKind.Metal, false)]
    public void WizardMayOnlyCastUnarmoredAndUnshielded(
        ArmorType armor,
        ShieldKind shield,
        bool expected)
    {
        var check = Spellcasting.CanCast(CastingTradition.Wizard, armor, shield);

        Assert.Equal(expected, check.IsAllowed);
    }

    // "Druid spells cannot be cast in metal armor or with metal shields."
    [Theory]
    [InlineData(ArmorType.None, ShieldKind.None, true)]
    [InlineData(ArmorType.Leather, ShieldKind.None, true)]
    [InlineData(ArmorType.Leather, ShieldKind.Wooden, true)]
    [InlineData(ArmorType.Chain, ShieldKind.None, false)]
    [InlineData(ArmorType.Plate, ShieldKind.Wooden, false)]
    [InlineData(ArmorType.Leather, ShieldKind.Metal, false)]
    public void DruidMayCastInNonMetalArmorWithWoodenShields(
        ArmorType armor,
        ShieldKind shield,
        bool expected)
    {
        var check = Spellcasting.CanCast(CastingTradition.Druid, armor, shield);

        Assert.Equal(expected, check.IsAllowed);
    }

    // "Clerics may wear any armor subject to deity alignment maintenance."
    [Theory]
    [InlineData(ArmorType.Plate, ShieldKind.Metal)]
    [InlineData(ArmorType.Chain, ShieldKind.Wooden)]
    [InlineData(ArmorType.None, ShieldKind.None)]
    public void ClericArmorIsUnrestrictedWhenAlignmentHolds(ArmorType armor, ShieldKind shield)
    {
        var check = Spellcasting.CanCast(
            CastingTradition.Cleric,
            armor,
            shield,
            CompliantVirtues);

        Assert.True(check.IsAllowed, check.Reason);
    }

    [Fact]
    public void ClericVirtueRequirementMatchesThePublishedThresholds()
    {
        var virtues = Spellcasting.ClericVirtues;

        Assert.Equal(5, virtues.RequiredVirtueCount);
        Assert.Equal(new VirtueThreshold(15, 3), virtues.Primary);
        Assert.Equal(new VirtueThreshold(11, 2), virtues.Secondary);
        Assert.Contains("divine spellcasting", virtues.FailureEffect, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Exactly at both thresholds: three at 15, two at 11.
    [InlineData(new[] { 15, 15, 15, 11, 11 }, true)]
    [InlineData(new[] { 20, 18, 15, 14, 11 }, true)]
    // Only two virtues reach the primary threshold of 15, though all clear 11.
    [InlineData(new[] { 15, 15, 14, 14, 14 }, false)]
    // Three reach 15, but a remaining virtue has slipped below 11.
    [InlineData(new[] { 15, 15, 15, 11, 10 }, false)]
    // Nothing reaches either threshold.
    [InlineData(new[] { 10, 10, 10, 10, 10 }, false)]
    public void ClericAlignmentIsEnforcedAgainstVirtueScores(int[] scores, bool expected)
    {
        var check = Spellcasting.ClericVirtues.Check(scores);

        Assert.Equal(expected, check.IsAllowed);
        if (!expected)
        {
            Assert.Contains("Deity alignment lost", check.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FallenClericCannotCastEvenInPermittedArmor()
    {
        var check = Spellcasting.CanCast(
            CastingTradition.Cleric,
            ArmorType.Plate,
            ShieldKind.Metal,
            [10, 10, 10, 10, 10]);

        Assert.False(check.IsAllowed);
        Assert.Contains(
            Spellcasting.ClericVirtues.FailureEffect,
            check.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WrongNumberOfVirtueScoresThrows()
    {
        var virtues = Spellcasting.ClericVirtues;

        Assert.Throws<ArgumentException>(() => virtues.Check([15, 15, 15, 11]));
        Assert.Throws<ArgumentException>(() => virtues.Check([15, 15, 15, 11, 11, 11]));
    }

    [Fact]
    public void VirtueScoresAreRequiredForClericsAndRejectedForOthers()
    {
        var spellcasting = Spellcasting;

        Assert.Throws<ArgumentNullException>(
            () => spellcasting.CanCast(CastingTradition.Cleric, ArmorType.None, ShieldKind.None));
        Assert.Throws<ArgumentException>(
            () => spellcasting.CanCast(
                CastingTradition.Wizard,
                ArmorType.None,
                ShieldKind.None,
                CompliantVirtues));
    }

    [Fact]
    public void NaturalOneMishapLocksOutOnlyThatSpellUntilDawn()
    {
        var spellcasting = Spellcasting;

        Assert.Equal(1, spellcasting.MishapRawD20);
        Assert.Equal(MishapLockoutScope.ThatSpellOnly, spellcasting.MishapLockoutScope);
        Assert.Equal(MishapLockoutDuration.UntilNextDawn, spellcasting.MishapLockoutDuration);
        Assert.Contains("next dawn", spellcasting.MishapConsequence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AttackOfOpportunityRulesAreLoaded()
    {
        var spellcasting = Spellcasting;

        Assert.True(spellcasting.ProvokesInMelee);
        Assert.True(spellcasting.InterruptionOnCriticalOrStun);
    }

    [Theory]
    [InlineData(ArmorType.Plate, true)]
    [InlineData(ArmorType.Chain, true)]
    [InlineData(ArmorType.Leather, false)]
    [InlineData(ArmorType.None, false)]
    public void MetalArmorClassificationDrivesTheDruidRestriction(ArmorType armor, bool isMetal)
    {
        Assert.Equal(isMetal, ArmorRestriction.IsMetal(armor));
    }
}
