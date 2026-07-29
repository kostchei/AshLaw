namespace Ash.Core;

public readonly record struct Rgba32(byte R, byte G, byte B, byte A)
{
    public static Rgba32 Transparent => new(0, 0, 0, 0);
}

/// <summary>A contiguous span of palette indices.</summary>
public readonly record struct IndexRange(int Start, int Count)
{
    public int EndExclusive => Start + Count;

    public bool Contains(int index) => index >= Start && index < EndExclusive;

    public void Validate(string parameterName)
    {
        if (Start < 0 || Count < 0 || EndExclusive > Palette.EntryCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                this,
                $"Range must lie within 0..{Palette.EntryCount - 1}.");
        }
    }
}

/// <summary>
/// A 256-entry palette matching the <c>256x1 RGBA8</c> palette texture in build
/// plan §2.6.
/// </summary>
/// <remarks>
/// Global transforms (fade, tint, inversion) never touch
/// <see cref="ReservedUi"/>, so a world fade cannot corrupt gump chrome
/// (GFX-044).
/// </remarks>
public sealed class Palette
{
    public const int EntryCount = 256;

    /// <summary>Index 0 is the transparency key; the shader discards it.</summary>
    public const byte TransparentIndex = 0;

    /// <summary>Default reserved gump-chrome range from §2.6.</summary>
    public static readonly IndexRange DefaultReservedUi = new(240, 16);

    private readonly Rgba32[] _entries;

    public Palette(IReadOnlyList<Rgba32> entries, IndexRange reservedUi)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count != EntryCount)
        {
            throw new ArgumentException(
                $"A palette must have exactly {EntryCount} entries; found {entries.Count}.",
                nameof(entries));
        }

        reservedUi.Validate(nameof(reservedUi));
        _entries = entries.ToArray();
        ReservedUi = reservedUi;
    }

    public IndexRange ReservedUi { get; }

    public Rgba32 this[int index]
    {
        get
        {
            ThrowIfOutOfRange(index);
            return _entries[index];
        }
    }

    /// <summary>The palette as it is uploaded: 256 RGBA8 texels, 1 KB.</summary>
    public byte[] ToTextureBytes()
    {
        var bytes = new byte[EntryCount * 4];
        for (var index = 0; index < EntryCount; index++)
        {
            var entry = _entries[index];
            bytes[(index * 4) + 0] = entry.R;
            bytes[(index * 4) + 1] = entry.G;
            bytes[(index * 4) + 2] = entry.B;
            bytes[(index * 4) + 3] = entry.A;
        }

        return bytes;
    }

    public Palette WithEntry(int index, Rgba32 colour)
    {
        ThrowIfOutOfRange(index);
        var copy = _entries.ToArray();
        copy[index] = colour;
        return new Palette(copy, ReservedUi);
    }

    /// <summary>
    /// Blends every non-reserved entry towards <paramref name="target"/>.
    /// <paramref name="amount"/> runs 0 (unchanged) to 255 (fully
    /// <paramref name="target"/>).
    /// </summary>
    public Palette Fade(Rgba32 target, int amount)
    {
        if (amount is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Amount must be 0..255.");
        }

        return MapUnreserved(entry => new Rgba32(
            Blend(entry.R, target.R, amount),
            Blend(entry.G, target.G, amount),
            Blend(entry.B, target.B, amount),
            entry.A));
    }

    /// <summary>Multiplies every non-reserved entry by a colour.</summary>
    public Palette Tint(Rgba32 tint) =>
        MapUnreserved(entry => new Rgba32(
            Multiply(entry.R, tint.R),
            Multiply(entry.G, tint.G),
            Multiply(entry.B, tint.B),
            entry.A));

    public Palette Invert() =>
        MapUnreserved(entry => new Rgba32(
            (byte)(255 - entry.R),
            (byte)(255 - entry.G),
            (byte)(255 - entry.B),
            entry.A));

    /// <summary>
    /// Rotates a declared index range by <paramref name="steps"/> (GFX-043).
    /// Cycling is a local animation, so unlike the global transforms it is
    /// allowed to target a reserved range if content declares one.
    /// </summary>
    public Palette Cycle(IndexRange range, int steps)
    {
        range.Validate(nameof(range));
        if (range.Count <= 1)
        {
            return this;
        }

        var copy = _entries.ToArray();
        for (var offset = 0; offset < range.Count; offset++)
        {
            // Positive steps move an entry towards higher indices.
            var source = Modulo(offset - steps, range.Count);
            copy[range.Start + offset] = _entries[range.Start + source];
        }

        return new Palette(copy, ReservedUi);
    }

    private Palette MapUnreserved(Func<Rgba32, Rgba32> transform)
    {
        var copy = new Rgba32[EntryCount];
        for (var index = 0; index < EntryCount; index++)
        {
            copy[index] = ReservedUi.Contains(index)
                ? _entries[index]
                : transform(_entries[index]);
        }

        return new Palette(copy, ReservedUi);
    }

    private static byte Blend(byte from, byte to, int amount) =>
        (byte)(((from * (255 - amount)) + (to * amount)) / 255);

    private static byte Multiply(byte value, byte factor) => (byte)(value * factor / 255);

    private static int Modulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static void ThrowIfOutOfRange(int index)
    {
        if (index is < 0 or >= EntryCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Palette index must be 0..{EntryCount - 1}.");
        }
    }
}
