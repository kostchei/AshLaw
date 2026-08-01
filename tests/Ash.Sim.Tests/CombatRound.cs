namespace Ash.Sim.Tests;

/// <summary>
/// Letting a fight's clock run, for tests about something other than timing.
/// </summary>
/// <remarks>
/// A swing costs a six-second round, so two blows in a row need the round
/// between them to actually elapse. Tests that care about looting a corpse
/// rather than about pacing say so with this instead of reaching into ticks.
/// </remarks>
internal static class CombatRound
{
    private const int MaxPhysicsTicks = 10_000;

    /// <summary>
    /// Runs the world until the player may swing again. The bound is generous
    /// and exists only so a broken clock fails as a test rather than a hang.
    /// </summary>
    public static void WaitForPlayerSwing(PlayableSliceWorld world) =>
        WaitFor(
            world,
            () => world.Combat.PlayerCanSwing,
            "the player's swing to come back round");

    public static void WaitForPlayerImpact(PlayableSliceWorld world) =>
        WaitFor(
            world,
            () => world.Combat.ActiveAttackOf(world.PlayerId) is null,
            "the player's attack to reach impact");

    /// <summary>
    /// One step, waiting first for the round's move to allow it. Tests that
    /// walk somewhere are about what is at the far end, not about the six
    /// tiles a round covers, so they let the clock run instead of asserting
    /// the budget.
    /// </summary>
    public static SliceActionResult Step(
        PlayableSliceWorld world,
        int deltaX,
        int deltaY)
    {
        WaitFor(
            world,
            () => world.Combat.PlayerCanStep,
            "the round's move to refill");
        return world.MovePlayer(deltaX, deltaY);
    }

    private static void WaitFor(
        PlayableSliceWorld world,
        Func<bool> ready,
        string what)
    {
        for (var tick = 0; tick < MaxPhysicsTicks; tick++)
        {
            if (ready())
            {
                return;
            }

            world.AdvancePhysics();
        }

        throw new InvalidOperationException(
            $"Waited {MaxPhysicsTicks} physics ticks for {what}, and it " +
            "never happened.");
    }
}
