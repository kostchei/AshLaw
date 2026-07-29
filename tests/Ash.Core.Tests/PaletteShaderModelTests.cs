using Ash.Core;

namespace Ash.Core.Tests;

/// <summary>
/// M1 task 3 and the M1 exit proof: "all 256 indices map to the exact authored
/// RGB". Build plan §2.6 calls this a five-minute test that catches an otherwise
/// maddening bug.
/// </summary>
public sealed class PaletteShaderModelTests
{
    /// <summary>
    /// A palette where every entry is distinguishable from every other, so an
    /// off-by-one lookup cannot coincidentally produce the right colour.
    /// </summary>
    private static Palette IdentityPalette()
    {
        var entries = new Rgba32[Palette.EntryCount];
        for (var index = 0; index < Palette.EntryCount; index++)
        {
            entries[index] = new Rgba32(
                (byte)index,
                (byte)(255 - index),
                (byte)((index * 7) % 256),
                255);
        }

        return new Palette(entries, Palette.DefaultReservedUi);
    }

    [Fact]
    public void EveryIndexSelectsItsOwnTexel()
    {
        for (var index = 0; index <= 255; index++)
        {
            var u = PaletteShaderModel.PaletteU(PaletteShaderModel.EncodeIndex((byte)index));

            Assert.Equal(index, PaletteShaderModel.SampleTexel(u));
        }
    }

    [Fact]
    public void EveryIndexResolvesToItsExactAuthoredColour()
    {
        var palette = IdentityPalette();

        // Index 0 is the transparency key, which the shader discards.
        Assert.Equal(Rgba32.Transparent, PaletteShaderModel.Resolve(palette, 0));

        for (var index = 1; index <= 255; index++)
        {
            var resolved = PaletteShaderModel.Resolve(palette, (byte)index);

            Assert.Equal(palette[index], resolved);
        }
    }

    /// <summary>
    /// The invariant that actually matters. Selecting the correct integer texel is
    /// not enough — the sample must land at the texel's centre, half a texel from
    /// either boundary, or the result depends on how a given GPU rounds.
    /// </summary>
    [Fact]
    public void EveryIndexSamplesItsTexelCentre()
    {
        for (var index = 0; index <= 255; index++)
        {
            var u = PaletteShaderModel.PaletteU(PaletteShaderModel.EncodeIndex((byte)index));

            Assert.Equal(0.5f, PaletteShaderModel.SubTexelPosition(u), 5);
            Assert.Equal(0.5f, PaletteShaderModel.BoundaryMargin(u), 5);
        }
    }

    /// <summary>
    /// Proves the previous test has teeth. Without the half-texel offset every
    /// sample sits exactly on a texel boundary — margin 0.0 — which is the
    /// documented failure mode, and is invisible to a test that only checks which
    /// integer texel was selected.
    /// </summary>
    [Fact]
    public void OmittingTheHalfTexelOffsetPutsEverySampleOnATexelBoundary()
    {
        var worstMargin = 1.0f;
        for (var index = 0; index <= 255; index++)
        {
            var u = PaletteShaderModel.PaletteUWithoutHalfTexelOffset(
                PaletteShaderModel.EncodeIndex((byte)index));

            worstMargin = MathF.Min(worstMargin, PaletteShaderModel.BoundaryMargin(u));
        }

        Assert.Equal(0.0f, worstMargin, 5);
    }

    [Fact]
    public void PaletteUploadsAsExactlyOneKilobyteOfRgba()
    {
        var palette = IdentityPalette();

        var bytes = palette.ToTextureBytes();

        Assert.Equal(1024, bytes.Length);
        for (var index = 0; index < Palette.EntryCount; index++)
        {
            Assert.Equal(palette[index].R, bytes[(index * 4) + 0]);
            Assert.Equal(palette[index].G, bytes[(index * 4) + 1]);
            Assert.Equal(palette[index].B, bytes[(index * 4) + 2]);
            Assert.Equal(palette[index].A, bytes[(index * 4) + 3]);
        }
    }
}
