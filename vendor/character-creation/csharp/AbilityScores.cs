namespace Ash.Rules;

public enum Ability
{
    Strength = 0,
    Dexterity = 1,
    Constitution = 2,
    Intelligence = 3,
    Wisdom = 4,
    Charisma = 5,
}

[Flags]
public enum AbilityMask
{
    None = 0,
    Strength = 1 << Ability.Strength,
    Dexterity = 1 << Ability.Dexterity,
    Constitution = 1 << Ability.Constitution,
    Intelligence = 1 << Ability.Intelligence,
    Wisdom = 1 << Ability.Wisdom,
    Charisma = 1 << Ability.Charisma,
    All = Strength | Dexterity | Constitution | Intelligence | Wisdom | Charisma,
}

public static class AbilityMaskExtensions
{
    public static AbilityMask AsMask(this Ability ability) =>
        (AbilityMask)(1 << (int)ability);

    public static bool Includes(this AbilityMask mask, Ability ability) =>
        (mask & ability.AsMask()) != 0;

    public static int Count(this AbilityMask mask) =>
        System.Numerics.BitOperations.PopCount((uint)mask);

    public static IReadOnlyList<Ability> Abilities(this AbilityMask mask) =>
        Enum.GetValues<Ability>()
            .Where(ability => mask.Includes(ability))
            .ToArray();
}

public sealed record AbilityScores
{
    public const int MinimumScore = 3;
    public const int MaximumScore = 20;
    public const int AbilityCount = 6;

    public AbilityScores(
        int strength,
        int dexterity,
        int constitution,
        int intelligence,
        int wisdom,
        int charisma)
    {
        Strength = Validate(strength, nameof(strength));
        Dexterity = Validate(dexterity, nameof(dexterity));
        Constitution = Validate(constitution, nameof(constitution));
        Intelligence = Validate(intelligence, nameof(intelligence));
        Wisdom = Validate(wisdom, nameof(wisdom));
        Charisma = Validate(charisma, nameof(charisma));
    }

    public int Strength { get; }
    public int Dexterity { get; }
    public int Constitution { get; }
    public int Intelligence { get; }
    public int Wisdom { get; }
    public int Charisma { get; }

    public int this[Ability ability] => ability switch
    {
        Ability.Strength => Strength,
        Ability.Dexterity => Dexterity,
        Ability.Constitution => Constitution,
        Ability.Intelligence => Intelligence,
        Ability.Wisdom => Wisdom,
        Ability.Charisma => Charisma,
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };

    public IReadOnlyList<int> InOrder =>
    [
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma,
    ];

    public static AbilityScores FromOrder(IReadOnlyList<int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count != AbilityCount)
        {
            throw new ArgumentException(
                $"Six scores are required, not {scores.Count}.",
                nameof(scores));
        }

        return new AbilityScores(
            scores[0],
            scores[1],
            scores[2],
            scores[3],
            scores[4],
            scores[5]);
    }

    public AbilityScores With(Ability ability, int score)
    {
        var scores = InOrder.ToArray();
        scores[(int)ability] = score;
        return FromOrder(scores);
    }

    public override string ToString() =>
        $"STR {Strength} DEX {Dexterity} CON {Constitution} " +
        $"INT {Intelligence} WIS {Wisdom} CHA {Charisma}";

    private static int Validate(int score, string parameterName) =>
        score is >= MinimumScore and <= MaximumScore
            ? score
            : throw new ArgumentOutOfRangeException(
                parameterName,
                score,
                $"Ability scores run from {MinimumScore} to {MaximumScore}.");
}
