using Ash.Rules;

namespace Ash.Sim;

/// <summary>
/// Finds the vendored rules data on disk.
/// </summary>
/// <remarks>
/// <para>
/// The rules are data, not code: attack tables, critical tables and class
/// progressions all load from <c>vendor/ash-v1-rules/data</c>, and changing a
/// value there must change resolution without touching the engine.
/// </para>
/// <para>
/// <b>This locates them by walking up from the running assembly to the
/// repository root, which works while running from source and will not work in
/// an exported game</b>, where files live inside a Godot pack rather than on
/// disk. Packaging the rules data alongside the shape packs, and giving the
/// loader a text-based entry point the engine can feed, is outstanding work.
/// Until then an export fails here loudly rather than running with invented
/// numbers.
/// </para>
/// </remarks>
public static class RulesRepository
{
    private static readonly Lazy<RulesData> Loaded = new(Load);
    private static readonly Lazy<CharacterCreationData> LoadedCreation =
        new(LoadCreation);

    private static readonly Lazy<VitalityData> LoadedVitality = new(LoadVitality);

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

    private static RulesData Load()
    {
        var directory = FindDataDirectory();
        return RulesDataLoader.LoadFromDirectory(directory);
    }

    private static CharacterCreationData LoadCreation()
    {
        var directory = FindRepositoryDirectory("data");
        return CharacterCreationLoader.LoadFromFile(
            Path.Combine(directory, CharacterCreationLoader.FileName));
    }

    private static VitalityData LoadVitality()
    {
        var directory = FindRepositoryDirectory("data");
        return VitalityLoader.LoadFromFile(
            Path.Combine(directory, VitalityLoader.FileName));
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
