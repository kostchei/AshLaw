using Ash.Rules;
using System.Security.Cryptography;
using System.Text;

namespace Ash.Sim;

/// <summary>
/// Loads the authoritative runtime rules from a text package or, for source
/// tests and tools, from the repository's data directories.
/// </summary>
/// <remarks>
/// <para>
/// The rules are data, not code: attack tables, critical tables and class
/// progressions all load from <c>vendor/ash-v1-rules/data</c>, and changing a
/// value there must change resolution without touching the engine.
/// </para>
/// </remarks>
public static class RulesRepository
{
    private sealed record RuntimeTextPackage(
        IReadOnlyDictionary<string, string> Rules,
        string CharacterCreation,
        string Vitality);

    private static readonly object ConfigurationLock = new();
    private static RuntimeTextPackage? _configuredPackage;
    private static readonly Lazy<RuntimeTextPackage> LoadedTexts = new(LoadTexts);
    private static readonly Lazy<RulesData> Loaded = new(Load);
    private static readonly Lazy<CharacterCreationData> LoadedCreation =
        new(LoadCreation);

    private static readonly Lazy<VitalityData> LoadedVitality = new(LoadVitality);
    private static readonly Lazy<string> LoadedFingerprint = new(Fingerprint);

    public static IReadOnlyList<string> RequiredRulesFiles =>
        RulesDataLoader.RequiredFileNames;

    /// <summary>
    /// Supplies packaged resource text before any rule is read. Godot calls
    /// this from <c>res://</c>; tests can supply dictionaries or streams.
    /// </summary>
    public static void ConfigureTextPackage(
        IReadOnlyDictionary<string, string> rules,
        string characterCreation,
        string vitality)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(characterCreation);
        ArgumentNullException.ThrowIfNull(vitality);
        lock (ConfigurationLock)
        {
            if (LoadedTexts.IsValueCreated || Loaded.IsValueCreated ||
                LoadedCreation.IsValueCreated || LoadedVitality.IsValueCreated)
            {
                throw new InvalidOperationException(
                    "Runtime rules must be configured before the repository is read.");
            }
            var copy = new Dictionary<string, string>(rules, StringComparer.Ordinal);
            foreach (var name in RulesDataLoader.RequiredFileNames)
            {
                if (!copy.ContainsKey(name))
                {
                    throw new RulesDataException(
                        $"The runtime rules package is missing '{name}'.");
                }
            }
            _configuredPackage = new RuntimeTextPackage(
                copy,
                characterCreation,
                vitality);
        }
    }

    public static RulesData Rules => Loaded.Value;

    public static ClassProgressionTable ClassProgression =>
        Loaded.Value.ClassProgression;

    /// <summary>This game's own creation rules, not the vendored package's.</summary>
    public static CharacterCreationData CharacterCreation => LoadedCreation.Value;

    public static AbilityBonusTable AbilityBonuses =>
        LoadedCreation.Value.AbilityBonuses;

    /// <summary>
    /// Concussion hits, wounds and the death clock. Also this game's own rules:
    /// the vendored package produces concussion hits as a damage quantity and
    /// leaves where they land to the engine.
    /// </summary>
    public static VitalityData Vitality => LoadedVitality.Value;

    /// <summary>Canonical SHA-256 identity of every packaged runtime rule text.</summary>
    public static string RuntimeRulesFingerprint => LoadedFingerprint.Value;

    public static string ComputeRuntimeRulesFingerprint(
        IReadOnlyDictionary<string, string> rules,
        string characterCreation,
        string vitality)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(characterCreation);
        ArgumentNullException.ThrowIfNull(vitality);
        var canonical = new StringBuilder();
        foreach (var value in rules.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            canonical.Append(value.Key).Append('\0')
                .Append(value.Value.Length).Append('\0')
                .Append(value.Value).Append('\0');
        }
        canonical.Append(CharacterCreationLoader.FileName).Append('\0')
            .Append(characterCreation.Length).Append('\0')
            .Append(characterCreation).Append('\0')
            .Append(VitalityLoader.FileName).Append('\0')
            .Append(vitality.Length).Append('\0')
            .Append(vitality);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static RulesData Load()
    {
        return RulesDataLoader.LoadFromTextFiles(LoadedTexts.Value.Rules);
    }

    private static CharacterCreationData LoadCreation()
    {
        return CharacterCreationLoader.Parse(LoadedTexts.Value.CharacterCreation);
    }

    private static VitalityData LoadVitality()
    {
        return VitalityLoader.Parse(LoadedTexts.Value.Vitality);
    }

    private static RuntimeTextPackage LoadTexts()
    {
        lock (ConfigurationLock)
        {
            if (_configuredPackage is { } configured)
            {
                return configured;
            }
        }

        var rulesDirectory = FindDataDirectory();
        var rules = RulesDataLoader.RequiredFileNames.ToDictionary(
            name => name,
            name => File.ReadAllText(Path.Combine(rulesDirectory, name)),
            StringComparer.Ordinal);
        var gameData = FindRepositoryDirectory("data");
        return new RuntimeTextPackage(
            rules,
            File.ReadAllText(Path.Combine(
                gameData, CharacterCreationLoader.FileName)),
            File.ReadAllText(Path.Combine(gameData, VitalityLoader.FileName)));
    }

    private static string Fingerprint()
    {
        var package = LoadedTexts.Value;
        return ComputeRuntimeRulesFingerprint(
            package.Rules,
            package.CharacterCreation,
            package.Vitality);
    }

    private static string FindDataDirectory() =>
        FindRepositoryDirectory(Path.Combine("vendor", "ash-v1-rules", "data"));

    private static string FindRepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new RulesResolutionException(
            $"'{relativePath}' was not found by walking up from " +
            $"'{AppContext.BaseDirectory}'. Running from source finds it; an " +
            "exported build will not until the rules data is packaged with " +
            "the game.");
    }
}
