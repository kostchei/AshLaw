namespace Ash.Rules.Tests;

public sealed class EnvironmentalHazardTests
{
    private static AbilityBonusTable Bonuses { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName))
        .AbilityBonuses;

    [Fact]
    public void CrackedFloorUsesDexterityAndOnlyFailureDealsOneD4Damage()
    {
        var scores = new AbilityScores(10, 10, 10, 10, 10, 10);
        var sawSuccess = false;
        var sawFailure = false;

        for (ulong seed = 1; seed <= 100; seed++)
        {
            var crossing = EnvironmentalHazard.CrossCrackedFloor(
                new Dice(seed),
                scores,
                Bonuses);

            Assert.Equal(crossing.Check.Total >= crossing.Difficulty, crossing.Avoided);
            if (crossing.Avoided)
            {
                sawSuccess = true;
                Assert.Equal(0, crossing.Damage);
            }
            else
            {
                sawFailure = true;
                Assert.InRange(crossing.Damage, 1, EnvironmentalHazard.DamageDieSides);
            }
        }

        Assert.True(sawSuccess);
        Assert.True(sawFailure);
    }

    [Fact]
    public void ReflexBonusIsAppliedExactlyOnce()
    {
        var scores = new AbilityScores(10, 10, 10, 10, 10, 10);
        const int bonus = 3;

        var normal = EnvironmentalHazard.CrossCrackedFloor(
            new Dice(42),
            scores,
            Bonuses);
        var helped = EnvironmentalHazard.CrossCrackedFloor(
            new Dice(42),
            scores,
            Bonuses,
            reflexBonus: bonus);

        Assert.Equal(normal.Check.Roll, helped.Check.Roll);
        Assert.Equal(normal.Check.Bonus + bonus, helped.Check.Bonus);
        Assert.Equal(normal.Check.Total + bonus, helped.Check.Total);
        Assert.Equal(helped.Check.Total >= helped.Difficulty, helped.Avoided);
    }
}
