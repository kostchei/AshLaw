namespace Ash.Rules.Tests;

public sealed class MovementAllowanceTests
{
    [Fact]
    public void TheDefaultMoveIsThirtyFeetOfFiveFootTiles()
    {
        Assert.Equal(30, MovementAllowance.FeetPerRound);
        Assert.Equal(5, MovementAllowance.FeetPerTile);
        Assert.Equal(6, MovementAllowance.StepsPerRound);
    }

    [Fact]
    public void TheMoveDividesIntoWholeTiles()
    {
        // StepsPerRound is integer division of two constants, so a rate that
        // does not divide evenly would silently lose the remainder rather than
        // fail. This is the only place that can catch it.
        Assert.Equal(
            0,
            MovementAllowance.FeetPerRound % MovementAllowance.FeetPerTile);
    }

    [Fact]
    public void AMoveAndAnAttackAreSeparateAllowancesOfTheSameRound()
    {
        // Six seconds holds both: thirty feet of ground and, at the base rate,
        // one attack. Neither is measured in the other's units.
        Assert.Equal(6000, AttackSpeed.RoundMilliseconds);
        Assert.Equal(
            1,
            AttackSpeed.AttacksPerRound(
                new AttackSpeedInputs(CharacterClass.Fighter, 1, false)));
        Assert.Equal(6, MovementAllowance.StepsPerRound);
    }
}
