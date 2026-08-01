namespace Ash.Sim;

/// <summary>
/// The world's day, counted in the same 200 ms beats a fight is counted in.
/// </summary>
/// <remarks>
/// Two separate things need to know what time it is: a schedule, which sends an
/// NPC somewhere different at dusk, and a spell mishap, which locks a spell away
/// "until next dawn". Both are answered from the combat beat rather than from a
/// clock of their own, so a save that restores one beat restores the time of day
/// with it and cannot land a world half a day out of step with its own NPCs.
/// </remarks>
public static class WorldCalendar
{
    public const int HoursPerDay = 24;

    public const int BeatsPerHour = ConditionTiming.BeatsPerHour;

    public const long BeatsPerDay = (long)HoursPerDay * BeatsPerHour;

    /// <summary>The hour a night ends: the boundary a mishap lockout clears on.</summary>
    public const int DawnHour = 6;

    /// <summary>
    /// Beat zero is dawn of day zero. A world therefore starts in the morning,
    /// which is where a schedule's daytime block wants it, rather than at
    /// midnight with every authored NPC asleep.
    /// </summary>
    public static int HourOf(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        return (int)(((tick / BeatsPerHour) + DawnHour) % HoursPerDay);
    }

    public static long DayOf(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        return tick / BeatsPerDay;
    }

    /// <summary>
    /// The first beat of the next dawn strictly after <paramref name="tick"/>.
    /// Standing exactly on a dawn beat still yields the following one: a lockout
    /// taken at first light lasts the day it was earned in.
    /// </summary>
    public static long NextDawnTick(long tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        return checked((DayOf(tick) + 1) * BeatsPerDay);
    }

    /// <summary>
    /// Whether <paramref name="hour"/> falls in the half-open window
    /// [<paramref name="fromHour"/>, <paramref name="toHour"/>), which may wrap
    /// past midnight.
    /// </summary>
    public static bool InWindow(int hour, int fromHour, int toHour)
    {
        ValidateHour(hour, nameof(hour));
        ValidateHour(fromHour, nameof(fromHour));
        ValidateHour(toHour, nameof(toHour));
        if (fromHour == toHour)
        {
            throw new ArgumentException(
                "An empty or whole-day window must be written as 0 to 24.",
                nameof(toHour));
        }

        return fromHour < toHour
            ? hour >= fromHour && hour < toHour
            : hour >= fromHour || hour < toHour;
    }

    private static void ValidateHour(int hour, string parameter)
    {
        if (hour is < 0 or > HoursPerDay)
        {
            throw new ArgumentOutOfRangeException(parameter, hour, "Hours run 0 to 24.");
        }
    }
}
