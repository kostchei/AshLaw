namespace Ash.Core;

/// <summary>
/// A CPU replica of the palette-lookup arithmetic in the fragment shader
/// (build plan §2.6), so the half-texel offset can be verified in a headless
/// test instead of only by eye on a GPU.
/// </summary>
/// <remarks>
/// <para>
/// Every computation here is <see cref="float"/>, not <see cref="double"/>,
/// because the shader runs in 32-bit float. Widening would hide exactly the
/// rounding behaviour under test.
/// </para>
/// <para>
/// The invariant that matters is <em>not</em> merely that each index selects the
/// right texel — it is that each index samples the texel's <em>centre</em>.
/// Without the <c>+0.5</c> offset the arithmetic still lands on the correct
/// integer, but it lands exactly on the boundary between two texels, where the
/// result depends on how a particular GPU rounds. That is the "half the palette
/// samples the neighbouring entry" failure §2.6 warns about, and
/// <see cref="SubTexelPosition"/> is what detects it.
/// </para>
/// </remarks>
public static class PaletteShaderModel
{
    /// <summary>Width of the palette texture, in texels.</summary>
    public const int PaletteWidth = Palette.EntryCount;

    /// <summary>
    /// The value an <c>R8</c> index texture yields for a stored index: the
    /// shader's <c>texture(index_tex, UV).r</c>, quantised to <c>i/255</c>.
    /// </summary>
    public static float EncodeIndex(byte index) => index / 255.0f;

    /// <summary>
    /// The shader's <c>pal_uv.x</c>: <c>(idx * 255.0 + 0.5) / 256.0</c>.
    /// </summary>
    public static float PaletteU(float encodedIndex) =>
        ((encodedIndex * 255.0f) + 0.5f) / PaletteWidth;

    /// <summary>
    /// The same computation with the half-texel offset omitted. Exists solely so
    /// a test can prove the offset is doing something.
    /// </summary>
    public static float PaletteUWithoutHalfTexelOffset(float encodedIndex) =>
        encodedIndex * 255.0f / PaletteWidth;

    /// <summary>
    /// The texel a nearest-neighbour sampler reads for the given normalised
    /// coordinate.
    /// </summary>
    public static int SampleTexel(float u)
    {
        var texel = (int)MathF.Floor(u * PaletteWidth);
        return Math.Clamp(texel, 0, PaletteWidth - 1);
    }

    /// <summary>
    /// Where within its texel the sample lands, in the range [0, 1). A correct
    /// lookup returns 0.5 — dead centre, maximally far from both boundaries.
    /// </summary>
    public static float SubTexelPosition(float u)
    {
        var scaled = u * PaletteWidth;
        return scaled - MathF.Floor(scaled);
    }

    /// <summary>
    /// Distance from the sample point to the nearest texel boundary, in texels.
    /// 0.5 is the best achievable; 0.0 means the sample sits on a boundary and
    /// the selected texel is at the mercy of driver rounding.
    /// </summary>
    public static float BoundaryMargin(float u)
    {
        var position = SubTexelPosition(u);
        return MathF.Min(position, 1.0f - position);
    }

    /// <summary>
    /// The complete lookup: index in, colour out, exactly as the shader resolves
    /// it. Returns <see cref="Rgba32.Transparent"/> for the transparency key,
    /// matching the shader's <c>discard</c>.
    /// </summary>
    public static Rgba32 Resolve(Palette palette, byte index)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (index == Palette.TransparentIndex)
        {
            return Rgba32.Transparent;
        }

        return palette[SampleTexel(PaletteU(EncodeIndex(index)))];
    }
}
