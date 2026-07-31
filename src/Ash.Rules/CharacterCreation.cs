namespace Ash.Rules;

/// <summary>
/// Ancestry. Only humans for now: a human takes two rolls on their class talent
/// table where another ancestry would take one and spend the rest on being
/// something other than human.
/// </summary>
public enum Ancestry
{
    Human = 0,
}

public enum CharacterCreationMethod
{
    /// <summary>3d6 in order, reroll the set until it is playable, then choose.</summary>
    Ironman = 0,

    /// <summary>Choose first; the class decides which stats get the fat dice.</summary>
    UnearthedArcana = 1,
}

/// <summary>A generated character, and how it was generated.</summary>
public sealed record CreatedCharacter(
    AbilityScores Scores,
    CharacterClass Class,
    Ancestry Ancestry,
    CharacterCreationMethod Method,
    int TalentRolls,
    int Attempts);

/// <summary>
/// Character creation: the two ways to roll a character, and the class stat
/// priorities the second one needs.
/// </summary>
/// <remarks>
/// These rules are U8_Ash's own, not part of the vendored Ash V1 package, which
/// is why they live in code here rather than in <c>vendor/</c>. They move to a
/// data file when there is more than one ancestry to configure.
/// </remarks>
public static class CharacterCreation
{
    /// <summary>A human's two rolls on the class talent table.</summary>
    public const int HumanTalentRolls = 2;

    /// <summary>An Ironman set needs at least this many scores of 15 or more.</summary>
    public const int IronmanHighScoresRequired = 2;

    public const int IronmanHighScore = 15;

    /// <summary>An Ironman set tolerates at most one score below this.</summary>
    public const int IronmanLowScore = 6;

    public const int IronmanLowScoresAllowed = 1;

    /// <summary>
    /// The Unearthed Arcana dice pools, biggest first. Each keeps its three
    /// highest dice, so the first stat in a class's order is reliably strong
    /// and the last is whatever the dice say.
    /// </summary>
    public static readonly IReadOnlyList<int> UnearthedArcanaPools =
        [8, 7, 6, 5, 4, 3];

    private static readonly Dictionary<CharacterClass, IReadOnlyList<Ability>>
        Priorities = new()
        {
            [CharacterClass.Fighter] =
            [
                Ability.Strength,
                Ability.Dexterity,
                Ability.Constitution,
                Ability.Charisma,
                Ability.Wisdom,
                Ability.Intelligence,
            ],
            [CharacterClass.Rogue] =
            [
                Ability.Dexterity,
                Ability.Intelligence,
                Ability.Charisma,
                Ability.Constitution,
                Ability.Strength,
                Ability.Wisdom,
            ],
            [CharacterClass.Cleric] =
            [
                Ability.Wisdom,
                Ability.Charisma,
                Ability.Strength,
                Ability.Intelligence,
                Ability.Constitution,
                Ability.Dexterity,
            ],
            [CharacterClass.Wizard] =
            [
                Ability.Intelligence,
                Ability.Wisdom,
                Ability.Dexterity,
                Ability.Constitution,
                Ability.Charisma,
                Ability.Strength,
            ],
        };

    /// <summary>The order a class wants its stats in, best dice first.</summary>
    public static IReadOnlyList<Ability> PriorityFor(CharacterClass characterClass) =>
        Priorities.TryGetValue(characterClass, out var priority)
            ? priority
            : throw new ArgumentOutOfRangeException(
                nameof(characterClass),
                characterClass,
                "No stat priority is defined for that class.");

    /// <summary>
    /// Ironman: 3d6 straight down the line. A set is kept only if it has two
    /// scores of 15 or better and no more than one score below 6 — the dice
    /// decide what you can be, but not that you are unplayable.
    /// </summary>
    public static CreatedCharacter RollIronman(
        Dice dice,
        CharacterClass characterClass,
        Ancestry ancestry = Ancestry.Human,
        int maximumAttempts = 1000)
    {
        ArgumentNullException.ThrowIfNull(dice);
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            var scores = new int[AbilityScores.AbilityCount];
            for (var index = 0; index < scores.Length; index++)
            {
                scores[index] = dice.PoolKeepHighest(3, 6, 3);
            }

            if (!IsPlayable(scores))
            {
                continue;
            }

            return new CreatedCharacter(
                AbilityScores.FromOrder(scores),
                characterClass,
                ancestry,
                CharacterCreationMethod.Ironman,
                TalentRollsFor(ancestry),
                attempt);
        }

        throw new RulesResolutionException(
            $"No playable Ironman set in {maximumAttempts} attempts, which " +
            "means the dice or the thresholds are wrong.");
    }

    /// <summary>
    /// Unearthed Arcana: the class is chosen first and its priority order takes
    /// the pools in turn, each keeping its three highest dice.
    /// </summary>
    public static CreatedCharacter RollUnearthedArcana(
        Dice dice,
        CharacterClass characterClass,
        Ancestry ancestry = Ancestry.Human)
    {
        ArgumentNullException.ThrowIfNull(dice);
        var priority = PriorityFor(characterClass);
        var scores = new int[AbilityScores.AbilityCount];
        for (var index = 0; index < priority.Count; index++)
        {
            scores[(int)priority[index]] =
                dice.PoolKeepHighest(UnearthedArcanaPools[index], 6, 3);
        }

        return new CreatedCharacter(
            AbilityScores.FromOrder(scores),
            characterClass,
            ancestry,
            CharacterCreationMethod.UnearthedArcana,
            TalentRollsFor(ancestry),
            Attempts: 1);
    }

    /// <summary>Whether a rolled set is one the Ironman method keeps.</summary>
    public static bool IsPlayable(IReadOnlyList<int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var high = 0;
        var low = 0;
        foreach (var score in scores)
        {
            if (score >= IronmanHighScore)
            {
                high++;
            }

            if (score < IronmanLowScore)
            {
                low++;
            }
        }

        return high >= IronmanHighScoresRequired && low <= IronmanLowScoresAllowed;
    }

    public static int TalentRollsFor(Ancestry ancestry) => ancestry switch
    {
        Ancestry.Human => HumanTalentRolls,
        _ => throw new ArgumentOutOfRangeException(nameof(ancestry)),
    };
}
