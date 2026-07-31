namespace Ash.Rules;

/// <summary>
/// The one source of chance the rules use, seeded and inspectable.
/// </summary>
/// <remarks>
/// Combat resolution and character creation must reproduce exactly from a seed:
/// the same request gives the same result, and a saved world resumes the same
/// rolls it would have made. A shared ambient generator cannot promise either,
/// so the state is explicit and small enough to save.
/// </remarks>
public sealed class Dice
{
    private ulong _state;

    public Dice(ulong seed)
    {
        // Zero is the one state a xorshift generator cannot leave.
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    /// <summary>The generator's state, which is what a save has to carry.</summary>
    public ulong State => _state;

    public static Dice FromState(ulong state) => new(state);

    /// <summary>One die of <paramref name="sides"/>, numbered from one.</summary>
    public int Roll(int sides)
    {
        if (sides < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sides));
        }

        return (int)(NextUInt64() % (ulong)sides) + 1;
    }

    public int D20() => Roll(20);

    public int D6() => Roll(6);

    /// <summary>Rolls a pool, in roll order.</summary>
    public IReadOnlyList<int> Pool(int count, int sides)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var rolls = new int[count];
        for (var index = 0; index < count; index++)
        {
            rolls[index] = Roll(sides);
        }

        return rolls;
    }

    /// <summary>
    /// Rolls a pool and sums its highest <paramref name="keep"/> dice, which is
    /// how the Unearthed Arcana method reads its 8d6 down to a score.
    /// </summary>
    public int PoolKeepHighest(int count, int sides, int keep)
    {
        if (keep < 0 || keep > count)
        {
            throw new ArgumentOutOfRangeException(nameof(keep));
        }

        var rolls = Pool(count, sides).OrderByDescending(value => value);
        return rolls.Take(keep).Sum();
    }

    /// <summary>xorshift64*: small, fast, and good enough for dice.</summary>
    private ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return unchecked(_state * 0x2545F4914F6CDD1DUL);
    }
}
