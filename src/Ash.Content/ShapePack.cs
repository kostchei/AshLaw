using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ash.Content;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<ShapeFlags>))]
public enum ShapeFlags
{
    None = 0,
    Animated = 1 << 0,
    Translucent = 1 << 1,
    Draw = 1 << 2,
    Solid = 1 << 3,
    Occludes = 1 << 4,
    LargeFlatSquare = 1 << 5,
    Fixed = 1 << 6,
    Sprite = 1 << 7,
}

[JsonConverter(typeof(JsonStringEnumConverter<ShapePlayback>))]
public enum ShapePlayback
{
    Loop,
    PingPong,
    Once,
}

public sealed record ShapePackAttribution
{
    public required string Title { get; init; }

    public required string Source { get; init; }

    public required string License { get; init; }

    public required string Revision { get; init; }

    public required IReadOnlyList<string> Authors { get; init; }
}

public sealed record ShapeFootprint
{
    public required int Width { get; init; }

    public required int Depth { get; init; }
}

public sealed record ShapeFrame
{
    public required int Sequence { get; init; }

    public required int Direction { get; init; }

    public required int X { get; init; }

    public required int Y { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int OriginX { get; init; }

    public required int OriginY { get; init; }

    public required int DurationMs { get; init; }

    public required int MaskOffset { get; init; }

    [JsonIgnore]
    public int MaskByteLength => checked(((Width * Height) + 7) / 8);
}

public sealed record ShapeAnimation
{
    public required string Name { get; init; }

    public required ShapePlayback Playback { get; init; }

    public required int Directions { get; init; }

    public required int FramesPerDirection { get; init; }

    public required IReadOnlyList<ShapeFrame> Frames { get; init; }

    public ShapeFrame GetFrame(int direction, int sequence)
    {
        if (direction < 0 || direction >= Directions)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        if (sequence < 0 || sequence >= FramesPerDirection)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return Frames.Single(frame =>
            frame.Direction == direction &&
            frame.Sequence == sequence);
    }
}

public sealed record ShapeDefinition
{
    public required string Id { get; init; }

    public required string Atlas { get; init; }

    public required int AtlasWidth { get; init; }

    public required int AtlasHeight { get; init; }

    public required string Mask { get; init; }

    public required int RenderScaleNumerator { get; init; }

    public required int RenderScaleDenominator { get; init; }

    public required ShapeFootprint Footprint { get; init; }

    public required int Height { get; init; }

    public required ShapeFlags Flags { get; init; }

    public required int SortBias { get; init; }

    public required IReadOnlyList<ShapeAnimation> Animations { get; init; }

    [JsonIgnore]
    public float RenderScale => (float)RenderScaleNumerator / RenderScaleDenominator;

    public ShapeAnimation GetAnimation(string name) =>
        Animations.Single(animation =>
            string.Equals(animation.Name, name, StringComparison.Ordinal));
}

public sealed record ShapePackDefinition
{
    public const int CurrentSchemaVersion = 1;

    public required int SchemaVersion { get; init; }

    public required string PackId { get; init; }

    public required ShapePackAttribution Attribution { get; init; }

    public required IReadOnlyList<ShapeDefinition> Shapes { get; init; }

    public ShapeDefinition GetShape(string id) =>
        Shapes.Single(shape =>
            string.Equals(shape.Id, id, StringComparison.Ordinal));
}

public sealed class ShapePackException : Exception
{
    public ShapePackException(string message)
        : base(message)
    {
    }

    public ShapePackException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class ShapePackLoader
{
    public const string ManifestFileName = "shape-pack.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false),
        },
    };

    public static ShapePackDefinition Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        ShapePackDefinition pack;
        try
        {
            pack = JsonSerializer.Deserialize<ShapePackDefinition>(json, JsonOptions)
                ?? throw new ShapePackException("Shape-pack JSON produced no document.");
        }
        catch (JsonException exception)
        {
            throw new ShapePackException(
                $"Shape-pack JSON is invalid at {exception.Path ?? "<root>"}: " +
                exception.Message,
                exception);
        }

        Validate(pack);
        return pack;
    }

    public static ShapePackDefinition LoadFromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var root = Path.GetFullPath(directory);
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new ShapePackException(
                $"Shape-pack manifest does not exist: {manifestPath}");
        }

        var pack = Parse(File.ReadAllText(manifestPath));
        foreach (var shape in pack.Shapes)
        {
            var atlasPath = ResolveAsset(root, shape.Atlas);
            if (!File.Exists(atlasPath))
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' atlas does not exist: {shape.Atlas}");
            }

            var maskPath = ResolveAsset(root, shape.Mask);
            if (!File.Exists(maskPath))
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' mask does not exist: {shape.Mask}");
            }

            var requiredMaskBytes = shape.Animations
                .SelectMany(animation => animation.Frames)
                .Select(frame => checked((long)frame.MaskOffset + frame.MaskByteLength))
                .DefaultIfEmpty(0)
                .Max();
            var actualMaskBytes = new FileInfo(maskPath).Length;
            if (actualMaskBytes != requiredMaskBytes)
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' mask is {actualMaskBytes} bytes; " +
                    $"metadata requires exactly {requiredMaskBytes}.");
            }
        }

        return pack;
    }

    /// <summary>
    /// Whether the frame, drawn with its origin at
    /// (<paramref name="originX"/>, <paramref name="originY"/>) in screen
    /// pixels, has an opaque pixel under (<paramref name="pixelX"/>,
    /// <paramref name="pixelY"/>).
    /// </summary>
    /// <remarks>
    /// This is the selection half of the same contract the renderer draws with:
    /// the destination rectangle starts at the origin minus the frame's origin,
    /// scaled by the shape's render scale. Picking therefore agrees with what
    /// the player sees, transparent pixels included, and stays integer-only.
    /// </remarks>
    public static bool CoversScreenPixel(
        ShapeDefinition shape,
        ShapeFrame frame,
        ReadOnlySpan<byte> maskBytes,
        int originX,
        int originY,
        int pixelX,
        int pixelY)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(frame);
        var numerator = shape.RenderScaleNumerator;
        var denominator = shape.RenderScaleDenominator;
        var left = originX - (frame.OriginX * numerator / denominator);
        var top = originY - (frame.OriginY * numerator / denominator);
        var localX = FloorDiv((pixelX - left) * denominator, numerator);
        var localY = FloorDiv((pixelY - top) * denominator, numerator);
        return IsOpaque(frame, maskBytes, localX, localY);
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0)
            ? quotient - 1
            : quotient;
    }

    public static bool IsOpaque(
        ShapeFrame frame,
        ReadOnlySpan<byte> maskBytes,
        int x,
        int y)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (x < 0 || x >= frame.Width || y < 0 || y >= frame.Height)
        {
            return false;
        }

        var pixel = checked((y * frame.Width) + x);
        var byteIndex = checked(frame.MaskOffset + (pixel / 8));
        if (byteIndex >= maskBytes.Length)
        {
            throw new ArgumentException(
                "Mask data is shorter than the frame metadata requires.",
                nameof(maskBytes));
        }

        var bit = 7 - (pixel % 8);
        return (maskBytes[byteIndex] & (1 << bit)) != 0;
    }

    private static void Validate(ShapePackDefinition pack)
    {
        if (pack.SchemaVersion != ShapePackDefinition.CurrentSchemaVersion)
        {
            throw new ShapePackException(
                $"Unsupported shape-pack schema {pack.SchemaVersion}; " +
                $"expected {ShapePackDefinition.CurrentSchemaVersion}.");
        }

        ValidateIdentifier(pack.PackId, "pack_id");
        ValidateText(pack.Attribution.Title, "attribution.title");
        ValidateText(pack.Attribution.Source, "attribution.source");
        ValidateText(pack.Attribution.License, "attribution.license");
        ValidateText(pack.Attribution.Revision, "attribution.revision");
        if (pack.Attribution.Authors.Count == 0 ||
            pack.Attribution.Authors.Any(string.IsNullOrWhiteSpace))
        {
            throw new ShapePackException(
                "attribution.authors must contain at least one non-empty author.");
        }

        if (pack.Shapes.Count == 0)
        {
            throw new ShapePackException("A shape pack must contain at least one shape.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shape in pack.Shapes)
        {
            ValidateIdentifier(shape.Id, "shape.id");
            if (!ids.Add(shape.Id))
            {
                throw new ShapePackException($"Duplicate shape id '{shape.Id}'.");
            }

            ValidateRelativeAssetPath(shape.Atlas, $"{shape.Id}.atlas");
            ValidateRelativeAssetPath(shape.Mask, $"{shape.Id}.mask");
            if (shape.AtlasWidth <= 0 || shape.AtlasHeight <= 0)
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' atlas dimensions must be positive.");
            }

            if (shape.RenderScaleNumerator <= 0 ||
                shape.RenderScaleDenominator <= 0)
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' render scale must be a positive rational.");
            }

            if (shape.Footprint.Width <= 0 || shape.Footprint.Depth <= 0)
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' footprint must be positive.");
            }

            if (shape.Height < 0)
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' height must not be negative.");
            }

            ValidateAnimations(shape);
        }
    }

    private static void ValidateAnimations(ShapeDefinition shape)
    {
        if (shape.Animations.Count == 0)
        {
            throw new ShapePackException(
                $"Shape '{shape.Id}' must define at least one animation.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var maskRanges = new List<(int Start, int End)>();
        foreach (var animation in shape.Animations)
        {
            ValidateIdentifier(
                animation.Name,
                $"{shape.Id}.animation.name");
            if (!names.Add(animation.Name))
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' has duplicate animation '{animation.Name}'.");
            }

            if (animation.Directions is < 1 or > 8)
            {
                throw new ShapePackException(
                    $"Animation '{shape.Id}/{animation.Name}' must have 1-8 directions.");
            }

            if (animation.FramesPerDirection <= 0)
            {
                throw new ShapePackException(
                    $"Animation '{shape.Id}/{animation.Name}' needs frames.");
            }

            var expectedFrames = checked(
                animation.Directions * animation.FramesPerDirection);
            if (animation.Frames.Count != expectedFrames)
            {
                throw new ShapePackException(
                    $"Animation '{shape.Id}/{animation.Name}' has " +
                    $"{animation.Frames.Count} frames; expected {expectedFrames}.");
            }

            var keys = new HashSet<(int Direction, int Sequence)>();
            foreach (var frame in animation.Frames)
            {
                if (frame.Direction < 0 || frame.Direction >= animation.Directions ||
                    frame.Sequence < 0 ||
                    frame.Sequence >= animation.FramesPerDirection)
                {
                    throw new ShapePackException(
                        $"Animation '{shape.Id}/{animation.Name}' has an out-of-range " +
                        $"frame key ({frame.Direction}, {frame.Sequence}).");
                }

                if (!keys.Add((frame.Direction, frame.Sequence)))
                {
                    throw new ShapePackException(
                        $"Animation '{shape.Id}/{animation.Name}' repeats frame key " +
                        $"({frame.Direction}, {frame.Sequence}).");
                }

                if (frame.X < 0 || frame.Y < 0 ||
                    frame.Width <= 0 || frame.Height <= 0 ||
                    frame.X + frame.Width > shape.AtlasWidth ||
                    frame.Y + frame.Height > shape.AtlasHeight)
                {
                    throw new ShapePackException(
                        $"Animation '{shape.Id}/{animation.Name}' has a frame outside " +
                        "the atlas.");
                }

                if (frame.DurationMs <= 0)
                {
                    throw new ShapePackException(
                        $"Animation '{shape.Id}/{animation.Name}' frame duration " +
                        "must be positive.");
                }

                if (frame.MaskOffset < 0)
                {
                    throw new ShapePackException(
                        $"Animation '{shape.Id}/{animation.Name}' mask offset " +
                        "must not be negative.");
                }

                var maskEnd = checked(frame.MaskOffset + frame.MaskByteLength);
                maskRanges.Add((frame.MaskOffset, maskEnd));
            }
        }

        maskRanges.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var index = 1; index < maskRanges.Count; index++)
        {
            if (maskRanges[index].Start != maskRanges[index - 1].End)
            {
                throw new ShapePackException(
                    $"Shape '{shape.Id}' mask ranges must be contiguous and " +
                    "non-overlapping.");
            }
        }

        if (maskRanges.Count > 0 && maskRanges[0].Start != 0)
        {
            throw new ShapePackException(
                $"Shape '{shape.Id}' first mask must start at byte zero.");
        }
    }

    private static void ValidateIdentifier(string value, string field)
    {
        ValidateText(value, field);
        if (value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
        {
            throw new ShapePackException(
                $"{field} '{value}' contains unsupported characters.");
        }
    }

    private static void ValidateText(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ShapePackException($"{field} must not be empty.");
        }
    }

    private static void ValidateRelativeAssetPath(string path, string field)
    {
        ValidateText(path, field);
        if (Path.IsPathRooted(path) ||
            path.Split('/', '\\').Any(segment =>
                segment is "" or "." or ".."))
        {
            throw new ShapePackException(
                $"{field} must be a normalized relative asset path.");
        }
    }

    private static string ResolveAsset(string root, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(root, normalized));
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new ShapePackException(
                $"Asset path escapes the shape-pack directory: {relativePath}");
        }

        return resolved;
    }
}
