namespace Ash.Core;

/// <summary>
/// Pixel-grid camera follow state advanced by a fixed simulation tick.
/// </summary>
/// <remarks>
/// The camera stores integer logical-pixel offsets so following an actor never
/// introduces fractional sampling into nearest-filtered world art. Given the
/// same target sequence, the output is byte-for-byte deterministic.
/// </remarks>
public sealed class FollowCamera
{
    public FollowCamera(Vec2i initialOffset)
    {
        Offset = initialOffset;
    }

    public Vec2i Offset { get; private set; }

    public bool IsAt(Vec2i target) => Offset == target;

    public void SnapTo(Vec2i target)
    {
        Offset = target;
    }

    public Vec2i StepToward(Vec2i target, int maxPixelsPerAxis)
    {
        if (maxPixelsPerAxis <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPixelsPerAxis),
                maxPixelsPerAxis,
                "Camera step must be positive.");
        }

        Offset = new Vec2i(
            MoveAxis(Offset.X, target.X, maxPixelsPerAxis),
            MoveAxis(Offset.Y, target.Y, maxPixelsPerAxis));
        return Offset;
    }

    private static int MoveAxis(int current, int target, int maximumDelta)
    {
        var delta = (long)target - current;
        if (Math.Abs(delta) <= maximumDelta)
        {
            return target;
        }

        return checked(current + Math.Sign(delta) * maximumDelta);
    }
}
