namespace Ash.Content.Tests;

public sealed class StarterShapePackTests
{
    [Fact]
    public void FlareStarterPackLoadsWithExpectedCoverage()
    {
        var packDirectory = Path.Combine(
            FindRepositoryRoot(),
            "game",
            "assets",
            "shape-packs",
            "flare-starter");

        var pack = ShapePackLoader.LoadFromDirectory(packDirectory);

        Assert.Equal("flare-starter", pack.PackId);
        Assert.Equal("CC-BY-SA-3.0-or-later", pack.Attribution.License);
        Assert.Equal(4, pack.Shapes.Count);
        Assert.Equal(
            32,
            pack.GetShape("avatar.knight")
                .Animations
                .SelectMany(animation => animation.Frames)
                .Count());
        Assert.Equal(
            144,
            pack.GetShape("monster.goblin")
                .Animations
                .SelectMany(animation => animation.Frames)
                .Count());
        Assert.Equal(
            ["stance", "run", "die"],
            pack.GetShape("monster.goblin")
                .Animations
                .Select(animation => animation.Name));
    }

    [Fact]
    public void EveryStarterFrameHasAtLeastOneOpaquePixel()
    {
        var packDirectory = Path.Combine(
            FindRepositoryRoot(),
            "game",
            "assets",
            "shape-packs",
            "flare-starter");
        var pack = ShapePackLoader.LoadFromDirectory(packDirectory);

        foreach (var shape in pack.Shapes)
        {
            var mask = File.ReadAllBytes(
                Path.Combine(packDirectory, shape.Mask));
            foreach (var frame in shape.Animations.SelectMany(
                         animation => animation.Frames))
            {
                Assert.Contains(
                    mask.AsSpan(frame.MaskOffset, frame.MaskByteLength).ToArray(),
                    value => value != 0);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Ash.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find Ash.sln above the test output directory.");
    }
}
