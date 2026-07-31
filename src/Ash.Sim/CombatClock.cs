namespace Ash.Sim;

/// <summary>
/// The combat clock: a 200 ms beat derived from the fixed physics tick.
/// </summary>
/// <remarks>
/// Gravity runs at the renderer's physics rate — 60 Hz — because a falling
/// object moving five times a second is a slideshow. Combat wants a far coarser
/// beat: a swing takes six seconds, and everything a fight schedules is a whole
/// number of 200 ms steps. Rather than rate two independent timers, the combat
/// tick counts physics ticks, so one clock drives the world and a saved
/// simulation tick still describes the whole of it.
/// </remarks>
public sealed class CombatClock
{
    /// <summary>One combat beat.</summary>
    public const int TickMilliseconds = 200;

    /// <summary>
    /// The physics rate this clock divides. Godot's default, and the rate
    /// <c>_PhysicsProcess</c> calls the simulation at; a project that changes
    /// <c>physics/common/physics_ticks_per_second</c> must pass the new rate to
    /// the constructor rather than leave the two disagreeing.
    /// </summary>
    public const int DefaultPhysicsTicksPerSecond = 60;

    private readonly int _physicsTicksPerBeat;

    public CombatClock(
        int physicsTicksPerSecond = DefaultPhysicsTicksPerSecond,
        long startPhysicsTick = 0)
    {
        if (physicsTicksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicsTicksPerSecond));
        }

        if (startPhysicsTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startPhysicsTick));
        }

        // A physics rate that does not divide into whole 200 ms beats would make
        // every swing drift by a fraction of a tick, and the drift would differ
        // between a machine that ran the fight in one session and one that
        // loaded it halfway. Refuse the rate instead.
        var product = checked(physicsTicksPerSecond * TickMilliseconds);
        if (product % 1000 != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicsTicksPerSecond),
                physicsTicksPerSecond,
                $"A physics rate must divide into whole {TickMilliseconds} ms " +
                "combat beats.");
        }

        _physicsTicksPerBeat = product / 1000;
        Tick = startPhysicsTick / _physicsTicksPerBeat;
    }

    /// <summary>Physics ticks per combat beat: twelve, at 60 Hz.</summary>
    public int PhysicsTicksPerBeat => _physicsTicksPerBeat;

    /// <summary>Combat beats elapsed since the world began.</summary>
    public long Tick { get; private set; }

    /// <summary>
    /// Reads the beat off the physics tick, and reports whether that tick
    /// completed a new one. Callers advance combat only when this is true.
    /// </summary>
    /// <remarks>
    /// The physics tick is the authority rather than a counter of its own,
    /// because the physics tick is what a save carries. A clock counting its
    /// own beats would resume a loaded world up to eleven physics ticks out of
    /// step with the world it is pacing; a division cannot.
    /// </remarks>
    public bool Advance(long physicsTick)
    {
        if (physicsTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(physicsTick));
        }

        var beat = physicsTick / _physicsTicksPerBeat;
        if (beat == Tick)
        {
            return false;
        }

        if (beat < Tick)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicsTick),
                physicsTick,
                $"The physics tick went backwards: beat {beat} is before the " +
                $"clock's {Tick}.");
        }

        Tick = beat;
        return true;
    }

    /// <summary>
    /// Whole combat beats in a duration. The duration must land on a beat: an
    /// interval the clock cannot express is a design error in the caller, not
    /// something to round.
    /// </summary>
    public static int BeatsIn(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        if (milliseconds % TickMilliseconds != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(milliseconds),
                milliseconds,
                $"A combat duration must be a whole number of " +
                $"{TickMilliseconds} ms beats.");
        }

        return milliseconds / TickMilliseconds;
    }
}
