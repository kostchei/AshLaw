namespace Ash.Core;

/// <summary>Integer world position in world units (ADR 0001).</summary>
public readonly record struct Vec3i(int X, int Y, int Z)
{
    public static Vec3i Zero => new(0, 0, 0);
}

/// <summary>Integer screen position in the logical 1280x800 canvas.</summary>
public readonly record struct Vec2i(int X, int Y)
{
    public static Vec2i Zero => new(0, 0);
}

/// <summary>
/// The oblique world-to-screen projection and its exact inverse (M1 task 6).
/// Both directions live in this one file deliberately: they are a matched pair,
/// and the round-trip only holds if they are edited together.
/// </summary>
/// <remarks>
/// <para>
/// Ultima VIII's projection is a 2:1 oblique:
/// </para>
/// <code>
/// screen_x = (x - y) / UnitsPerHorizontalPixel
/// screen_y = (x + y) / (2 * UnitsPerHorizontalPixel) - z / UnitsPerVerticalPixel
/// </code>
/// <para>
/// The inverse is exact only for world points that lie on the projection lattice
/// — the forward map is many-to-one, since raising Z and moving away from the
/// camera produce the same pixel. <see cref="ScreenToWorld"/> therefore takes the
/// Z to solve at, which is what a picking query actually has.
/// </para>
/// <para>
/// This is the one place floats would be tempting. They are not used: every
/// operation is integer, so the round-trip is exact rather than
/// nearly-exact, and no epsilon appears in the tests.
/// </para>
/// </remarks>
public static class ObliqueProjection
{
    /// <summary>World units per horizontal screen pixel. One tile (256u) is 32px wide.</summary>
    public const int UnitsPerHorizontalPixel = 8;

    /// <summary>
    /// World units of elevation per vertical screen pixel. One vertical level is
    /// 8 units (ADR 0001) and lifts a sprite by 8 pixels.
    /// </summary>
    public const int UnitsPerVerticalPixel = 1;

    /// <summary>
    /// The lattice spacing the projection is exactly invertible on. A world X or Y
    /// must be a multiple of this for <see cref="ScreenToWorld"/> to recover it.
    /// </summary>
    public const int LatticeStep = UnitsPerHorizontalPixel * 2;

    public static Vec2i WorldToScreen(Vec3i world)
    {
        var screenX = FloorDiv(world.X - world.Y, UnitsPerHorizontalPixel);
        var screenY =
            FloorDiv(world.X + world.Y, UnitsPerHorizontalPixel * 2) -
            FloorDiv(world.Z, UnitsPerVerticalPixel);
        return new Vec2i(screenX, screenY);
    }

    /// <summary>
    /// Recovers the world position that projects to <paramref name="screen"/> at
    /// elevation <paramref name="z"/>.
    /// </summary>
    public static Vec3i ScreenToWorld(Vec2i screen, int z)
    {
        // Undo the elevation offset first, so what remains is the ground-plane
        // projection of (x, y).
        var groundY = screen.Y + FloorDiv(z, UnitsPerVerticalPixel);

        // screen_x = (x - y)/h  and  groundY = (x + y)/(2h)
        //   =>  x - y = screen_x * h
        //       x + y = groundY * 2h
        var difference = screen.X * UnitsPerHorizontalPixel;
        var sum = groundY * UnitsPerHorizontalPixel * 2;

        var x = (sum + difference) / 2;
        var y = (sum - difference) / 2;
        return new Vec3i(x, y, z);
    }

    /// <summary>
    /// Whether <paramref name="world"/> lies on the lattice, and so survives a
    /// world → screen → world round trip unchanged.
    /// </summary>
    public static bool IsOnLattice(Vec3i world) =>
        Modulo(world.X, LatticeStep) == 0 &&
        Modulo(world.Y, LatticeStep) == 0 &&
        Modulo(world.Z, UnitsPerVerticalPixel) == 0;

    /// <summary>Snaps a world position onto the invertible lattice.</summary>
    public static Vec3i SnapToLattice(Vec3i world) => new(
        FloorDiv(world.X, LatticeStep) * LatticeStep,
        FloorDiv(world.Y, LatticeStep) * LatticeStep,
        FloorDiv(world.Z, UnitsPerVerticalPixel) * UnitsPerVerticalPixel);

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && ((value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
    }

    private static int Modulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
