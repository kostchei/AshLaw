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
    int Attempts,
    ClassTalentRoll? FirstTalent = null,
    ClassTalentRoll? SecondTalent = null)
{
    /// <summary>The rolled talents, in roll order. Humans currently have two.</summary>
    public IReadOnlyList<ClassTalentRoll> Talents =>
        new[] { FirstTalent, SecondTalent }
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

    /// <summary>Every rolled talent has had its player choice committed.</summary>
    public bool TalentsResolved =>
        Talents.Count == TalentRolls && Talents.All(value => value.Choice is not null);
}

public enum TalentChoiceKind
{
    AbilityIncrease = 0,
    WeaponSpecialization = 1,
    ArmoredDefense = 2,
    BackstabLethality = 3,
    RogueSkillAdvantage = 4,
    LightArmorDefense = 5,
    InitiativeAndReflexes = 6,
    HitPointsPerLevel = 7,
    CriticalSeverity = 8,
}

/// <summary>A persistent weapon family selected by a rolled talent.</summary>
public enum WeaponFamily
{
    Sword = 0,
    Dagger = 1,
}

/// <summary>The Rogue skills available in the first playable slice.</summary>
public enum RogueSkill
{
    Climb = 0,
    Locks = 1,
    Traps = 2,
    Stealth = 3,
    Pickpocket = 4,
}

/// <summary>
/// The player's legal resolution of one randomly rolled talent row.
/// </summary>
public sealed record ClassTalentChoice(
    string Id,
    TalentChoiceKind Kind,
    string Description,
    Ability? FirstAbility = null,
    int FirstIncrease = 0,
    Ability? SecondAbility = null,
    int SecondIncrease = 0,
    WeaponFamily? WeaponFamily = null,
    RogueSkill? RogueSkill = null);

/// <summary>
/// A random 2d6 class-table row and, once confirmed, the player's choice
/// within that row. The row never changes when its choice is made.
/// </summary>
public sealed record ClassTalentRoll(
    int Roll,
    string Id,
    string Name,
    string Description,
    ClassTalentChoice? Choice = null);

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
    private static readonly Ability[] FighterTalentAbilities =
    [
        Ability.Strength,
        Ability.Dexterity,
        Ability.Constitution,
    ];

    private static readonly Ability[] RogueTalentAbilities =
    [
        Ability.Dexterity,
        Ability.Intelligence,
        Ability.Charisma,
    ];

    /// <summary>Rolls one exact row from a class's 2d6 talent table.</summary>
    public static ClassTalentRoll RollTalent(
        CharacterCreationData data,
        Dice dice,
        CharacterClass characterClass)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        var roll = dice.D6() + dice.D6();
        var result = data.TalentTableFor(characterClass).ForRoll(roll);
        return new ClassTalentRoll(
            roll,
            result.Id,
            result.Name,
            result.Description);
    }

    /// <summary>
    /// Rolls the ancestry's complete talent allowance. The rows are random;
    /// their legal internal choices are resolved separately by the player.
    /// </summary>
    public static CreatedCharacter RollTalents(
        CharacterCreationData data,
        Dice dice,
        CreatedCharacter character)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(character);
        var count = data.AncestryOf(character.Ancestry).TalentRolls;
        if (count is < 0 or > 2)
        {
            throw new RulesResolutionException(
                $"This character record supports up to two starting talent rolls, not {count}.");
        }

        var talents = new ClassTalentRoll?[2];
        for (var index = 0; index < count; index++)
        {
            talents[index] = RollTalent(data, dice, character.Class);
        }

        return character with
        {
            TalentRolls = count,
            FirstTalent = talents[0],
            SecondTalent = talents[1],
        };
    }

    /// <summary>
    /// Every full-value resolution the player may choose for this rolled row.
    /// Choices that would carry an ability over 20 are absent.
    /// </summary>
    public static IReadOnlyList<ClassTalentChoice> LegalTalentChoices(
        CharacterClass characterClass,
        AbilityScores scores,
        ClassTalentRoll talent)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(talent);
        if (talent.Choice is not null)
        {
            throw new InvalidOperationException("A resolved talent has no pending choices.");
        }

        return talent.Id switch
        {
            "martial_attribute_boost" => AbilityChoices(
                scores,
                FighterTalentAbilities),
            "furtive_attribute_boost" => AbilityChoices(
                scores,
                RogueTalentAbilities),
            "weapon_specialization" => FighterCombatChoices(),
            "combat_vitality" => FighterVitalityChoices(),
            "agile_lethality" => RogueLethalityChoices(),
            "evasion_reflexes" => RogueEvasionChoices(),
            "mastery_wildcard" when characterClass == CharacterClass.Fighter =>
                WildcardChoices(characterClass, scores),
            "shadow_wildcard" when characterClass == CharacterClass.Rogue =>
                WildcardChoices(characterClass, scores),
            _ => throw new RulesResolutionException(
                $"Talent '{talent.Id}' has no playable choice resolver for {characterClass}."),
        };
    }

    /// <summary>
    /// Commits one choice to a starting talent and applies any ability gain.
    /// Earlier talents must be resolved first because they change later caps.
    /// </summary>
    public static CreatedCharacter ResolveTalent(
        CreatedCharacter character,
        int talentIndex,
        ClassTalentChoice choice)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(choice);
        if (talentIndex < 0 || talentIndex >= character.TalentRolls)
        {
            throw new ArgumentOutOfRangeException(nameof(talentIndex));
        }

        var talents = new[] { character.FirstTalent, character.SecondTalent };
        if (talents[talentIndex] is not { } talent)
        {
            throw new InvalidOperationException($"Talent {talentIndex + 1} was not rolled.");
        }
        if (talent.Choice is not null)
        {
            throw new InvalidOperationException($"Talent {talentIndex + 1} is already resolved.");
        }
        if (talents.Take(talentIndex).Any(value => value?.Choice is null))
        {
            throw new InvalidOperationException("Starting talents must be resolved in roll order.");
        }

        var legal = LegalTalentChoices(character.Class, character.Scores, talent);
        if (!legal.Contains(choice))
        {
            throw new RulesResolutionException(
                $"'{choice.Description}' is not legal for rolled talent '{talent.Name}'.");
        }

        talents[talentIndex] = talent with { Choice = choice };
        return character with
        {
            Scores = ApplyAbilityChoice(character.Scores, choice),
            FirstTalent = talents[0],
            SecondTalent = talents[1],
        };
    }

    /// <summary>Resolves a level-up talent without changing its random row.</summary>
    public static ClassTalentRoll ResolveTalent(
        CharacterClass characterClass,
        AbilityScores scores,
        ClassTalentRoll talent,
        ClassTalentChoice choice)
    {
        ArgumentNullException.ThrowIfNull(choice);
        var legal = LegalTalentChoices(characterClass, scores, talent);
        if (!legal.Contains(choice))
        {
            throw new RulesResolutionException(
                $"'{choice.Description}' is not legal for rolled talent '{talent.Name}'.");
        }

        return talent with { Choice = choice };
    }

    /// <summary>Applies the score portion of a resolved talent, if it has one.</summary>
    public static AbilityScores ApplyAbilityChoice(
        AbilityScores scores,
        ClassTalentChoice choice)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(choice);
        if (choice.Kind != TalentChoiceKind.AbilityIncrease)
        {
            return scores;
        }
        if (choice.FirstAbility is not { } first || choice.FirstIncrease < 1)
        {
            throw new RulesResolutionException("An ability talent needs its first increase.");
        }

        var changed = Increase(scores, first, choice.FirstIncrease);
        if (choice.SecondAbility is { } second)
        {
            if (second == first || choice.SecondIncrease < 1)
            {
                throw new RulesResolutionException(
                    "A split ability talent needs two different abilities.");
            }
            changed = Increase(changed, second, choice.SecondIncrease);
        }

        return changed;
    }

    /// <summary>
    /// Gives non-interactive/demo callers a deterministic valid character.
    /// The playable creation screen always asks the player instead.
    /// </summary>
    public static CreatedCharacter ResolveTalentsWithFirstLegalChoices(
        CreatedCharacter character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var resolved = character;
        for (var index = 0; index < resolved.TalentRolls; index++)
        {
            var talent = index == 0 ? resolved.FirstTalent! : resolved.SecondTalent!;
            var choices = LegalTalentChoices(resolved.Class, resolved.Scores, talent);
            if (choices.Count == 0)
            {
                throw new RulesResolutionException(
                    $"Rolled talent '{talent.Name}' has no legal full-value choice.");
            }
            resolved = ResolveTalent(resolved, index, choices[0]);
        }

        return resolved;
    }

    private static IReadOnlyList<ClassTalentChoice> WildcardChoices(
        CharacterClass characterClass,
        AbilityScores scores)
    {
        var choices = PlusTwoChoices(scores, Enum.GetValues<Ability>())
            .ToList();
        var classChoices = characterClass switch
        {
            CharacterClass.Fighter => AbilityChoices(scores, FighterTalentAbilities)
                .Concat(FighterCombatChoices())
                .Concat(FighterVitalityChoices()),
            CharacterClass.Rogue => AbilityChoices(scores, RogueTalentAbilities)
                .Concat(RogueLethalityChoices())
                .Concat(RogueEvasionChoices()),
            _ => [],
        };
        choices.AddRange(classChoices);
        return choices.Distinct().ToArray();
    }

    private static IReadOnlyList<ClassTalentChoice> AbilityChoices(
        AbilityScores scores,
        IReadOnlyList<Ability> allowed)
    {
        var choices = PlusTwoChoices(scores, allowed).ToList();
        var plusOne = allowed.Where(ability => scores[ability] <= 19).ToArray();
        for (var first = 0; first < plusOne.Length; first++)
        {
            for (var second = first + 1; second < plusOne.Length; second++)
            {
                var a = plusOne[first];
                var b = plusOne[second];
                choices.Add(new ClassTalentChoice(
                    $"ability.{a.ToString().ToLowerInvariant()}+{b.ToString().ToLowerInvariant()}.1",
                    TalentChoiceKind.AbilityIncrease,
                    $"+1 {ShortAbility(a)}, +1 {ShortAbility(b)}",
                    a,
                    1,
                    b,
                    1));
            }
        }

        return choices;
    }

    private static IEnumerable<ClassTalentChoice> PlusTwoChoices(
        AbilityScores scores,
        IReadOnlyList<Ability> allowed) =>
        allowed
            .Where(ability => scores[ability] <= 18)
            .Select(ability => new ClassTalentChoice(
                $"ability.{ability.ToString().ToLowerInvariant()}.2",
                TalentChoiceKind.AbilityIncrease,
                $"+2 {ShortAbility(ability)}",
                ability,
                2));

    private static IReadOnlyList<ClassTalentChoice> FighterCombatChoices() =>
    [
        .. Enum.GetValues<WeaponFamily>().Select(family => new ClassTalentChoice(
            $"weapon.{family.ToString().ToLowerInvariant()}",
            TalentChoiceKind.WeaponSpecialization,
            $"+1 attack/damage with {family}",
            WeaponFamily: family)),
        new(
            "defense.armored",
            TalentChoiceKind.ArmoredDefense,
            "+1 AC while wearing armor"),
    ];

    private static IReadOnlyList<ClassTalentChoice> FighterVitalityChoices() =>
    [
        new(
            "vitality.hp_per_level",
            TalentChoiceKind.HitPointsPerLevel,
            "+1 HP per level (retroactive)"),
        new(
            "critical.severity",
            TalentChoiceKind.CriticalSeverity,
            "+1 critical-hit severity"),
    ];

    private static IReadOnlyList<ClassTalentChoice> RogueLethalityChoices() =>
    [
        new(
            "backstab.lethality",
            TalentChoiceKind.BackstabLethality,
            "+1 attack/damage while backstabbing"),
        .. Enum.GetValues<RogueSkill>().Select(skill => new ClassTalentChoice(
            $"skill.{skill.ToString().ToLowerInvariant()}",
            TalentChoiceKind.RogueSkillAdvantage,
            $"Advantage with {skill}",
            RogueSkill: skill)),
    ];

    private static IReadOnlyList<ClassTalentChoice> RogueEvasionChoices() =>
    [
        new(
            "defense.light_armor",
            TalentChoiceKind.LightArmorDefense,
            "+1 AC unarmored or in light armor"),
        new(
            "reflex.initiative",
            TalentChoiceKind.InitiativeAndReflexes,
            "+1 initiative and Reflex saves"),
    ];

    private static AbilityScores Increase(
        AbilityScores scores,
        Ability ability,
        int amount)
    {
        var increased = checked(scores[ability] + amount);
        if (increased > AbilityScores.MaximumScore)
        {
            throw new RulesResolutionException(
                $"{ability} cannot increase from {scores[ability]} to {increased}; " +
                $"the mortal cap is {AbilityScores.MaximumScore}.");
        }
        return scores.With(ability, increased);
    }

    private static string ShortAbility(Ability ability) => ability switch
    {
        Ability.Strength => "STR",
        Ability.Dexterity => "DEX",
        Ability.Constitution => "CON",
        Ability.Intelligence => "INT",
        Ability.Wisdom => "WIS",
        Ability.Charisma => "CHA",
        _ => throw new ArgumentOutOfRangeException(nameof(ability)),
    };

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
