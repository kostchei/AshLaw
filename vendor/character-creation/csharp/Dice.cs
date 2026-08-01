namespace Ash.Rules;

/// <summary>
/// The one source of chance the rules use, seeded and inspectable.
/// </summary>
public sealed class Dice
{
    private ulong _state;

    public Dice(ulong seed)
    {
        // Zero is the one state a xorshift generator cannot leave.
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public ulong State => _state;

    public static Dice FromState(ulong state) => new(state);

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

    public int PoolKeepHighest(int count, int sides, int keep)
    {
        if (keep < 0 || keep > count)
        {
            throw new ArgumentOutOfRangeException(nameof(keep));
        }

        var rolls = Pool(count, sides).OrderByDescending(value => value);
        return rolls.Take(keep).Sum();
    }

    private ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return unchecked(_state * 0x2545F4914F6CDD1DUL);
    }
}
