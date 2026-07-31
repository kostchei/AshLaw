namespace Ash.Rules;

/// <summary>
/// How far a combatant may move inside the six-second round.
/// </summary>
/// <remarks>
/// The round holds a maximum move and a maximum number of attacks. Movement is
/// not free and it is not unlimited: you may move and attack, or move and cast,
/// but only so far.
///
/// The rate is thirty feet, and the grid is what converts it into steps. Feet
/// are the authored unit — "speed = 0" while restrained and "costs half
/// movement to stand" are both written about a distance, not about a number of
/// tiles — so the distance is the constant and the tile count is derived. A
/// per-creature Speed stat would replace <see cref="FeetPerRound"/> and leave
/// the conversion below untouched.
/// </remarks>
public static class MovementAllowance
{
    /// <summary>The default move: thirty feet in the six-second round.</summary>
    public const int FeetPerRound = 30;

    /// <summary>
    /// How much ground one tile covers. Five feet, so a creature that occupies
    /// a tile occupies five feet of it.
    /// </summary>
    public const int FeetPerTile = 5;

    /// <summary>
    /// Tiles a combatant may cross in one round: six, at thirty feet over
    /// five-foot tiles.
    /// </summary>
    /// <remarks>
    /// Integer division truncates, so a move that does not divide into whole
    /// tiles would quietly lose the remainder. Nothing here can catch that —
    /// the values are compile-time constants and any guard would be inlined
    /// away — so the divisibility is asserted in the rules tests instead,
    /// where it actually runs.
    /// </remarks>
    public const int StepsPerRound = FeetPerRound / FeetPerTile;
}
