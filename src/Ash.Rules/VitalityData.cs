using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ash.Rules;

/// <summary>How well a character is being looked after while they mend.</summary>
public enum CareLevel
{
    /// <summary>A full day of rest with medical treatment.</summary>
    Treated = 0,

    /// <summary>Rest, or treatment, but not both.</summary>
    Rested = 1,

    /// <summary>Carrying on regardless.</summary>
    Adventuring = 2,
}

/// <summary>One class's hit die, and whether its constitution bonus is capped.</summary>
/// <remarks>
/// A fighter's body is the thing they train, so their constitution bonus goes
/// on every die in full. Everyone else takes it up to the cap and no further:
/// a wizard with an 18 constitution is hardy for a wizard, not a fighter.
/// </remarks>
public sealed record HitDieDefinition(
    CharacterClass Class,
    int DieSides,
    bool CapsConstitutionBonus);

public sealed record HitDiceRules(
    int MaximumDice,
    int MinimumPerDie,
    int NonFighterConstitutionBonusCap,
    IReadOnlyDictionary<CharacterClass, HitDieDefinition> Classes)
{
    public HitDieDefinition For(CharacterClass characterClass) =>
        Classes.TryGetValue(characterClass, out var definition)
            ? definition
            : throw new RulesResolutionException(
                $"No hit die is configured for {characterClass}.");

    /// <summary>How many hit dice a character of this level has.</summary>
    public int DiceAt(int level)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "A character is at least level one.");
        }

        return Math.Min(level, MaximumDice);
    }

    /// <summary>
    /// The constitution bonus that actually rides on each of this class's dice.
    /// </summary>
    public int ConstitutionBonusFor(
        CharacterClass characterClass,
        int constitutionBonus) =>
        For(characterClass).CapsConstitutionBonus
            ? Math.Min(constitutionBonus, NonFighterConstitutionBonusCap)
            : constitutionBonus;
}

/// <summary>One bracket of the daily recovery cap: a first level and its dice.</summary>
public sealed record RecoveryBracket(int FirstLevel, int Dice);

public sealed record ConcussionRecoveryRules(
    int DicePerDay,
    int FullRestBonusDice,
    int MedicalAttentionBonusDice,
    IReadOnlyList<RecoveryBracket> DailyDiceBrackets)
{
    /// <summary>
    /// The most dice a character of this level can spend in one day, however
    /// well they are looked after.
    /// </summary>
    public int DailyCapAt(int level)
    {
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "A character is at least level one.");
        }

        var cap = 0;
        var found = false;
        foreach (var bracket in DailyDiceBrackets)
        {
            if (bracket.FirstLevel > level)
            {
                continue;
            }

            cap = bracket.Dice;
            found = true;
        }

        return found
            ? cap
            : throw new RulesResolutionException(
                $"No recovery bracket covers level {level}.");
    }

    /// <summary>
    /// The dice a day of this kind of care offers, before the level cap and the
    /// dice the character actually has are applied.
    /// </summary>
    public int OfferedDice(bool fullRest, bool medicalAttention) =>
        DicePerDay +
        (fullRest ? FullRestBonusDice : 0) +
        (medicalAttention ? MedicalAttentionBonusDice : 0);
}

public sealed record WoundRules(int Minimum, int ImpairmentsOnDeathClockSurvival);

public sealed record WoundRecoveryRules(int PerDay, int PerHealing);

public sealed record DeathClockRules(
    int SaveDc,
    int SuccessesToStabilise,
    int FailuresToDie);

public sealed record InfectionRules(
    int DamageDice,
    int DamageDieSides,
    IReadOnlyDictionary<CareLevel, int> CareLevelDcs)
{
    public int DcFor(CareLevel care) =>
        CareLevelDcs.TryGetValue(care, out var dc)
            ? dc
            : throw new RulesResolutionException(
                $"No infection DC is configured for {care} care.");
}

/// <summary>
/// Everything the injury model needs, loaded from <c>data/vitality.json</c>.
/// </summary>
public sealed class VitalityData
{
    public const int CurrentSchemaVersion = 1;

    internal VitalityData(
        HitDiceRules hitDice,
        ConcussionRecoveryRules concussionRecovery,
        WoundRules wounds,
        WoundRecoveryRules woundRecovery,
        DeathClockRules deathClock,
        InfectionRules infection)
    {
        HitDice = hitDice;
        ConcussionRecovery = concussionRecovery;
        Wounds = wounds;
        WoundRecovery = woundRecovery;
        DeathClock = deathClock;
        Infection = infection;
    }

    public HitDiceRules HitDice { get; }

    public ConcussionRecoveryRules ConcussionRecovery { get; }

    public WoundRules Wounds { get; }

    public WoundRecoveryRules WoundRecovery { get; }

    public DeathClockRules DeathClock { get; }

    public InfectionRules Infection { get; }
}

/// <summary>
/// Reads <c>vitality.json</c>. Parsing takes text rather than a path, so a
/// packaged build can hand over file contents it read its own way.
/// </summary>
public static class VitalityLoader
{
    public const string FileName = "vitality.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static VitalityData Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, JsonOptions) ??
                throw new RulesDataException("Vitality JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new RulesDataException(
                $"Vitality JSON is invalid at {exception.Path ?? "<root>"}: " +
                exception.Message,
                exception);
        }

        if (document.SchemaVersion != VitalityData.CurrentSchemaVersion)
        {
            throw new RulesDataException(
                $"Unsupported vitality schema {document.SchemaVersion}; " +
                $"expected {VitalityData.CurrentSchemaVersion}.");
        }

        return new VitalityData(
            ReadHitDice(document.HitDice),
            ReadConcussionRecovery(document.ConcussionRecovery),
            ReadWounds(document.Wounds),
            ReadWoundRecovery(document.WoundRecovery),
            ReadDeathClock(document.DeathClock),
            ReadInfection(document.Infection));
    }

    public static VitalityData LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new RulesDataException($"Vitality data does not exist: {path}");
        }

        return Parse(File.ReadAllText(path));
    }

    private static HitDiceRules ReadHitDice(HitDiceDocument? document)
    {
        var hitDice = Require(document, "hit_dice");
        if (hitDice.MaximumDice < 1)
        {
            throw new RulesDataException(
                "hit_dice.maximum_dice must be at least one.");
        }

        if (hitDice.MinimumPerDie < 1)
        {
            throw new RulesDataException(
                "hit_dice.minimum_per_die must be at least one: a level never " +
                "takes concussion hits away.");
        }

        if (hitDice.NonFighterConstitutionBonusCap < 0)
        {
            throw new RulesDataException(
                "hit_dice.non_fighter_constitution_bonus_cap cannot be negative.");
        }

        var classes = new Dictionary<CharacterClass, HitDieDefinition>();
        foreach (var entry in Require(hitDice.Classes, "hit_dice.classes"))
        {
            var characterClass = ParseEnum<CharacterClass>(entry.Id, "class");
            if (entry.DieSides < 2)
            {
                throw new RulesDataException(
                    $"'{entry.Id}' has a hit die of {entry.DieSides} sides.");
            }

            if (!classes.TryAdd(
                    characterClass,
                    new HitDieDefinition(
                        characterClass,
                        entry.DieSides,
                        entry.CapsConstitutionBonus)))
            {
                throw new RulesDataException(
                    $"'{entry.Id}' has more than one hit die.");
            }
        }

        foreach (var characterClass in Enum.GetValues<CharacterClass>())
        {
            if (!classes.ContainsKey(characterClass))
            {
                throw new RulesDataException(
                    $"hit_dice.classes has no entry for {characterClass}.");
            }
        }

        return new HitDiceRules(
            hitDice.MaximumDice,
            hitDice.MinimumPerDie,
            hitDice.NonFighterConstitutionBonusCap,
            classes);
    }

    private static ConcussionRecoveryRules ReadConcussionRecovery(
        ConcussionRecoveryDocument? document)
    {
        var recovery = Require(document, "concussion_recovery");
        if (recovery.DicePerDay < 0 ||
            recovery.FullRestBonusDice < 0 ||
            recovery.MedicalAttentionBonusDice < 0)
        {
            throw new RulesDataException(
                "Concussion recovery cannot offer a negative number of dice.");
        }

        var brackets = Require(
                recovery.DailyDiceBrackets,
                "concussion_recovery.daily_dice_brackets")
            .Select(bracket => new RecoveryBracket(bracket.FirstLevel, bracket.Dice))
            .OrderBy(bracket => bracket.FirstLevel)
            .ToArray();
        if (brackets.Length == 0 || brackets[0].FirstLevel != 1)
        {
            throw new RulesDataException(
                "The daily recovery brackets must start at level one.");
        }

        for (var index = 0; index < brackets.Length; index++)
        {
            if (brackets[index].Dice < 0)
            {
                throw new RulesDataException(
                    $"The recovery bracket at level {brackets[index].FirstLevel} " +
                    "spends a negative number of dice.");
            }

            if (index > 0 &&
                brackets[index].FirstLevel == brackets[index - 1].FirstLevel)
            {
                throw new RulesDataException(
                    $"Level {brackets[index].FirstLevel} starts more than one " +
                    "recovery bracket.");
            }
        }

        return new ConcussionRecoveryRules(
            recovery.DicePerDay,
            recovery.FullRestBonusDice,
            recovery.MedicalAttentionBonusDice,
            brackets);
    }

    private static WoundRules ReadWounds(WoundDocument? document)
    {
        var wounds = Require(document, "wounds");
        if (wounds.Minimum < 1)
        {
            throw new RulesDataException(
                "wounds.minimum must be at least one: a character with no " +
                "wound layer would go from unhurt to the death clock.");
        }

        if (wounds.ImpairmentsOnDeathClockSurvival < 0 ||
            wounds.ImpairmentsOnDeathClockSurvival > AbilityScores.AbilityCount)
        {
            throw new RulesDataException(
                "wounds.impairments_on_death_clock_survival must name between " +
                $"zero and {AbilityScores.AbilityCount} abilities.");
        }

        return new WoundRules(
            wounds.Minimum,
            wounds.ImpairmentsOnDeathClockSurvival);
    }

    private static WoundRecoveryRules ReadWoundRecovery(
        WoundRecoveryDocument? document)
    {
        var recovery = Require(document, "wound_recovery");
        if (recovery.PerDay < 0 || recovery.PerHealing < 0)
        {
            throw new RulesDataException(
                "Wound recovery cannot restore a negative number of wounds.");
        }

        return new WoundRecoveryRules(recovery.PerDay, recovery.PerHealing);
    }

    private static DeathClockRules ReadDeathClock(DeathClockDocument? document)
    {
        var clock = Require(document, "death_clock");
        if (clock.SuccessesToStabilise < 1 || clock.FailuresToDie < 1)
        {
            throw new RulesDataException(
                "A death clock that cannot resolve either way never ends.");
        }

        return new DeathClockRules(
            clock.SaveDc,
            clock.SuccessesToStabilise,
            clock.FailuresToDie);
    }

    private static InfectionRules ReadInfection(InfectionDocument? document)
    {
        var infection = Require(document, "infection");
        if (infection.DamageDice < 0 || infection.DamageDieSides < 2)
        {
            throw new RulesDataException("The infection damage dice are invalid.");
        }

        var dcs = new Dictionary<CareLevel, int>();
        foreach (var level in Require(infection.CareLevels, "infection.care_levels"))
        {
            if (!dcs.TryAdd(ParseEnum<CareLevel>(level.Id, "care level"), level.Dc))
            {
                throw new RulesDataException(
                    $"Care level '{level.Id}' has more than one DC.");
            }
        }

        foreach (var care in Enum.GetValues<CareLevel>())
        {
            if (!dcs.ContainsKey(care))
            {
                throw new RulesDataException(
                    $"infection.care_levels has no entry for {care}.");
            }
        }

        return new InfectionRules(
            infection.DamageDice,
            infection.DamageDieSides,
            dcs);
    }

    private static TEnum ParseEnum<TEnum>(string? name, string what)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(name, ignoreCase: true, out var value)
            ? value
            : throw new RulesDataException($"Unknown {what} '{name}'.");

    private static T Require<T>(T? value, string what)
        where T : class =>
        value ?? throw new RulesDataException($"'{what}' is missing.");

    private sealed record Document
    {
        public int SchemaVersion { get; init; }

        public HitDiceDocument? HitDice { get; init; }

        public ConcussionRecoveryDocument? ConcussionRecovery { get; init; }

        public WoundDocument? Wounds { get; init; }

        public WoundRecoveryDocument? WoundRecovery { get; init; }

        public DeathClockDocument? DeathClock { get; init; }

        public InfectionDocument? Infection { get; init; }
    }

    private sealed record HitDiceDocument
    {
        public int MaximumDice { get; init; }

        public int MinimumPerDie { get; init; }

        public int NonFighterConstitutionBonusCap { get; init; }

        public IReadOnlyList<HitDieRow>? Classes { get; init; }
    }

    private sealed record HitDieRow
    {
        public string? Id { get; init; }

        public int DieSides { get; init; }

        public bool CapsConstitutionBonus { get; init; }
    }

    private sealed record ConcussionRecoveryDocument
    {
        public int DicePerDay { get; init; }

        public int FullRestBonusDice { get; init; }

        public int MedicalAttentionBonusDice { get; init; }

        public IReadOnlyList<BracketRow>? DailyDiceBrackets { get; init; }
    }

    private sealed record BracketRow
    {
        public int FirstLevel { get; init; }

        public int Dice { get; init; }
    }

    private sealed record WoundDocument
    {
        public int Minimum { get; init; }

        public int ImpairmentsOnDeathClockSurvival { get; init; }
    }

    private sealed record WoundRecoveryDocument
    {
        public int PerDay { get; init; }

        public int PerHealing { get; init; }
    }

    private sealed record DeathClockDocument
    {
        public int SaveDc { get; init; }

        public int SuccessesToStabilise { get; init; }

        public int FailuresToDie { get; init; }
    }

    private sealed record InfectionDocument
    {
        public int DamageDice { get; init; }

        public int DamageDieSides { get; init; }

        public IReadOnlyList<CareLevelRow>? CareLevels { get; init; }
    }

    private sealed record CareLevelRow
    {
        public string? Id { get; init; }

        public int Dc { get; init; }
    }
}
