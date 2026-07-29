using Ash.Core;

namespace Ash.Core.Tests;

/// <summary>
/// M1 task 6 and the M1 round-trip exit proof.
/// </summary>
public sealed class ProjectionAndViewportTests
{
    [Fact]
    public void OriginProjectsToTheScreenOrigin()
    {
        Assert.Equal(Vec2i.Zero, ObliqueProjection.WorldToScreen(Vec3i.Zero));
    }

    [Fact]
    public void MovingAlongXAndYMovesInOppositeScreenDirections()
    {
        var step = ObliqueProjection.LatticeStep;

        var alongX = ObliqueProjection.WorldToScreen(new Vec3i(step, 0, 0));
        var alongY = ObliqueProjection.WorldToScreen(new Vec3i(0, step, 0));

        Assert.Equal(-alongX.X, alongY.X);
        Assert.Equal(alongX.Y, alongY.Y);
    }

    [Fact]
    public void RaisingElevationMovesUpTheScreen()
    {
        var ground = ObliqueProjection.WorldToScreen(new Vec3i(0, 0, 0));
        var raised = ObliqueProjection.WorldToScreen(new Vec3i(0, 0, 8));

        Assert.True(raised.Y < ground.Y);
    }

    /// <summary>
    /// The exit proof: world → screen → world is exact for every lattice point.
    /// No epsilon appears, because the projection is integer arithmetic.
    /// </summary>
    [Fact]
    public void WorldScreenWorldRoundTripsExactlyOnTheLattice()
    {
        var step = ObliqueProjection.LatticeStep;

        for (var x = -20 * step; x <= 20 * step; x += step)
        {
            for (var y = -20 * step; y <= 20 * step; y += step)
            {
                foreach (var z in new[] { -64, -8, 0, 8, 64, 256 })
                {
                    var world = new Vec3i(x, y, z);
                    Assert.True(ObliqueProjection.IsOnLattice(world));

                    var screen = ObliqueProjection.WorldToScreen(world);
                    var recovered = ObliqueProjection.ScreenToWorld(screen, z);

                    Assert.Equal(world, recovered);
                }
            }
        }
    }

    [Fact]
    public void SnappingProducesALatticePointThatRoundTrips()
    {
        foreach (var raw in new[]
        {
            new Vec3i(1, 2, 3),
            new Vec3i(-1, -2, -3),
            new Vec3i(1023, 77, 15),
            new Vec3i(-1023, -77, -15),
        })
        {
            var snapped = ObliqueProjection.SnapToLattice(raw);

            Assert.True(ObliqueProjection.IsOnLattice(snapped));
            Assert.Equal(
                snapped,
                ObliqueProjection.ScreenToWorld(
                    ObliqueProjection.WorldToScreen(snapped),
                    snapped.Z));
        }
    }

    // 1x through 4x, plus two letterboxed cases: a wide window and a tall one.
    public static TheoryData<int, int, int> WindowSizes =>
        new()
        {
            { 1280, 800, 1 },
            { 2560, 1600, 2 },
            { 3840, 2400, 3 },
            { 5120, 3200, 4 },
            { 1920, 1080, 1 },
            { 2560, 1440, 1 },
            { 1600, 2400, 1 },
        };

    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void IntegerScaleIsTheLargestThatFits(int windowWidth, int windowHeight, int expectedScale)
    {
        var viewport = ViewportScaling.ForWindow(windowWidth, windowHeight);

        Assert.Equal(expectedScale, viewport.Scale);
        Assert.True(ViewportScaling.LogicalWidth * viewport.Scale <= windowWidth);
        Assert.True(ViewportScaling.LogicalHeight * viewport.Scale <= windowHeight);
    }

    /// <summary>
    /// The exit proof: <c>ScreenToLogical(LogicalToScreen(p)) == p</c> at every
    /// scale, windowed and fullscreen-shaped.
    /// </summary>
    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void LogicalScreenLogicalRoundTripsAtEveryScale(
        int windowWidth,
        int windowHeight,
        int expectedScale)
    {
        var viewport = ViewportScaling.ForWindow(windowWidth, windowHeight);
        Assert.Equal(expectedScale, viewport.Scale);

        for (var x = 0; x < ViewportScaling.LogicalWidth; x++)
        {
            for (var y = 0; y < ViewportScaling.LogicalHeight; y++)
            {
                var logical = new Vec2i(x, y);

                var screen = viewport.LogicalToScreen(logical);

                Assert.True(viewport.IsInsideCanvas(screen));
                Assert.Equal(logical, viewport.ScreenToLogical(screen));
            }
        }
    }

    /// <summary>
    /// Every physical pixel inside the canvas must map to a valid logical pixel —
    /// not just the top-left corner of each magnified block.
    /// </summary>
    [Theory]
    [MemberData(nameof(WindowSizes))]
    public void EveryPhysicalPixelInsideTheCanvasMapsIntoBounds(
        int windowWidth,
        int windowHeight,
        int expectedScale)
    {
        var viewport = ViewportScaling.ForWindow(windowWidth, windowHeight);
        Assert.Equal(expectedScale, viewport.Scale);

        for (var x = 0; x < windowWidth; x++)
        {
            for (var y = 0; y < windowHeight; y++)
            {
                var screen = new Vec2i(x, y);
                if (!viewport.IsInsideCanvas(screen))
                {
                    continue;
                }

                var logical = viewport.ScreenToLogical(screen);

                Assert.InRange(logical.X, 0, ViewportScaling.LogicalWidth - 1);
                Assert.InRange(logical.Y, 0, ViewportScaling.LogicalHeight - 1);
            }
        }
    }

    [Fact]
    public void LetterboxIsCentredAndPointsInsideItAreRejected()
    {
        var viewport = ViewportScaling.ForWindow(1920, 1080);

        // 1920 - 1280 = 640 total horizontal slack, 320 each side.
        Assert.Equal(320, viewport.OffsetX);
        Assert.Equal(140, viewport.OffsetY);

        Assert.False(viewport.IsInsideCanvas(new Vec2i(0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => viewport.ScreenToLogical(new Vec2i(0, 0)));
    }

    [Fact]
    public void WindowSmallerThanTheLogicalCanvasIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ViewportScaling.ForWindow(1279, 800));
        Assert.Throws<ArgumentOutOfRangeException>(() => ViewportScaling.ForWindow(1280, 799));
    }
}
