namespace Ash.Core.Tests;

public sealed class FollowCameraTests
{
    [Fact]
    public void SnapAndFixedTickFollowStayOnIntegerPixelGrid()
    {
        var camera = new FollowCamera(new Vec2i(0, 0));

        camera.SnapTo(new Vec2i(10, -4));
        Assert.Equal(new Vec2i(10, -4), camera.Offset);

        Assert.Equal(
            new Vec2i(7, -1),
            camera.StepToward(new Vec2i(2, 8), maxPixelsPerAxis: 3));
        Assert.Equal(
            new Vec2i(4, 2),
            camera.StepToward(new Vec2i(2, 8), maxPixelsPerAxis: 3));
        Assert.Equal(
            new Vec2i(2, 5),
            camera.StepToward(new Vec2i(2, 8), maxPixelsPerAxis: 3));
        Assert.Equal(
            new Vec2i(2, 8),
            camera.StepToward(new Vec2i(2, 8), maxPixelsPerAxis: 3));
        Assert.True(camera.IsAt(new Vec2i(2, 8)));
    }

    [Fact]
    public void FixedTargetSequenceProducesIdenticalOffsets()
    {
        Vec2i[] targets =
        [
            new(40, 10),
            new(-12, 28),
            new(-12, 28),
            new(7, -9),
        ];
        var left = new FollowCamera(Vec2i.Zero);
        var right = new FollowCamera(Vec2i.Zero);

        var leftOffsets = targets
            .Select(target => left.StepToward(target, 2))
            .ToArray();
        var rightOffsets = targets
            .Select(target => right.StepToward(target, 2))
            .ToArray();

        Assert.Equal(leftOffsets, rightOffsets);
    }

    [Fact]
    public void FollowStepMustBePositive()
    {
        var camera = new FollowCamera(Vec2i.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.StepToward(new Vec2i(1, 1), 0));
    }
}
