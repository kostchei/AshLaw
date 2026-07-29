namespace Ash.Core;

/// <summary>
/// Maps between physical window pixels and the logical 640x400 canvas:
/// integer scale, aspect kept, letterboxed.
/// </summary>
/// <remarks>
/// Godot's <c>viewport</c> stretch mode already does this for mouse input. The
/// build plan says to write the test anyway, because "already handled by the
/// engine" is exactly the assumption that silently stops being true after a
/// project-setting change. Keeping the arithmetic here means the round-trip is
/// asserted in CI without a display.
/// </remarks>
public sealed class ViewportScaling
{
    public const int LogicalWidth = 640;
    public const int LogicalHeight = 400;

    private ViewportScaling(int scale, int offsetX, int offsetY, int windowWidth, int windowHeight)
    {
        Scale = scale;
        OffsetX = offsetX;
        OffsetY = offsetY;
        WindowWidth = windowWidth;
        WindowHeight = windowHeight;
    }

    /// <summary>Integer magnification factor, at least 1.</summary>
    public int Scale { get; }

    /// <summary>Left letterbox width in physical pixels.</summary>
    public int OffsetX { get; }

    /// <summary>Top letterbox height in physical pixels.</summary>
    public int OffsetY { get; }

    public int WindowWidth { get; }

    public int WindowHeight { get; }

    /// <summary>
    /// Derives the largest integer scale that fits the logical canvas inside the
    /// window, and centres it.
    /// </summary>
    public static ViewportScaling ForWindow(int windowWidth, int windowHeight)
    {
        if (windowWidth < LogicalWidth || windowHeight < LogicalHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowWidth),
                $"Window {windowWidth}x{windowHeight} is smaller than the logical " +
                $"{LogicalWidth}x{LogicalHeight} canvas; there is no valid integer scale.");
        }

        var scale = Math.Min(windowWidth / LogicalWidth, windowHeight / LogicalHeight);
        var usedWidth = LogicalWidth * scale;
        var usedHeight = LogicalHeight * scale;

        return new ViewportScaling(
            scale,
            (windowWidth - usedWidth) / 2,
            (windowHeight - usedHeight) / 2,
            windowWidth,
            windowHeight);
    }

    /// <summary>
    /// Physical position of a logical pixel's top-left corner.
    /// </summary>
    public Vec2i LogicalToScreen(Vec2i logical) => new(
        (logical.X * Scale) + OffsetX,
        (logical.Y * Scale) + OffsetY);

    /// <summary>
    /// The logical pixel containing a physical position. Points inside the
    /// letterbox have no logical pixel, so callers must check
    /// <see cref="IsInsideCanvas"/> first rather than receive a clamped guess.
    /// </summary>
    public Vec2i ScreenToLogical(Vec2i screen)
    {
        if (!IsInsideCanvas(screen))
        {
            throw new ArgumentOutOfRangeException(
                nameof(screen),
                screen,
                $"Physical point lies in the letterbox, outside the {LogicalWidth}x" +
                $"{LogicalHeight} canvas drawn at scale {Scale} offset ({OffsetX}, {OffsetY}).");
        }

        return new Vec2i(
            (screen.X - OffsetX) / Scale,
            (screen.Y - OffsetY) / Scale);
    }

    public bool IsInsideCanvas(Vec2i screen) =>
        screen.X >= OffsetX &&
        screen.Y >= OffsetY &&
        screen.X < OffsetX + (LogicalWidth * Scale) &&
        screen.Y < OffsetY + (LogicalHeight * Scale);
}
