using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ash.Rules;

/// <summary>
/// The ability-bonus curve, as data.
/// </summary>
/// <remarks>
/// Every score in range has an explicit row, so the curve can be retuned
/// without touching code — including making it steeper, which is the difference
/// between plate being wearable and plate being theoretical.
/// </remarks>
public sealed class AbilityBonusTable
{
    private readonly IReadOnlyDictionary<int, int> _bonuses;

    internal AbilityBonusTable(
        int minimumScore,
        int maximumScore,
        IReadOnlyDictionary<int, int> bonuses)
    {
        MinimumScore = minimumScore;
        MaximumScore = maximumScore;
        _bonuses = bonuses;
    }

    public int MinimumScore { get; }

    public int MaximumScore { get; }

    public int BonusFor(int score) =>
        _bonuses.TryGetValue(score, out var bonus)
            ? bonus
            : throw new RulesResolutionException(
                $"No ability bonus is defined for a score of {score}; the " +
                $"table covers {MinimumScore} to {MaximumScore}.");

    public int BonusOf(AbilityScores scores, Ability ability) =>
        BonusFor(scores[ability]);
}

public sealed record AncestryDefinition(
    Ancestry Ancestry,
    string Name,
    int TalentRolls);

public sealed record IronmanRules(
    int DicePerScore,
    int DieSides,
    int KeepHighest,
    IReadOnlyList<Ability> ScoreOrder,
    int HighScore,
    int HighScoresRequired,
    int LowScore,
    int LowScoresAllowed,
    int MaximumAttempts)
{
    /// <summary>Whether a rolled set is one this method keeps.</summary>
    public bool IsPlayable(IReadOnlyList<int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var high = scores.Count(score => score >= HighScore);
        var low = scores.Count(score => score < LowScore);
        return high >= HighScoresRequired && low <= LowScoresAllowed;
    }
}

public sealed record UnearthedArcanaRules(
    int DieSides,
    int KeepHighest,
    IReadOnlyList<int> Pools,
    IReadOnlyDictionary<CharacterClass, IReadOnlyList<Ability>> ClassPriorities)
{
    public IReadOnlyList<Ability> PriorityFor(CharacterClass characterClass) =>
        ClassPriorities.TryGetValue(characterClass, out var priority)
            ? priority
            : throw new RulesResolutionException(
                $"No stat priority is configured for {characterClass}.");
}

/// <summary>
/// Everything character creation needs, loaded from
/// <c>data/character-creation.json</c>.
/// </summary>
public sealed class CharacterCreationData
{
    public const int CurrentSchemaVersion = 1;

    internal CharacterCreationData(
        AbilityBonusTable abilityBonuses,
        IReadOnlyDictionary<Ancestry, AncestryDefinition> ancestries,
        IronmanRules ironman,
        UnearthedArcanaRules unearthedArcana)
    {
        AbilityBonuses = abilityBonuses;
        Ancestries = ancestries;
        Ironman = ironman;
        UnearthedArcana = unearthedArcana;
    }

    public AbilityBonusTable AbilityBonuses { get; }

    public IReadOnlyDictionary<Ancestry, AncestryDefinition> Ancestries { get; }

    public IronmanRules Ironman { get; }

    public UnearthedArcanaRules UnearthedArcana { get; }

    public AncestryDefinition AncestryOf(Ancestry ancestry) =>
        Ancestries.TryGetValue(ancestry, out var definition)
            ? definition
            : throw new RulesResolutionException(
                $"No ancestry '{ancestry}' is configured.");
}

/// <summary>
/// Reads <c>character-creation.json</c>. Parsing takes text rather than a path,
/// so a packaged build can hand over file contents it read its own way.
/// </summary>
public static class CharacterCreationLoader
{
    public const string FileName = "character-creation.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static CharacterCreationData Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, JsonOptions) ??
                throw new RulesDataException("Character creation JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new RulesDataException(
                $"Character creation JSON is invalid at " +
                $"{exception.Path ?? "<root>"}: {exception.Message}",
                exception);
        }

        if (document.SchemaVersion != CharacterCreationData.CurrentSchemaVersion)
        {
            throw new RulesDataException(
                $"Unsupported character creation schema " +
                $"{document.SchemaVersion}; expected " +
                $"{CharacterCreationData.CurrentSchemaVersion}.");
        }

        return new CharacterCreationData(
            ReadBonuses(document.AbilityBonuses),
            ReadAncestries(document.Ancestries),
            ReadIronman(document.Ironman),
            ReadUnearthedArcana(document.UnearthedArcana));
    }

    public static CharacterCreationData LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new RulesDataException(
                $"Character creation data does not exist: {path}");
        }

        return Parse(File.ReadAllText(path));
    }

    private static AbilityBonusTable ReadBonuses(BonusDocument? document)
    {
        var bonuses = Require(document, "ability_bonuses");
        var rows = Require(bonuses.Rows, "ability_bonuses.rows");
        var table = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            if (!table.TryAdd(row.Score, row.Bonus))
            {
                throw new RulesDataException(
                    $"Ability score {row.Score} has more than one bonus.");
            }
        }

        for (var score = bonuses.MinimumScore; score <= bonuses.MaximumScore; score++)
        {
            if (!table.ContainsKey(score))
            {
                throw new RulesDataException(
                    $"The ability bonus table has no row for a score of {score}.");
            }
        }

        return new AbilityBonusTable(
            bonuses.MinimumScore,
            bonuses.MaximumScore,
            table);
    }

    private static IReadOnlyDictionary<Ancestry, AncestryDefinition>
        ReadAncestries(IReadOnlyList<AncestryDocument>? documents)
    {
        var ancestries = new Dictionary<Ancestry, AncestryDefinition>();
        foreach (var document in Require(documents, "ancestries"))
        {
            var ancestry = ParseEnum<Ancestry>(document.Id, "ancestry");
            if (document.TalentRolls < 0)
            {
                throw new RulesDataException(
                    $"Ancestry '{document.Id}' has a negative talent count.");
            }

            ancestries.Add(
                ancestry,
                new AncestryDefinition(
                    ancestry,
                    document.Name ?? document.Id ?? string.Empty,
                    document.TalentRolls));
        }

        if (ancestries.Count == 0)
        {
            throw new RulesDataException("No ancestries are configured.");
        }

        return ancestries;
    }

    private static IronmanRules ReadIronman(IronmanDocument? document)
    {
        var ironman = Require(document, "ironman");
        var order = ReadAbilities(ironman.ScoreOrder, "ironman.score_order");
        if (ironman.DicePerScore < 1 ||
            ironman.DieSides < 2 ||
            ironman.KeepHighest < 1 ||
            ironman.KeepHighest > ironman.DicePerScore ||
            ironman.MaximumAttempts < 1)
        {
            throw new RulesDataException("The Ironman dice configuration is invalid.");
        }

        var playable = Require(ironman.Playable, "ironman.playable");
        return new IronmanRules(
            ironman.DicePerScore,
            ironman.DieSides,
            ironman.KeepHighest,
            order,
            playable.HighScore,
            playable.HighScoresRequired,
            playable.LowScore,
            playable.LowScoresAllowed,
            ironman.MaximumAttempts);
    }

    private static UnearthedArcanaRules ReadUnearthedArcana(
        UnearthedArcanaDocument? document)
    {
        var arcana = Require(document, "unearthed_arcana");
        var pools = Require(arcana.Pools, "unearthed_arcana.pools");
        if (pools.Count != AbilityScores.AbilityCount)
        {
            throw new RulesDataException(
                $"Unearthed Arcana needs {AbilityScores.AbilityCount} pools, " +
                $"not {pools.Count}.");
        }

        if (pools.Any(pool => pool < arcana.KeepHighest))
        {
            throw new RulesDataException(
                "A pool cannot be smaller than the dice it keeps.");
        }

        var priorities = new Dictionary<CharacterClass, IReadOnlyList<Ability>>();
        foreach (var (name, abilities) in
                 Require(arcana.ClassPriorities, "unearthed_arcana.class_priorities"))
        {
            priorities.Add(
                ParseEnum<CharacterClass>(name, "class"),
                ReadAbilities(abilities, $"class_priorities.{name}"));
        }

        return new UnearthedArcanaRules(
            arcana.DieSides,
            arcana.KeepHighest,
            pools,
            priorities);
    }

    private static IReadOnlyList<Ability> ReadAbilities(
        IReadOnlyList<string>? names,
        string context)
    {
        var abilities = Require(names, context)
            .Select(name => ParseEnum<Ability>(name, "ability"))
            .ToArray();
        if (abilities.Length != AbilityScores.AbilityCount ||
            abilities.Distinct().Count() != abilities.Length)
        {
            throw new RulesDataException(
                $"'{context}' must name each of the six abilities exactly once.");
        }

        return abilities;
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

        public BonusDocument? AbilityBonuses { get; init; }

        public IReadOnlyList<AncestryDocument>? Ancestries { get; init; }

        public IronmanDocument? Ironman { get; init; }

        public UnearthedArcanaDocument? UnearthedArcana { get; init; }
    }

    private sealed record BonusDocument
    {
        public int MinimumScore { get; init; }

        public int MaximumScore { get; init; }

        public IReadOnlyList<BonusRow>? Rows { get; init; }
    }

    private sealed record BonusRow
    {
        public int Score { get; init; }

        public int Bonus { get; init; }
    }

    private sealed record AncestryDocument
    {
        public string? Id { get; init; }

        public string? Name { get; init; }

        public int TalentRolls { get; init; }
    }

    private sealed record IronmanDocument
    {
        public int DicePerScore { get; init; }

        public int DieSides { get; init; }

        public int KeepHighest { get; init; }

        public IReadOnlyList<string>? ScoreOrder { get; init; }

        public PlayableDocument? Playable { get; init; }

        public int MaximumAttempts { get; init; }
    }

    private sealed record PlayableDocument
    {
        public int HighScore { get; init; }

        public int HighScoresRequired { get; init; }

        public int LowScore { get; init; }

        public int LowScoresAllowed { get; init; }
    }

    private sealed record UnearthedArcanaDocument
    {
        public int DieSides { get; init; }

        public int KeepHighest { get; init; }

        public IReadOnlyList<int>? Pools { get; init; }

        public IReadOnlyDictionary<string, IReadOnlyList<string>>?
            ClassPriorities { get; init; }
    }
}
