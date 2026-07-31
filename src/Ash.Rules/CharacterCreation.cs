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

/// <summary>
/// Six scores off the dice, before anyone has decided what they are.
/// </summary>
/// <remarks>
/// The Ironman method rolls first and chooses second, so its two halves are two
/// calls: what the dice gave, and then what you make of it.
/// </remarks>
public sealed record RolledScores(
    AbilityScores Scores,
    Ancestry Ancestry,
    int Attempts);

/// <summary>A generated character, and how it was generated.</summary>
public sealed record CreatedCharacter(
    AbilityScores Scores,
    CharacterClass Class,
    Ancestry Ancestry,
    CharacterCreationMethod Method,
    int TalentRolls,
    int Attempts);

/// <summary>
/// Character creation: the two ways to roll a character.
/// </summary>
/// <remarks>
/// Every threshold, pool size and class priority comes from
/// <see cref="CharacterCreationData"/>, which is loaded from
/// <c>data/character-creation.json</c>. These are U8_Ash's own rules rather
/// than part of the vendored Ash V1 package, so they live in this repository's
/// own data file and can be retuned without touching code.
/// </remarks>
public static class CharacterCreation
{
    /// <summary>
    /// Ironman, first half: dice straight down the line in the configured
    /// order. A set is kept only if it is playable — the dice decide what you
    /// can be, but not that you are unplayable — and the attempt count says how
    /// long that took. What the character becomes is
    /// <see cref="Choose"/>, after the scores are known.
    /// </summary>
    public static RolledScores RollIronman(
        CharacterCreationData data,
        Dice dice,
        Ancestry ancestry = Ancestry.Human)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        var rules = data.Ironman;
        for (var attempt = 1; attempt <= rules.MaximumAttempts; attempt++)
        {
            var scores = new int[AbilityScores.AbilityCount];
            foreach (var ability in rules.ScoreOrder)
            {
                scores[(int)ability] = dice.PoolKeepHighest(
                    rules.DicePerScore,
                    rules.DieSides,
                    rules.KeepHighest);
            }

            if (!rules.IsPlayable(scores))
            {
                continue;
            }

            return new RolledScores(
                AbilityScores.FromOrder(scores),
                ancestry,
                attempt);
        }

        throw new RulesResolutionException(
            $"No playable Ironman set in {rules.MaximumAttempts} attempts, " +
            "which means the dice or the thresholds are wrong.");
    }

    /// <summary>
    /// Ironman, second half: having seen the scores, decide what they are.
    /// </summary>
    public static CreatedCharacter Choose(
        CharacterCreationData data,
        RolledScores rolled,
        CharacterClass characterClass)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(rolled);
        return new CreatedCharacter(
            rolled.Scores,
            characterClass,
            rolled.Ancestry,
            CharacterCreationMethod.Ironman,
            data.AncestryOf(rolled.Ancestry).TalentRolls,
            rolled.Attempts);
    }

    /// <summary>
    /// Unearthed Arcana: the class is chosen first — it decides which stat gets
    /// the fattest pool — and its priority order takes the pools in turn, each
    /// keeping its highest dice. There is no second call: the choice came first.
    /// </summary>
    public static CreatedCharacter RollUnearthedArcana(
        CharacterCreationData data,
        Dice dice,
        CharacterClass characterClass,
        Ancestry ancestry = Ancestry.Human)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        var rules = data.UnearthedArcana;
        var priority = rules.PriorityFor(characterClass);
        var scores = new int[AbilityScores.AbilityCount];
        for (var index = 0; index < priority.Count; index++)
        {
            scores[(int)priority[index]] = dice.PoolKeepHighest(
                rules.Pools[index],
                rules.DieSides,
                rules.KeepHighest);
        }

        return new CreatedCharacter(
            AbilityScores.FromOrder(scores),
            characterClass,
            ancestry,
            CharacterCreationMethod.UnearthedArcana,
            data.AncestryOf(ancestry).TalentRolls,
            Attempts: 1);
    }
}
