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

/// <summary>
/// The six ability scores and the one curve that turns them into bonuses.
/// </summary>
/// <remarks>
/// <para>
/// <c>bonus = floor((score - 10) / 2)</c>, with scores from 3 to 20, so a
/// mortal tops out at +5 before magic.
/// </para>
/// <para>
/// This matters beyond attack rolls: armour offsets its dexterity penalty with
/// the strength bonus. Chain's -4 is fully offset at Strength 18; plate's -8
/// never is, because +8 is past the mortal ceiling. What plate takes is the
/// dexterity bonus itself, down to zero and no further — see
/// <see cref="DefenseDerivation"/>.
/// </para>
/// </remarks>
public sealed record AbilityScores
{
    public const int MinimumScore = 3;

    /// <summary>Generation never exceeds 18; talents carry a score to 20.</summary>
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

    /// <summary>The scores in canonical order: STR, DEX, CON, INT, WIS, CHA.</summary>
    public IReadOnlyList<int> InOrder =>
    [
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma,
    ];

    /// <summary>The bonus a score grants: <c>floor((score - 10) / 2)</c>.</summary>
    public static int BonusFor(int score)
    {
        var offset = score - 10;

        // Integer division truncates toward zero, which would make -1 out of
        // -3; the curve floors, so a score of 9 is -1 and a score of 3 is -4.
        return offset >= 0 ? offset / 2 : (offset - 1) / 2;
    }

    public int BonusOf(Ability ability) => BonusFor(this[ability]);

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
