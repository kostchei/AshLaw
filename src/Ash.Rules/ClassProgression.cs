namespace Ash.Rules;

public enum CharacterClass
{
    Fighter,
    Cleric,
    Rogue,
    Wizard,
    Bard,
    Barbarian,
    Sorcerer,
    Paladin,
    CelestialWarlock,
    Monk,
    Ranger,
    Assassin,
}

/// <summary>
/// The standard fractional progression steps from
/// <c>class_progression_tables.md</c> §1.2: <c>{+1, +2/3, +1/2, +1/3}</c>, plus
/// the "Capped" state used by the Wizard's third bracket.
/// </summary>
public enum ProgressionRate
{
    Capped,
    OneThirdPerLevel,
    OneHalfPerLevel,
    TwoThirdsPerLevel,
    OnePerLevel,
}

/// <summary>
/// One 10-level Rolemaster bracket: the first level it covers and the rate that
/// applies inside it.
/// </summary>
public sealed record ProgressionBracket(int FirstLevel, ProgressionRate Rate);

/// <summary>
/// The bracket rules for one class, from <c>class_progression_tables.md</c> §2.
/// These are the <em>derivation</em> inputs. The published Lv 1-30 table in
/// <c>class_progression.csv</c> remains the authority; <c>ClassProgressionTests</c>
/// asserts the two agree.
/// </summary>
public sealed class ClassProgressionRules
{
    private readonly ProgressionBracket[] _brackets;

    internal ClassProgressionRules(
        CharacterClass characterClass,
        int hardCap,
        IReadOnlyList<ProgressionBracket> brackets)
    {
        Class = characterClass;
        HardCap = hardCap;
        _brackets = brackets.ToArray();
    }

    public CharacterClass Class { get; }

    public int HardCap { get; }

    public IReadOnlyList<ProgressionBracket> Brackets => _brackets;

    /// <summary>
    /// Derives the attack modifier at <paramref name="level"/> from the bracket
    /// rates alone, without consulting the published table.
    /// </summary>
    public int DeriveAttackModifier(int level)
    {
        if (level is < ClassProgressionTable.MinimumLevel or > ClassProgressionTable.MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                $"Level must be {ClassProgressionTable.MinimumLevel} through {ClassProgressionTable.MaximumLevel}.");
        }

        var total = 0;
        for (var index = 0; index < _brackets.Length; index++)
        {
            var bracket = _brackets[index];
            if (level < bracket.FirstLevel)
            {
                break;
            }

            var lastLevelOfBracket = index + 1 < _brackets.Length
                ? _brackets[index + 1].FirstLevel - 1
                : ClassProgressionTable.MaximumLevel;
            var levelsInBracket = Math.Min(level, lastLevelOfBracket) - bracket.FirstLevel + 1;
            total = checked(total + GainAfter(bracket.Rate, levelsInBracket));
        }

        return Math.Min(total, HardCap);
    }

    /// <summary>
    /// Total gained after <paramref name="levels"/> levels inside a bracket
    /// running at <paramref name="rate"/>.
    /// </summary>
    /// <remarks>
    /// Each rate is expressed as a repeating per-period gain pattern rather than
    /// as rounded arithmetic on <c>levels * rate</c>. No single rounding mode
    /// reproduces the published table: <c>+2/3</c> grants its first point on the
    /// bracket's opening level, while <c>+1/2</c> and <c>+1/3</c> grant theirs on
    /// the closing level of each period.
    /// </remarks>
    private static int GainAfter(ProgressionRate rate, int levels)
    {
        var pattern = GainPattern(rate);
        if (pattern.Length == 0)
        {
            return 0;
        }

        var wholePeriods = Math.DivRem(levels, pattern.Length, out var remainder);
        var total = wholePeriods * SumOf(pattern);
        for (var index = 0; index < remainder; index++)
        {
            total += pattern[index];
        }

        return total;
    }

    private static ReadOnlySpan<int> GainPattern(ProgressionRate rate) => rate switch
    {
        ProgressionRate.OnePerLevel => [1],
        ProgressionRate.TwoThirdsPerLevel => [1, 0, 1],
        ProgressionRate.OneHalfPerLevel => [0, 1],
        ProgressionRate.OneThirdPerLevel => [0, 0, 1],
        ProgressionRate.Capped => [],
        _ => throw new RulesResolutionException($"Unhandled progression rate '{rate}'."),
    };

    private static int SumOf(ReadOnlySpan<int> pattern)
    {
        var total = 0;
        foreach (var value in pattern)
        {
            total += value;
        }

        return total;
    }
}

/// <summary>
/// The published Lv 1-30 attack-modifier progression, loaded from
/// <c>class_progression.csv</c>. This table is the authority at runtime.
/// </summary>
public sealed class ClassProgressionTable
{
    public const int MinimumLevel = 1;
    public const int MaximumLevel = 30;

    private readonly IReadOnlyDictionary<(CharacterClass Class, int Level), int> _attackModifiers;
    private readonly IReadOnlyDictionary<CharacterClass, ClassProgressionRules> _rules;

    internal ClassProgressionTable(
        IDictionary<(CharacterClass Class, int Level), int> attackModifiers,
        IDictionary<CharacterClass, ClassProgressionRules> rules)
    {
        _attackModifiers = new Dictionary<(CharacterClass, int), int>(attackModifiers);
        _rules = new Dictionary<CharacterClass, ClassProgressionRules>(rules);
    }

    public int GetAttackModifier(CharacterClass characterClass, int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                $"Level must be {MinimumLevel} through {MaximumLevel}.");
        }

        return _attackModifiers.TryGetValue((characterClass, level), out var modifier)
            ? modifier
            : throw new RulesResolutionException(
                $"No published attack modifier for {characterClass} at level {level}.");
    }

    public ClassProgressionRules GetRules(CharacterClass characterClass) =>
        _rules.TryGetValue(characterClass, out var rules)
            ? rules
            : throw new RulesResolutionException($"Unknown character class '{characterClass}'.");

    /// <summary>
    /// The lowest level at which <paramref name="characterClass"/> reaches its
    /// hard cap, per the published table.
    /// </summary>
    public int GetCapLevel(CharacterClass characterClass)
    {
        var cap = GetRules(characterClass).HardCap;
        for (var level = MinimumLevel; level <= MaximumLevel; level++)
        {
            if (GetAttackModifier(characterClass, level) == cap)
            {
                return level;
            }
        }

        throw new RulesResolutionException(
            $"{characterClass} never reaches its hard cap of +{cap} by level {MaximumLevel}.");
    }
}
