namespace Ash.Content.Tests;

public sealed class ShapeHitTestTests
{
    // container.chest renders at 2:1, so screen pixels map back to frame pixels
    // one-to-one and the mask can be checked exactly. The fractional-scale
    // shapes are covered by the containment property below instead.
    private const string ExactShapeId = "container.chest";
    private const string ExactAnimation = "state";

    [Fact]
    public void EveryOpaqueMaskPixelIsPickableWhereItIsDrawn()
    {
        var pack = LoadStarterPack(out var directory);
        var shape = pack.GetShape(ExactShapeId);
        var frame = shape.GetAnimation(ExactAnimation)
            .GetFrame(direction: 0, sequence: 0);
        var mask = File.ReadAllBytes(Path.Combine(directory, shape.Mask));
        const int OriginX = 400;
        const int OriginY = 300;
        var checkedPixels = 0;

        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var expected = ShapePackLoader.IsOpaque(frame, mask, x, y);
                var (screenX, screenY) = Project(shape, frame, OriginX, OriginY, x, y);
                Assert.Equal(
                    expected,
                    ShapePackLoader.CoversScreenPixel(
                        shape,
                        frame,
                        mask,
                        OriginX,
                        OriginY,
                        screenX,
                        screenY));
                checkedPixels++;
            }
        }

        Assert.Equal(frame.Width * frame.Height, checkedPixels);
    }

    [Fact]
    public void TransparentPixelsMissSoOverlappingSpritesPickTheRightObject()
    {
        var pack = LoadStarterPack(out var directory);
        var shape = pack.GetShape(ExactShapeId);
        var frame = shape.GetAnimation(ExactAnimation)
            .GetFrame(direction: 0, sequence: 0);
        var mask = File.ReadAllBytes(Path.Combine(directory, shape.Mask));
        const int OriginX = 400;
        const int OriginY = 300;

        var transparent = FindTransparentPixel(frame, mask);
        var (screenX, screenY) = Project(
            shape,
            frame,
            OriginX,
            OriginY,
            transparent.X,
            transparent.Y);

        Assert.False(
            ShapePackLoader.CoversScreenPixel(
                shape,
                frame,
                mask,
                OriginX,
                OriginY,
                screenX,
                screenY));
        Assert.False(Covers(OriginX - 4_000, OriginY));
        Assert.False(Covers(OriginX + 4_000, OriginY));
        Assert.False(Covers(OriginX, OriginY - 4_000));
        Assert.False(Covers(OriginX, OriginY + 4_000));

        bool Covers(int x, int y) =>
            ShapePackLoader.CoversScreenPixel(
                shape,
                frame,
                mask,
                OriginX,
                OriginY,
                x,
                y);
    }

    [Theory]
    [InlineData("avatar.knight", "stance")]
    [InlineData("monster.goblin", "run")]
    [InlineData("loot.shortsword", "power")]
    [InlineData(ExactShapeId, ExactAnimation)]
    public void EveryHitLiesInsideTheRectangleTheFrameIsDrawnInto(
        string shapeId,
        string animationName)
    {
        var pack = LoadStarterPack(out var directory);
        var shape = pack.GetShape(shapeId);
        var frame = shape.GetAnimation(animationName)
            .GetFrame(direction: 0, sequence: 0);
        var mask = File.ReadAllBytes(Path.Combine(directory, shape.Mask));
        const int OriginX = 200;
        const int OriginY = 150;
        var left = OriginX - Scale(shape, frame.OriginX);
        var top = OriginY - Scale(shape, frame.OriginY);

        // The drawn rectangle is fractional at scales like 5:8, so its last row
        // and column are partly covered: the extent rounds up, not down.
        var right = left + ScaleUp(shape, frame.Width);
        var bottom = top + ScaleUp(shape, frame.Height);
        var hits = 0;

        // Sweep a window well beyond the drawn rectangle: a sign or rounding
        // error in the inverse mapping shows up as a hit outside it.
        for (var y = top - 40; y < bottom + 40; y++)
        {
            for (var x = left - 40; x < right + 40; x++)
            {
                if (!ShapePackLoader.CoversScreenPixel(
                        shape,
                        frame,
                        mask,
                        OriginX,
                        OriginY,
                        x,
                        y))
                {
                    continue;
                }

                hits++;
                Assert.InRange(x, left, right - 1);
                Assert.InRange(y, top, bottom - 1);
            }
        }

        Assert.True(hits > 0, $"{shapeId} was pickable nowhere.");
    }

    private static (int X, int Y) Project(
        ShapeDefinition shape,
        ShapeFrame frame,
        int originX,
        int originY,
        int frameX,
        int frameY) =>
        (
            originX - Scale(shape, frame.OriginX) + Scale(shape, frameX),
            originY - Scale(shape, frame.OriginY) + Scale(shape, frameY));

    private static int Scale(ShapeDefinition shape, int value) =>
        value * shape.RenderScaleNumerator / shape.RenderScaleDenominator;

    private static int ScaleUp(ShapeDefinition shape, int value)
    {
        var numerator = value * shape.RenderScaleNumerator;
        var denominator = shape.RenderScaleDenominator;
        return (numerator + denominator - 1) / denominator;
    }

    private static (int X, int Y) FindTransparentPixel(
        ShapeFrame frame,
        byte[] mask)
    {
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (!ShapePackLoader.IsOpaque(frame, mask, x, y))
                {
                    return (x, y);
                }
            }
        }

        throw new InvalidOperationException(
            "The frame is fully opaque, so it cannot test a transparent miss.");
    }

    private static ShapePackDefinition LoadStarterPack(out string directory)
    {
        directory = Path.Combine(
            FindRepositoryRoot(),
            "game",
            "assets",
            "shape-packs",
            "flare-starter");
        return ShapePackLoader.LoadFromDirectory(directory);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Ash.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException(
                "Could not find the repository root from the test output.");
    }
}
