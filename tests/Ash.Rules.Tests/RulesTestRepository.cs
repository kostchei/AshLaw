namespace Ash.Rules.Tests;

internal static class RulesTestRepository
{
    public static string Root { get; } = FindRoot();

    public static string DataDirectory =>
        Path.Combine(Root, "vendor", "ash-v1-rules", "data");

    public static string FixtureDirectory =>
        Path.Combine(Root, "tests", "fixtures", "rules");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ash.sln")) &&
                Directory.Exists(Path.Combine(
                    directory.FullName,
                    "vendor",
                    "ash-v1-rules",
                    "data")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the AshLaw repository from '{AppContext.BaseDirectory}'.");
    }
}

