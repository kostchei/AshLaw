using Ash.Core;

namespace Ash.Core.Tests;

public sealed class PaletteTests
{
    private static Palette Uniform(byte value)
    {
        var entries = new Rgba32[Palette.EntryCount];
        Array.Fill(entries, new Rgba32(value, value, value, 255));
        return new Palette(entries, Palette.DefaultReservedUi);
    }

    private static Palette Ramp()
    {
        var entries = new Rgba32[Palette.EntryCount];
        for (var index = 0; index < Palette.EntryCount; index++)
        {
            entries[index] = new Rgba32((byte)index, 0, 0, 255);
        }

        return new Palette(entries, Palette.DefaultReservedUi);
    }

    [Fact]
    public void PaletteMustHaveExactlyTwoHundredAndFiftySixEntries()
    {
        Assert.Throws<ArgumentException>(
            () => new Palette(new Rgba32[255], Palette.DefaultReservedUi));
    }

    /// <summary>GFX-044: a world fade must not be able to corrupt gump chrome.</summary>
    [Fact]
    public void GlobalTransformsSkipTheReservedUiRange()
    {
        var palette = Uniform(200);
        var reserved = palette.ReservedUi;

        foreach (var transformed in new[]
        {
            palette.Fade(new Rgba32(0, 0, 0, 255), 255),
            palette.Tint(new Rgba32(0, 0, 0, 255)),
            palette.Invert(),
        })
        {
            for (var index = 0; index < Palette.EntryCount; index++)
            {
                if (reserved.Contains(index))
                {
                    Assert.Equal(palette[index], transformed[index]);
                }
                else
                {
                    Assert.NotEqual(palette[index], transformed[index]);
                }
            }
        }
    }

    [Fact]
    public void FadingFullyReachesTheTargetColour()
    {
        var faded = Uniform(200).Fade(new Rgba32(0, 0, 0, 255), 255);

        Assert.Equal(new Rgba32(0, 0, 0, 255), faded[10]);
    }

    [Fact]
    public void FadingByZeroChangesNothing()
    {
        var palette = Uniform(200);

        var faded = palette.Fade(new Rgba32(0, 0, 0, 255), 0);

        Assert.Equal(palette[10], faded[10]);
    }

    [Fact]
    public void CyclingRotatesOnlyTheDeclaredRange()
    {
        var palette = Ramp();
        var range = new IndexRange(10, 4);

        var cycled = palette.Cycle(range, 1);

        // The window 10..13 held 10, 11, 12, 13; one step forward makes it
        // 13, 10, 11, 12.
        Assert.Equal(13, cycled[10].R);
        Assert.Equal(10, cycled[11].R);
        Assert.Equal(11, cycled[12].R);
        Assert.Equal(12, cycled[13].R);

        Assert.Equal(9, cycled[9].R);
        Assert.Equal(14, cycled[14].R);
    }

    [Fact]
    public void CyclingByTheRangeLengthIsIdentity()
    {
        var palette = Ramp();
        var range = new IndexRange(32, 16);

        var cycled = palette.Cycle(range, 16);

        for (var index = 0; index < Palette.EntryCount; index++)
        {
            Assert.Equal(palette[index], cycled[index]);
        }
    }

    [Fact]
    public void CyclingIsReversible()
    {
        var palette = Ramp();
        var range = new IndexRange(64, 8);

        var roundTripped = palette.Cycle(range, 3).Cycle(range, -3);

        for (var index = 0; index < Palette.EntryCount; index++)
        {
            Assert.Equal(palette[index], roundTripped[index]);
        }
    }

    [Fact]
    public void TransformsDoNotMutateTheOriginal()
    {
        var palette = Uniform(200);

        _ = palette.Invert();

        Assert.Equal(new Rgba32(200, 200, 200, 255), palette[0]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void OutOfRangeIndicesThrow(int index)
    {
        var palette = Uniform(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => palette[index]);
    }

    [Fact]
    public void RangeExtendingPastTheEndOfThePaletteIsRejected()
    {
        var palette = Uniform(1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => palette.Cycle(new IndexRange(250, 10), 1));
    }
}
