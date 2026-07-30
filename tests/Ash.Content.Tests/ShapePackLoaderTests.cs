using System.Text.Json.Nodes;

namespace Ash.Content.Tests;

public sealed class ShapePackLoaderTests
{
    [Fact]
    public void ValidPackLoadsAndExposesDirectionalFrames()
    {
        var pack = ShapePackLoader.Parse(ValidJson());

        Assert.Equal("test-pack", pack.PackId);
        var shape = pack.GetShape("hero");
        Assert.Equal(0.625f, shape.RenderScale);
        Assert.Equal(
            new ShapeFrame
            {
                Sequence = 0,
                Direction = 1,
                X = 8,
                Y = 0,
                Width = 8,
                Height = 8,
                OriginX = 4,
                OriginY = 7,
                DurationMs = 100,
                MaskOffset = 8,
            },
            shape.GetAnimation("idle").GetFrame(1, 0));
    }

    [Fact]
    public void UnknownJsonMembersAreRejected()
    {
        var json = JsonNode.Parse(ValidJson())!.AsObject();
        json["mystery"] = true;

        var exception = Assert.Throws<ShapePackException>(
            () => ShapePackLoader.Parse(json.ToJsonString()));

        Assert.Contains("mystery", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingDirectionalFrameIsRejected()
    {
        var json = JsonNode.Parse(ValidJson())!.AsObject();
        var frames = json["shapes"]![0]!["animations"]![0]!["frames"]!.AsArray();
        frames.RemoveAt(1);

        var exception = Assert.Throws<ShapePackException>(
            () => ShapePackLoader.Parse(json.ToJsonString()));

        Assert.Contains("expected 2", exception.Message);
    }

    [Fact]
    public void AssetPathCannotEscapeThePack()
    {
        var json = JsonNode.Parse(ValidJson())!.AsObject();
        json["shapes"]![0]!["atlas"] = "../outside.png";

        var exception = Assert.Throws<ShapePackException>(
            () => ShapePackLoader.Parse(json.ToJsonString()));

        Assert.Contains("relative asset path", exception.Message);
    }

    [Fact]
    public void DirectoryLoadChecksExactMaskLength()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-shape-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, ShapePackLoader.ManifestFileName),
                ValidJson());
            File.WriteAllBytes(Path.Combine(directory, "hero.png"), [0x89]);
            File.WriteAllBytes(Path.Combine(directory, "hero.mask"), new byte[15]);

            var exception = Assert.Throws<ShapePackException>(
                () => ShapePackLoader.LoadFromDirectory(directory));

            Assert.Contains("requires exactly 16", exception.Message);

            File.WriteAllBytes(Path.Combine(directory, "hero.mask"), new byte[16]);
            var pack = ShapePackLoader.LoadFromDirectory(directory);
            Assert.Equal("hero", Assert.Single(pack.Shapes).Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AlphaMaskUsesMostSignificantBitFirstAndRejectsOutsidePixels()
    {
        var frame = new ShapeFrame
        {
            Sequence = 0,
            Direction = 0,
            X = 0,
            Y = 0,
            Width = 8,
            Height = 1,
            OriginX = 0,
            OriginY = 0,
            DurationMs = 100,
            MaskOffset = 1,
        };
        byte[] mask = [0, 0b1010_0001];

        Assert.True(ShapePackLoader.IsOpaque(frame, mask, 0, 0));
        Assert.False(ShapePackLoader.IsOpaque(frame, mask, 1, 0));
        Assert.True(ShapePackLoader.IsOpaque(frame, mask, 2, 0));
        Assert.True(ShapePackLoader.IsOpaque(frame, mask, 7, 0));
        Assert.False(ShapePackLoader.IsOpaque(frame, mask, -1, 0));
        Assert.False(ShapePackLoader.IsOpaque(frame, mask, 8, 0));
    }

    private static string ValidJson() =>
        """
        {
          "schema_version": 1,
          "pack_id": "test-pack",
          "attribution": {
            "title": "Test art",
            "source": "https://example.invalid/art",
            "license": "CC-BY-SA-3.0-or-later",
            "revision": "test",
            "authors": ["Test Artist"]
          },
          "shapes": [
            {
              "id": "hero",
              "atlas": "hero.png",
              "atlas_width": 16,
              "atlas_height": 8,
              "mask": "hero.mask",
              "render_scale_numerator": 5,
              "render_scale_denominator": 8,
              "footprint": {
                "width": 128,
                "depth": 128
              },
              "height": 64,
              "flags": "animated, sprite",
              "sort_bias": 0,
              "animations": [
                {
                  "name": "idle",
                  "playback": "ping_pong",
                  "directions": 2,
                  "frames_per_direction": 1,
                  "frames": [
                    {
                      "sequence": 0,
                      "direction": 0,
                      "x": 0,
                      "y": 0,
                      "width": 8,
                      "height": 8,
                      "origin_x": 4,
                      "origin_y": 7,
                      "duration_ms": 100,
                      "mask_offset": 0
                    },
                    {
                      "sequence": 0,
                      "direction": 1,
                      "x": 8,
                      "y": 0,
                      "width": 8,
                      "height": 8,
                      "origin_x": 4,
                      "origin_y": 7,
                      "duration_ms": 100,
                      "mask_offset": 8
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;
}
