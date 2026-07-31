namespace Ash.Rules.Tests;

/// <summary>
/// The armour dexterity rule, including the one place U8_Ash deliberately
/// differs from the vendored worked example. See ADR 0006.
/// </summary>
public sealed class DefenseDerivationTests
{
    private static AbilityBonusTable Bonuses { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName)).AbilityBonuses;

    [Theory]
    [InlineData(ArmorType.None, 0)]
    [InlineData(ArmorType.Leather, 0)]
    [InlineData(ArmorType.Chain, -4)]
    [InlineData(ArmorType.Plate, -8)]
    public void EachArmourCarriesTheTransribedPenalty(
        ArmorType armor,
        int expected)
    {
        Assert.Equal(expected, DefenseDerivation.DexterityPenaltyFor(armor));
    }

    [Fact]
    public void StrengthCarriesWeightButNeverAddsAgility()
    {
        // Strength offsets the suit; a surplus does not become a bonus.
        Assert.Equal(
            0,
            DefenseDerivation.EffectiveDexterityPenalty(ArmorType.Chain, 4));
        Assert.Equal(
            0,
            DefenseDerivation.EffectiveDexterityPenalty(ArmorType.Chain, 9));
        Assert.Equal(
            -4,
            DefenseDerivation.EffectiveDexterityPenalty(ArmorType.Plate, 4));
        Assert.Equal(
            0,
            DefenseDerivation.EffectiveDexterityPenalty(ArmorType.Leather, 0));
    }

    [Fact]
    public void PlateTakesAgilityAwayButDoesNotInvertIt()
    {
        // ADR 0006: the vendored rules' worked example carries this to -1.
        // U8_Ash floors it at zero — armour can stop agility helping, it cannot
        // make a character clumsier than one with no agility at all.
        Assert.Equal(
            0,
            DefenseDerivation.DexterityContribution(
                ArmorType.Plate,
                dexterityBonus: 3,
                strengthBonus: 4));

        // The same character with nothing to lose is no worse off.
        Assert.Equal(
            0,
            DefenseDerivation.DexterityContribution(
                ArmorType.Plate,
                dexterityBonus: 0,
                strengthBonus: 4));
    }

    [Fact]
    public void ChainUnlocksFullAgilityAtStrengthEighteen()
    {
        // Chain's -4 is exactly offset by a +4 bonus, which is Strength 18.
        Assert.Equal(4, Bonuses.BonusFor(18));
        Assert.Equal(
            2,
            DefenseDerivation.DexterityContribution(
                ArmorType.Chain,
                dexterityBonus: 2,
                strengthBonus: Bonuses.BonusFor(18)));

        // A point short and the suit eats one point of it.
        Assert.Equal(
            1,
            DefenseDerivation.DexterityContribution(
                ArmorType.Chain,
                dexterityBonus: 2,
                strengthBonus: Bonuses.BonusFor(16)));
    }

    [Fact]
    public void NoMortalStrengthFullyOffsetsPlate()
    {
        var strongest = Bonuses.BonusFor(AbilityScores.MaximumScore);

        Assert.Equal(5, strongest);
        Assert.Equal(
            -3,
            DefenseDerivation.EffectiveDexterityPenalty(
                ArmorType.Plate,
                strongest));
        Assert.Equal(
            2,
            DefenseDerivation.DexterityContribution(
                ArmorType.Plate,
                dexterityBonus: 5,
                strengthBonus: strongest));
    }

    [Fact]
    public void TheDefenseModifierAddsArmourAgilityShieldAndParry()
    {
        var defense = DefenseDerivation.DefenseModifier(
            ArmorType.Leather,
            baseDefense: 2,
            dexterityBonus: 3,
            strengthBonus: 0,
            shieldModifier: 2,
            parryModifier: 1);

        Assert.Equal(8, defense);
        Assert.Equal(
            5,
            DefenseDerivation.DefenseModifier(
                ArmorType.Plate,
                baseDefense: 5,
                dexterityBonus: 3,
                strengthBonus: 4));
    }
}
