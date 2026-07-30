using Ash.Content;
using Ash.Core;
using Ash.Sim;
using Godot;

namespace Ash.Game;

public partial class Main : Node2D
{
    private const float RenderScale = 4;
    private const int IsoOriginX = 100;
    private const int IsoOriginY = 27;
    private const int TileHalfWidth = 8;
    private const int TileHalfHeight = 4;
    private const int InventoryRowHeight = 12;
    private const int WorldUnitsPerTile = 256;
    private const int WorldViewportWidth = 240;
    private const int WorldViewportHeight = 200;
    private const int BackWallHeight = 22;
    private const int CameraPixelsPerPhysicsTick = 2;
    private const string ShapePackRoot =
        "res://assets/shape-packs/flare-starter";

    private static readonly Color Void = new("12110f");
    private static readonly Color Mortar = new("36322e");
    private static readonly Color StoneA = new("58534c");
    private static readonly Color StoneB = new("4b4843");
    private static readonly Color TimberA = new("685d4c");
    private static readonly Color TimberB = new("5b5244");
    private static readonly Color CarpetA = new("5d3938");
    private static readonly Color CarpetB = new("70413d");
    private static readonly Color Panel = new("17120f");
    private static readonly Color PanelInset = new("2a211a");
    private static readonly Color PanelEdge = new("a16d35");
    private static readonly Color Text = new("f0d7a1");
    private static readonly Color MutedText = new("c6a776");
    private static readonly Color Highlight = new("f0a83c");
    private static readonly Color TextShadow = new("100c09c0");
    private static readonly Color DebugFootprint = new("45d7d7d0");
    private static readonly Color DebugOrigin = new("ff5fa2");
    private static readonly Color DebugSortOrder = new("ffe36a");

    private PlayableSliceWorld _world = PlayableSliceWorld.CreateDemo();
    private readonly FollowCamera _camera = new(Vec2i.Zero);
    private ShapePackDefinition? _shapePack;
    private IReadOnlyDictionary<string, Texture2D> _shapeTextures =
        new Dictionary<string, Texture2D>();
    private double _animationElapsedMilliseconds;
    private int _playerDirection = 6;
    private bool _debugOverlayVisible;
    private SmokeRun? _smoke;

    private sealed record WorldDrawItem(SortItem SortItem, Action Draw);

    /// <summary>
    /// An automated run: play a scripted input sequence for a fixed number of
    /// frames, write a report of the authoritative world state, then quit.
    /// This exists because this Godot build does not terminate cleanly on
    /// Windows, so a caller cannot use the exit code as the verdict; it reads
    /// the report instead. See tools/run-game.ps1.
    /// </summary>
    private sealed class SmokeRun
    {
        public required int Frames { get; init; }

        public required string ReportPath { get; init; }

        public string? ScreenshotPath { get; init; }

        /// <summary>Where to walk before reporting, when the run names a cell.</summary>
        public GridPosition? Destination { get; init; }

        /// <summary>Exercise the save and load commands before reporting.</summary>
        public bool SaveLoad { get; init; }

        public string? SaveMessage { get; set; }

        public string? LoadMessage { get; set; }

        public int Frame { get; set; }

        public int WalkStepsTaken { get; set; }
    }

    public override void _Ready()
    {
        var userArguments = OS.GetCmdlineUserArgs();
        _debugOverlayVisible = userArguments.Contains(
            "--debug-overlay",
            StringComparer.Ordinal);
        _smoke = ParseSmokeRun(userArguments);
        if (userArguments.Contains("--backpack-open", StringComparer.Ordinal))
        {
            _world.ToggleBackpack();
        }

        if (userArguments.Contains("--equip-main-hand", StringComparer.Ordinal))
        {
            _world.ToggleMainHand();
        }

        LoadShapePack();
        SnapCameraToPlayer();
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _animationElapsedMilliseconds += delta * 1000;
        AdvanceSmokeRun();
        QueueRedraw();
    }

    private static SmokeRun? ParseSmokeRun(IReadOnlyList<string> arguments)
    {
        if (!arguments.Contains("--smoke-test", StringComparer.Ordinal))
        {
            return null;
        }

        return new SmokeRun
        {
            Frames = int.Parse(
                Argument(arguments, "--smoke-frames") ?? "90",
                System.Globalization.CultureInfo.InvariantCulture),
            ReportPath = Argument(arguments, "--smoke-report") ??
                throw new InvalidOperationException(
                    "--smoke-test requires --smoke-report=<absolute path>."),
            ScreenshotPath = Argument(arguments, "--screenshot"),
            Destination = ParseCell(Argument(arguments, "--smoke-goto")),
            SaveLoad = arguments.Contains(
                "--smoke-save-load",
                StringComparer.Ordinal),
        };
    }

    private static GridPosition? ParseCell(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var parts = value.Split(',');
        if (parts.Length != 2)
        {
            throw new ArgumentException(
                $"--smoke-goto expects 'x,y' but was '{value}'.");
        }

        return new GridPosition(
            int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string? Argument(
        IReadOnlyList<string> arguments,
        string name)
    {
        var prefix = $"{name}=";
        foreach (var argument in arguments)
        {
            if (argument.StartsWith(prefix, StringComparison.Ordinal))
            {
                return argument[prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>
    /// Drives the scripted run: a few real moves through the same input path a
    /// player uses, then one report and a quit.
    /// </summary>
    private void AdvanceSmokeRun()
    {
        if (_smoke is not { } smoke)
        {
            return;
        }

        smoke.Frame++;

        // Real moves through the same input path a player uses, so movement,
        // collision, support attachment and drawing all run several times
        // before the report is taken. Either walk to a named cell — how a
        // reviewer points the run at the feature they changed — or take a short
        // default walk.
        if (smoke.Frame % 2 == 0)
        {
            if (smoke.Destination is { } destination)
            {
                smoke.WalkStepsTaken += StepToward(destination) ? 1 : 0;
            }
            else if (smoke.Frame % 5 == 0 && smoke.WalkStepsTaken < 8)
            {
                smoke.WalkStepsTaken++;
                _ = smoke.WalkStepsTaken % 2 == 0
                    ? _world.MovePlayer(0, 1)
                    : _world.MovePlayer(1, 0);
            }
        }

        // Save, then load it back into the running app, through exactly the
        // commands F5 and F9 use.
        if (smoke.SaveLoad && smoke.Frame == Math.Max(1, smoke.Frames - 8))
        {
            smoke.SaveMessage = _world.RequestSave(SavePath).Message;
        }

        if (smoke.SaveLoad && smoke.Frame == Math.Max(2, smoke.Frames - 4))
        {
            LoadSavedWorld();
            smoke.LoadMessage = _world.LastMessage;
        }

        if (smoke.Frame < smoke.Frames)
        {
            return;
        }

        WriteSmokeReport(smoke);
        _smoke = null;
        GetTree().Quit();
    }

    /// <summary>
    /// One step of a walk toward <paramref name="destination"/>: the column
    /// first, then the row, which is the order that clears the demo's props.
    /// Returns whether a step was taken.
    /// </summary>
    private bool StepToward(GridPosition destination)
    {
        var here = _world.PlayerPosition;
        if (here.Y != destination.Y)
        {
            return _world.MovePlayer(0, here.Y < destination.Y ? 1 : -1)
                .Succeeded;
        }

        if (here.X != destination.X)
        {
            return _world.MovePlayer(here.X < destination.X ? 1 : -1, 0)
                .Succeeded;
        }

        return false;
    }

    private void WriteSmokeReport(SmokeRun smoke)
    {
        var player = _world.Player;
        var position = player.Location.Position;
        var drawItems = BuildWorldDrawItems();
        var sortResult = VolumeSorter.Sort(
            drawItems.Select(item => item.SortItem).ToArray());
        var falling = _world.Map.QueryAll()
            .Count(value => value.Motion == MotionState.Falling);
        var lines = new List<string>
        {
            $"godot={Engine.GetVersionInfo()["string"]}",
            $"headless={DisplayServer.GetName() == "headless"}",
            $"frames={smoke.Frame}",
            $"walk_steps={smoke.WalkStepsTaken}",
            $"physics_tick={_world.Physics.Tick}",
            $"player_grid={_world.PlayerPosition.X},{_world.PlayerPosition.Y}",
            $"player_world={position.X},{position.Y},{position.Z}",
            $"player_motion={player.Motion}",
            $"player_support={DescribeSupport(player.Support)}",
            $"map_revision={_world.Map.Revision}",
            $"map_objects={_world.Map.IndexedObjectCount}",
            $"draw_items={drawItems.Count}",
            $"sort_cycles={sortResult.Cycles.Count}",
            $"falling_objects={falling}",
            $"last_message={_world.LastMessage}",
            $"save_pending={_world.SaveGate.IsSavePending}",
        };

        if (smoke.SaveLoad)
        {
            lines.Add($"save={smoke.SaveMessage}");
            lines.Add($"load={smoke.LoadMessage}");
        }

        if (smoke.ScreenshotPath is { } screenshotPath)
        {
            var image = GetViewport().GetTexture()?.GetImage();
            if (image is null)
            {
                lines.Add(
                    "screenshot_error=the viewport produced no image; a " +
                    "headless run has no renderer to capture");
            }
            else
            {
                image.SavePng(screenshotPath);
                lines.Add($"screenshot={screenshotPath}");
            }
        }

        // The invariants are the point of the run: a violation throws here and
        // the report is never written, which is exactly the failure signal the
        // caller checks for.
        _world.Physics.ValidateInvariants();
        _world.Objects.ValidateInvariants();
        _world.Map.ValidateIndex();
        lines.Add("invariants=ok");
        System.IO.File.WriteAllLines(smoke.ReportPath, lines);
        GD.Print($"smoke report written to {smoke.ReportPath}");
    }

    public override void _PhysicsProcess(double _delta)
    {
        // The Godot physics step is the simulation's fixed tick: gravity,
        // support maintenance and landings all advance here, never in _Draw.
        _world.AdvancePhysics();
        _camera.StepToward(
            CameraTargetForPlayer(),
            CameraPixelsPerPhysicsTick);
        QueueRedraw();
    }

    public override void _Draw()
    {
        // Author in compact pixel-art units, then rasterize into the 1280x800
        // logical framebuffer at 4x. Windows displays that framebuffer at 2x.
        DrawSetTransform(
            Vector2.Zero,
            rotation: 0,
            scale: new Vector2(RenderScale, RenderScale));
        DrawWorld();
        DrawHud();

        if (_world.ActiveChest is not null)
        {
            DrawChestTransfer();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var handled = @event switch
        {
            InputEventKey key when key.Pressed && !key.Echo => HandleKey(key.Keycode),
            InputEventMouseButton mouse when
                mouse.Pressed && mouse.ButtonIndex == MouseButton.Left =>
                HandleClick(mouse.Position / RenderScale),
            _ => false,
        };

        if (!handled)
        {
            return;
        }

        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.W:
            case Key.Up:
                _playerDirection = 3;
                _world.MovePlayer(0, -1);
                return true;
            case Key.S:
            case Key.Down:
                _playerDirection = 7;
                _world.MovePlayer(0, 1);
                return true;
            case Key.A:
            case Key.Left:
                _playerDirection = 1;
                _world.MovePlayer(-1, 0);
                return true;
            case Key.D:
            case Key.Right:
                _playerDirection = 5;
                _world.MovePlayer(1, 0);
                return true;
            case Key.B:
            case Key.I:
                _world.ToggleBackpack();
                return true;
            case Key.Q:
                _world.ToggleMainHand();
                return true;
            case Key.G:
                _world.PickUpAtPlayerFeet();
                return true;
            case Key.X:
                _world.DropFromBackpack();
                return true;
            case Key.E:
                _world.ToggleNearestChest();
                return true;
            case Key.F:
            case Key.Space:
                _world.AttackAdjacentMonster();
                return true;
            case Key.Escape:
                _world.ClosePanels();
                return true;
            case Key.R:
                Adopt(PlayableSliceWorld.CreateDemo());
                return true;
            case Key.F5:
                _world.RequestSave(SavePath);
                return true;
            case Key.F9:
                LoadSavedWorld();
                return true;
            case Key.F3:
                _debugOverlayVisible = !_debugOverlayVisible;
                return true;
            case Key.T:
                _world.RemoveTrestleSupport();
                return true;
            default:
                return false;
        }
    }

    private static string SavePath =>
        ProjectSettings.GlobalizePath("user://object-world.ashw");

    /// <summary>
    /// Replaces the live world with <paramref name="world"/>. The old one is
    /// disposed so its map stops indexing a store nothing else uses.
    /// </summary>
    private void Adopt(PlayableSliceWorld world)
    {
        _world.Dispose();
        _world = world;
        _playerDirection = 6;
        SnapCameraToPlayer();
    }

    /// <summary>
    /// Loads the save into a world built beside this one and adopts it only if
    /// the whole load succeeded. A save that cannot be read leaves the running
    /// world exactly as it was and says so.
    /// </summary>
    private void LoadSavedWorld()
    {
        if (!File.Exists(SavePath))
        {
            _world.ReportFailure("There is no save to load.");
            return;
        }

        PlayableSliceWorld loaded;
        try
        {
            loaded = PlayableSliceWorld.Load(SavePath);
        }
        catch (Exception exception) when (
            exception is ObjectWorldSaveException or
                InvalidOperationException or
                ArgumentException or
                IOException)
        {
            _world.ReportFailure($"The save could not be loaded: {exception.Message}");
            return;
        }

        Adopt(loaded);
        _world.Report($"Loaded the world at tick {loaded.Physics.Tick}.");
    }

    private bool HandleClick(Vector2 mouse)
    {
        var chest = _world.ActiveChest;
        if (chest is not null)
        {
            const float listTop = 49;
            if (mouse.Y >= listTop)
            {
                var row = (int)((mouse.Y - listTop) / InventoryRowHeight);
                if (mouse.X is >= 28 and < 117)
                {
                    _world.PutInOpenChest(row);
                    return true;
                }

                if (mouse.X is >= 123 and < 218)
                {
                    _world.TakeFromOpenChest(row);
                    return true;
                }
            }

            return true;
        }

        if (_world.BackpackOpen && mouse.X >= 242)
        {
            if (mouse.Y is >= 129 and < 144)
            {
                _world.UnequipToBackpack(EquipmentSlot.MainHand);
                return true;
            }

            const float backpackListTop = 150;
            if (mouse.Y >= backpackListTop)
            {
                var row =
                    (int)((mouse.Y - backpackListTop) / InventoryRowHeight);
                _world.EquipFromBackpack(row);
                return true;
            }

            return true;
        }

        return false;
    }

    private void LoadShapePack()
    {
        try
        {
            var manifestPath =
                $"{ShapePackRoot}/{ShapePackLoader.ManifestFileName}";
            using var manifest = Godot.FileAccess.Open(
                manifestPath,
                Godot.FileAccess.ModeFlags.Read);
            if (manifest is null)
            {
                throw new ShapePackException(
                    $"Godot could not open {manifestPath}.");
            }

            var pack = ShapePackLoader.Parse(manifest.GetAsText());
            var textures = new Dictionary<string, Texture2D>(
                StringComparer.Ordinal);
            foreach (var shape in pack.Shapes)
            {
                var maskPath = $"{ShapePackRoot}/{shape.Mask}";
                using var mask = Godot.FileAccess.Open(
                    maskPath,
                    Godot.FileAccess.ModeFlags.Read);
                if (mask is null)
                {
                    throw new ShapePackException(
                        $"Godot could not open shape mask {maskPath}.");
                }

                var expectedMaskLength = shape.Animations
                    .SelectMany(animation => animation.Frames)
                    .Select(frame =>
                        checked((long)frame.MaskOffset + frame.MaskByteLength))
                    .DefaultIfEmpty(0)
                    .Max();
                if ((long)mask.GetLength() != expectedMaskLength)
                {
                    throw new ShapePackException(
                        $"Shape '{shape.Id}' mask is {mask.GetLength()} bytes; " +
                        $"metadata requires exactly {expectedMaskLength}.");
                }

                var atlasPath = $"{ShapePackRoot}/{shape.Atlas}";
                var texture = GD.Load<Texture2D>(atlasPath)
                    ?? throw new ShapePackException(
                        $"Godot could not load shape atlas {atlasPath}.");
                if (texture.GetWidth() != shape.AtlasWidth ||
                    texture.GetHeight() != shape.AtlasHeight)
                {
                    throw new ShapePackException(
                        $"Shape '{shape.Id}' declares a " +
                        $"{shape.AtlasWidth}x{shape.AtlasHeight} atlas, but Godot " +
                        $"loaded {texture.GetWidth()}x{texture.GetHeight()}.");
                }

                textures.Add(shape.Id, texture);
            }

            _shapePack = pack;
            _shapeTextures = textures;
            GD.Print(
                $"Loaded shape pack '{pack.PackId}' with " +
                $"{pack.Shapes.Count} shapes.");
        }
        catch (Exception exception)
        {
            _shapePack = null;
            _shapeTextures = new Dictionary<string, Texture2D>();
            GD.PushError(
                $"Shape Pack v1 failed to load; using procedural fallbacks. " +
                exception.Message);
        }
    }

    private bool DrawShape(
        string shapeId,
        string animationName,
        int direction,
        Vector2 at,
        int? fixedSequence = null)
    {
        var shape = _shapePack?.Shapes.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, shapeId, StringComparison.Ordinal));
        if (shape is null ||
            !_shapeTextures.TryGetValue(shapeId, out var texture))
        {
            return false;
        }

        var animation = shape.Animations.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Name,
                animationName,
                StringComparison.Ordinal));
        if (animation is null)
        {
            return false;
        }

        var safeDirection = Math.Clamp(direction, 0, animation.Directions - 1);
        var sequence = fixedSequence is null
            ? SelectAnimationSequence(animation)
            : Math.Clamp(
                fixedSequence.Value,
                0,
                animation.FramesPerDirection - 1);
        var frame = animation.GetFrame(safeDirection, sequence);
        var scale = shape.RenderScale;
        var nativeAt = at * RenderScale;
        var destination = new Rect2(
            nativeAt - (new Vector2(frame.OriginX, frame.OriginY) * scale),
            new Vector2(frame.Width, frame.Height) * scale);
        var source = new Rect2(
            frame.X,
            frame.Y,
            frame.Width,
            frame.Height);

        // Atlas pixels are already authored at the target logical resolution,
        // so draw them in native framebuffer coordinates rather than scaling a
        // tiny substitute through the 320x200 compact authoring transform.
        DrawSetTransform(Vector2.Zero, rotation: 0, scale: Vector2.One);
        DrawTextureRectRegion(
            texture,
            destination,
            source,
            modulate: Colors.White,
            transpose: false);
        RestoreCompactTransform();
        return true;
    }

    private int SelectAnimationSequence(ShapeAnimation animation)
    {
        if (animation.FramesPerDirection == 1)
        {
            return 0;
        }

        int[] timeline = animation.Playback switch
        {
            ShapePlayback.Loop =>
                Enumerable.Range(0, animation.FramesPerDirection).ToArray(),
            ShapePlayback.PingPong =>
            [
                .. Enumerable.Range(0, animation.FramesPerDirection),
                .. Enumerable.Range(1, animation.FramesPerDirection - 2)
                    .Reverse(),
            ],
            ShapePlayback.Once =>
                Enumerable.Range(0, animation.FramesPerDirection).ToArray(),
            _ => throw new ArgumentOutOfRangeException(),
        };

        var durations = timeline
            .Select(sequence => animation.GetFrame(0, sequence).DurationMs)
            .ToArray();
        var totalDuration = durations.Sum();
        var elapsed = animation.Playback == ShapePlayback.Once
            ? Math.Min(_animationElapsedMilliseconds, totalDuration - 1)
            : _animationElapsedMilliseconds % totalDuration;

        for (var index = 0; index < timeline.Length; index++)
        {
            if (elapsed < durations[index])
            {
                return timeline[index];
            }

            elapsed -= durations[index];
        }

        return timeline[^1];
    }

    private void RestoreCompactTransform() =>
        DrawSetTransform(
            Vector2.Zero,
            rotation: 0,
            scale: new Vector2(RenderScale, RenderScale));

    private void DrawWorld()
    {
        DrawRect(
            new Rect2(0, 0, WorldViewportWidth, WorldViewportHeight),
            Void);
        DrawOutsideRock();
        DrawBackWalls();

        for (var y = 0; y < PlayableSliceWorld.MapHeight; y++)
        {
            for (var x = 0; x < PlayableSliceWorld.MapWidth; x++)
            {
                DrawFloorTile(new GridPosition(x, y));
            }
        }

        // The visible set now goes through the same Pentagram-derived volume
        // sorter used by the headless engine. This makes the playable slice the
        // first real consumer of shape footprints, height, flags and sort bias.
        var drawItems = BuildWorldDrawItems();
        var drawById = drawItems.ToDictionary(item => item.SortItem.Id);
        var sortResult = VolumeSorter.Sort(
            drawItems.Select(item => item.SortItem).ToArray());
        foreach (var objectId in sortResult.DrawOrder)
        {
            drawById[objectId].Draw();
        }

        if (_debugOverlayVisible)
        {
            DrawDebugOverlay(drawById, sortResult);
        }
    }

    private void DrawDebugOverlay(
        IReadOnlyDictionary<int, WorldDrawItem> drawById,
        SortResult sortResult)
    {
        DrawText(
            new Vector2(4, 9),
            $"CAM {_camera.Offset.X},{_camera.Offset.Y}",
            size: 5,
            DebugSortOrder);
        DrawText(
            new Vector2(4, 16),
            $"MAP r{_world.Map.Revision} n{_world.Map.IndexedObjectCount}",
            size: 5,
            DebugSortOrder);
        DrawPhysicsReadout(_world.Player);

        for (var sortIndex = 0;
             sortIndex < sortResult.DrawOrder.Count;
             sortIndex++)
        {
            var objectId = sortResult.DrawOrder[sortIndex];
            var volume = drawById[objectId].SortItem.Volume;
            var footprint = new[]
            {
                WorldToCompact(volume.XMin, volume.YMin, volume.ZMin),
                WorldToCompact(volume.XMax, volume.YMin, volume.ZMin),
                WorldToCompact(volume.XMax, volume.YMax, volume.ZMin),
                WorldToCompact(volume.XMin, volume.YMax, volume.ZMin),
                WorldToCompact(volume.XMin, volume.YMin, volume.ZMin),
            };
            DrawPolyline(footprint, DebugFootprint, width: 0.5f);

            // Shape-frame origins are drawn at their runtime destination:
            // the projected volume anchor plus the slice's three-pixel foot
            // offset. Cross-hairs therefore expose a bad imported origin
            // independently of the sprite's visible alpha.
            var origin =
                WorldToCompact(volume.XMax, volume.YMax, volume.ZMin) +
                new Vector2(0, 3);
            DrawLine(
                origin + new Vector2(-2, 0),
                origin + new Vector2(2, 0),
                DebugOrigin,
                width: 0.75f);
            DrawLine(
                origin + new Vector2(0, -2),
                origin + new Vector2(0, 2),
                DebugOrigin,
                width: 0.75f);
            DrawText(
                origin + new Vector2(2, -2),
                $"{sortIndex}:{FormatSortIdentity(objectId)}",
                size: 5,
                DebugSortOrder);
        }

        if (sortResult.Cycles.Count > 0)
        {
            DrawText(
                new Vector2(4, 17),
                $"SORT CYCLES {sortResult.Cycles.Count}",
                size: 6,
                DebugOrigin);
        }
    }

    /// <summary>
    /// Collision volume, base and top elevation, motion state and support
    /// identity for the Avatar, plus the tick the physics system is on.
    /// </summary>
    private void DrawPhysicsReadout(WorldObject value)
    {
        var volume = WorldMap.VolumeFor(value);
        DrawText(
            new Vector2(4, 23),
            $"TICK {_world.Physics.Tick} " +
            $"VOL {volume.Horizontal.Width}x{volume.Horizontal.Depth} " +
            $"Z {volume.ZMin}..{volume.ZMax}",
            size: 5,
            DebugSortOrder);
        DrawText(
            new Vector2(4, 30),
            $"{value.Motion.ToString().ToUpperInvariant()} " +
            $"v{value.VerticalVelocity} ON {DescribeSupport(value.Support)}",
            size: 5,
            DebugSortOrder);
    }

    private string DescribeSupport(SupportRef support) =>
        support.Kind switch
        {
            SupportKind.Terrain =>
                $"terrain({support.CellX},{support.CellY})@{support.TopZ}",
            SupportKind.Object =>
                $"{Shorten(_world.Objects.Get(support.ObjectId).Name, 12)}" +
                $"@{support.TopZ}",
            _ => "nothing",
        };

    private void DrawPlayer()
    {
        var at = Iso(_world.PlayerPosition) +
            new Vector2(0, 3) +
            Elevation(_world.Player.Location.Position.Z);
        DrawShadow(at, 7);

        if (!DrawShape("avatar.knight", "stance", _playerDirection, at))
        {
            DrawPlayerFallback(at);
        }

        if (_world.Chests.Any(chest =>
                _world.PlayerPosition.ManhattanDistance(
                    _world.GetGridPosition(chest.Id)) <= 1) ||
            _world.Monsters.Any(monster =>
                monster.IsAlive &&
                _world.PlayerPosition.ManhattanDistance(
                    _world.GetGridPosition(monster.Id)) <= 1))
        {
            DrawPolyline(
                Diamond(at + new Vector2(0, -2), 9, 4, close: true),
                Highlight,
                1);
        }
    }

    private void DrawPlayerFallback(Vector2 at)
    {
        // Kept as a resilient fallback if an external pack is missing or invalid.
        DrawRect(
            new Rect2(at + new Vector2(-7, -14), new Vector2(6, 10)),
            new Color("694124"));
        DrawRect(
            new Rect2(at + new Vector2(-6, -13), new Vector2(4, 2)),
            new Color("b17a39"));
        DrawRect(
            new Rect2(at + new Vector2(-4, -6), new Vector2(3, 7)),
            new Color("26364b"));
        DrawRect(
            new Rect2(at + new Vector2(2, -6), new Vector2(3, 7)),
            new Color("1d293b"));
        DrawColoredPolygon(
        [
            at + new Vector2(-6, -15),
            at + new Vector2(1, -18),
            at + new Vector2(6, -13),
            at + new Vector2(5, -5),
            at + new Vector2(-4, -5),
        ],
            new Color("315d85"));
        DrawCircle(at + new Vector2(0, -20), 4, new Color("c6976d"));
    }

    private void DrawChest(WorldObject chest)
    {
        var at = Iso(_world.GetGridPosition(chest.Id)) +
            new Vector2(0, 3) +
            Elevation(chest.Location.Position.Z);
        var isCorpse = chest.HasFlag(ObjectFlags.Corpse);
        if (isCorpse)
        {
            DrawShadow(at, 8);
            DrawLine(
                at + new Vector2(-7, -2),
                at + new Vector2(6, -6),
                new Color("aca694"),
                3);
            DrawCircle(at + new Vector2(7, -7), 3, new Color("c3bca7"));
            DrawLine(
                at + new Vector2(-2, -4),
                at + new Vector2(-7, -9),
                new Color("8b877a"),
                2);
            return;
        }

        DrawShadow(at, 8);
        if (DrawShape(
                "container.chest",
                "state",
                direction: 0,
                at,
                fixedSequence: chest.IsContainerOpen ? 1 : 0))
        {
            return;
        }

        DrawColoredPolygon(
            [
                at + new Vector2(-8, -7),
                at + new Vector2(1, -3),
                at + new Vector2(8, -7),
                at + new Vector2(0, -11),
            ],
            new Color("9a6431"));
        DrawColoredPolygon(
            [
                at + new Vector2(-8, -7),
                at + new Vector2(1, -3),
                at + new Vector2(1, 3),
                at + new Vector2(-8, -1),
            ],
            new Color("69401f"));
        DrawColoredPolygon(
            [
                at + new Vector2(1, -3),
                at + new Vector2(8, -7),
                at + new Vector2(8, -1),
                at + new Vector2(1, 3),
            ],
            new Color("4a2d1b"));
        DrawLine(
            at + new Vector2(-7, -5),
            at + new Vector2(7, -5),
            new Color("d59a49"),
            1);
        DrawRect(
            new Rect2(at + new Vector2(-1, -5), new Vector2(3, 4)),
            new Color("e0b54e"));

        if (chest.IsContainerOpen)
        {
            DrawColoredPolygon(
                [
                    at + new Vector2(-8, -10),
                    at + new Vector2(0, -16),
                    at + new Vector2(8, -12),
                    at + new Vector2(0, -7),
                ],
                new Color("a97336"));
            DrawLine(
                at + new Vector2(-7, -10),
                at + new Vector2(7, -12),
                new Color("e0aa59"),
                1);
        }
    }

    private void DrawMonster(WorldObject monster)
    {
        var at = Iso(_world.GetGridPosition(monster.Id)) +
            new Vector2(0, 3) +
            Elevation(monster.Location.Position.Z);

        if (monster.ShapeId == "monster.many-eyed")
        {
            DrawManyEyedTyrant(at);
        }
        else
        {
            DrawShadow(at, 7);
            if (!DrawShape("monster.goblin", "stance", direction: 6, at))
            {
                DrawCaveRat(at);
            }
        }

        if (monster.Health < monster.MaxHealth)
        {
            var healthWidth = 14f * monster.Health / monster.MaxHealth;
            var healthOffset =
                monster.ShapeId == "monster.many-eyed" ? -27 : -21;
            DrawRect(
                new Rect2(at.X - 7, at.Y + healthOffset, 14, 2),
                new Color("351212"));
            DrawRect(
                new Rect2(at.X - 7, at.Y + healthOffset, healthWidth, 2),
                new Color("b93b30"));
        }
    }

    private void DrawOutsideRock()
    {
        for (var index = 0; index < 36; index++)
        {
            var x = (index * 47) % 238;
            var y = (index * 29) % 198;
            var width = 2 + ((index * 7) % 8);
            var colour = index % 3 == 0
                ? new Color("292621")
                : new Color("201e1b");
            DrawRect(new Rect2(x, y, width, 2), colour);
        }
    }

    private void DrawBackWalls()
    {
        for (var x = 0; x < PlayableSliceWorld.MapWidth; x++)
        {
            var at = Iso(new GridPosition(x, 0));
            var left = at + new Vector2(-TileHalfWidth, 0);
            var top = at + new Vector2(0, -TileHalfHeight);
            DrawColoredPolygon(
            [
                left,
                top,
                top + new Vector2(0, -BackWallHeight),
                left + new Vector2(0, -BackWallHeight),
            ],
                x % 2 == 0 ? new Color("68504a") : new Color("5e4944"));
            DrawWallCourses(left, top, BackWallHeight, x);
        }

        for (var y = 0; y < PlayableSliceWorld.MapHeight; y++)
        {
            var at = Iso(new GridPosition(0, y));
            var top = at + new Vector2(0, -TileHalfHeight);
            var right = at + new Vector2(TileHalfWidth, 0);
            DrawColoredPolygon(
            [
                top,
                right,
                right + new Vector2(0, -BackWallHeight),
                top + new Vector2(0, -BackWallHeight),
            ],
                y % 2 == 0 ? new Color("5d4c47") : new Color("51433f"));
            DrawWallCourses(top, right, BackWallHeight, y);
        }

        DrawWallPillar(Iso(new GridPosition(0, 0)) + new Vector2(0, -3));
        for (var x = 7; x < PlayableSliceWorld.MapWidth; x += 7)
        {
            DrawWallPillar(
                Iso(new GridPosition(x, 0)) + new Vector2(-5, -1));
        }

        for (var y = 6; y < PlayableSliceWorld.MapHeight; y += 6)
        {
            DrawWallPillar(
                Iso(new GridPosition(0, y)) + new Vector2(5, -1));
        }
    }

    private void DrawGroundItem(WorldObject item)
    {
        var at = Iso(_world.GetGridPosition(item.Id)) +
            new Vector2(0, 1) +
            Elevation(item.Location.Position.Z);
        if (DrawShape(
                item.ShapeId,
                "power",
                direction: 0,
                at,
                fixedSequence: 0))
        {
            return;
        }

        DrawShadow(at, 4);
        DrawColoredPolygon(
            Diamond(at + new Vector2(0, -2), 4, 2, close: false),
            new Color("b8873d"));
    }

    private void DrawWallCourses(
        Vector2 start,
        Vector2 end,
        int wallHeight,
        int offset)
    {
        for (var rise = 5; rise < wallHeight; rise += 6)
        {
            DrawLine(
                start + new Vector2(0, -rise),
                end + new Vector2(0, -rise),
                new Color("403733"),
                1);
        }

        var middle = start.Lerp(end, offset % 2 == 0 ? 0.35f : 0.65f);
        DrawLine(
            middle + new Vector2(0, -6),
            middle + new Vector2(0, -11),
            new Color("453934"),
            1);
    }

    private void DrawWallPillar(Vector2 at)
    {
        DrawRect(
            new Rect2(at + new Vector2(-3, -24), new Vector2(7, 24)),
            new Color("565251"));
        DrawRect(
            new Rect2(at + new Vector2(-1, -23), new Vector2(4, 22)),
            new Color("81776a"));
        DrawRect(
            new Rect2(at + new Vector2(-5, -25), new Vector2(11, 3)),
            new Color("9b8d78"));
        DrawRect(
            new Rect2(at + new Vector2(-5, -3), new Vector2(11, 3)),
            new Color("3b3938"));
    }

    private void DrawFloorTile(GridPosition position)
    {
        var terrain = _world.Map.GetTerrain(position.X, position.Y);
        var ground = Iso(position);
        var at = ground + Elevation(terrain.FloorZ);
        if (terrain.FloorZ != 0)
        {
            DrawFloorRiser(ground, at);
        }

        Color colour;
        if (IsCarpetTile(position))
        {
            colour = (position.X + position.Y) % 2 == 0
                ? CarpetA
                : CarpetB;
        }
        else if (position.X <= 13 &&
                 position.Y is >= 7 and <= 21)
        {
            colour = (position.X + position.Y) % 2 == 0
                ? TimberA
                : TimberB;
        }
        else
        {
            colour = (position.X + position.Y) % 2 == 0
                ? StoneA
                : StoneB;
        }

        var points = Diamond(at, TileHalfWidth, TileHalfHeight, close: false);
        DrawColoredPolygon(points, colour);
        DrawPolyline(
            Diamond(at, TileHalfWidth, TileHalfHeight, close: true),
            Mortar,
            1);

        var mark = ((position.X * 17) + (position.Y * 31)) % 7;
        if (mark == 0)
        {
            DrawLine(
                at + new Vector2(-3, 0),
                at + new Vector2(2, 1),
                colour.Lightened(0.12f),
                1);
        }

        if (IsCarpetBorder(position))
        {
            DrawLine(
                at + new Vector2(-5, 0),
                at + new Vector2(5, 0),
                new Color("d1903b"),
                1);
        }

        if (terrain.Flags.HasFlag(TerrainFlags.Solid))
        {
            DrawSolidTerrainBlock(at);
        }
    }

    /// <summary>
    /// The visible side of a cell whose floor is not at zero, so stairs, the
    /// raised platform and the pit read as elevation rather than as flat paint.
    /// </summary>
    private void DrawFloorRiser(Vector2 ground, Vector2 top)
    {
        var height = ground.Y - top.Y;
        var left = top + new Vector2(-TileHalfWidth, 0);
        var right = top + new Vector2(TileHalfWidth, 0);
        if (height < 0)
        {
            // A sunken cell. What is visible is the inside of the hole: the two
            // faces away from the camera, running from the surrounding floor
            // down to this cell. Without them the viewport background shows
            // through the gap the lowered tile leaves behind.
            var depth = -height;
            var back = top + new Vector2(0, -TileHalfHeight);
            DrawColoredPolygon(
            [
                left + new Vector2(0, -depth),
                back + new Vector2(0, -depth),
                back,
                left,
            ],
                new Color("332f2b"));
            DrawColoredPolygon(
            [
                back + new Vector2(0, -depth),
                right + new Vector2(0, -depth),
                right,
                back,
            ],
                new Color("2a2724"));
            return;
        }

        var bottom = top + new Vector2(0, TileHalfHeight);
        DrawColoredPolygon(
        [
            left,
            bottom,
            bottom + new Vector2(0, height),
            left + new Vector2(0, height),
        ],
            new Color("4a443d"));
        DrawColoredPolygon(
        [
            bottom,
            right,
            right + new Vector2(0, height),
            bottom + new Vector2(0, height),
        ],
            new Color("3e3934"));
    }

    /// <summary>
    /// Solid terrain is map state, not an object, so it is drawn with the floor
    /// pass. It exists so the collision solver has terrain the player can feel.
    /// </summary>
    private void DrawSolidTerrainBlock(Vector2 at)
    {
        const int BlockHeight = 12;
        var top = at + new Vector2(0, -BlockHeight);
        var left = at + new Vector2(-TileHalfWidth, 0);
        var right = at + new Vector2(TileHalfWidth, 0);
        var bottom = at + new Vector2(0, TileHalfHeight);
        DrawColoredPolygon(
        [
            left,
            bottom,
            bottom + new Vector2(0, -BlockHeight),
            left + new Vector2(0, -BlockHeight),
        ],
            new Color("463f39"));
        DrawColoredPolygon(
        [
            bottom,
            right,
            right + new Vector2(0, -BlockHeight),
            bottom + new Vector2(0, -BlockHeight),
        ],
            new Color("3a342f"));
        DrawColoredPolygon(
            Diamond(top, TileHalfWidth, TileHalfHeight, close: false),
            new Color("6d655b"));
        DrawPolyline(
            Diamond(top, TileHalfWidth, TileHalfHeight, close: true),
            Mortar,
            1);
    }

    private static bool IsCarpetTile(GridPosition position) =>
        (position.X is >= 17 and <= 23 &&
         position.Y is >= 5 and <= 10) ||
        (position.X is >= 27 and <= 36 &&
         position.Y is >= 18 and <= 24);

    private static bool IsCarpetBorder(GridPosition position) =>
        (position.X is >= 17 and <= 23 &&
         position.Y is >= 5 and <= 10 &&
         (position.X is 17 or 23 || position.Y is 5 or 10)) ||
        (position.X is >= 27 and <= 36 &&
         position.Y is >= 18 and <= 24 &&
         (position.X is 27 or 36 || position.Y is 18 or 24));

    private List<WorldDrawItem> BuildWorldDrawItems()
    {
        var visibleObjects = _world.VisibleObjects();
        var items = new List<WorldDrawItem>
        {
            CreateDrawItem(
                id: 10,
                new GridPosition(3, 2),
                footprintWidth: 128,
                footprintDepth: 128,
                height: 96,
                shapeNumber: 10,
                () => DrawBarrel(
                    Iso(new GridPosition(3, 2)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 11,
                new GridPosition(11, 2),
                footprintWidth: 256,
                footprintDepth: 192,
                height: 160,
                shapeNumber: 11,
                () => DrawAltar(
                    Iso(new GridPosition(11, 2)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 12,
                new GridPosition(1, 8),
                footprintWidth: 96,
                footprintDepth: 96,
                height: 160,
                shapeNumber: 12,
                () => DrawBrazier(
                    Iso(new GridPosition(1, 8)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 13,
                new GridPosition(15, 4),
                footprintWidth: 96,
                footprintDepth: 96,
                height: 160,
                shapeNumber: 12,
                () => DrawBrazier(
                    Iso(new GridPosition(15, 4)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 14,
                new GridPosition(12, 20),
                footprintWidth: 128,
                footprintDepth: 128,
                height: 96,
                shapeNumber: 10,
                () => DrawBarrel(
                    Iso(new GridPosition(12, 20)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 15,
                new GridPosition(29, 14),
                footprintWidth: 128,
                footprintDepth: 128,
                height: 96,
                shapeNumber: 10,
                () => DrawBarrel(
                    Iso(new GridPosition(29, 14)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 16,
                new GridPosition(33, 23),
                footprintWidth: 256,
                footprintDepth: 192,
                height: 160,
                shapeNumber: 11,
                () => DrawAltar(
                    Iso(new GridPosition(33, 23)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 17,
                new GridPosition(20, 25),
                footprintWidth: 96,
                footprintDepth: 96,
                height: 160,
                shapeNumber: 12,
                () => DrawBrazier(
                    Iso(new GridPosition(20, 25)) + new Vector2(0, 3))),
            CreateDrawItem(
                id: 18,
                new GridPosition(38, 17),
                footprintWidth: 96,
                footprintDepth: 96,
                height: 160,
                shapeNumber: 12,
                () => DrawBrazier(
                    Iso(new GridPosition(38, 17)) + new Vector2(0, 3))),
        };

        foreach (var chest in visibleObjects.Where(candidate =>
                     candidate.Id != _world.PlayerId &&
                     candidate.HasFlag(ObjectFlags.Container) &&
                     !candidate.HasFlag(ObjectFlags.Monster)))
        {
            items.Add(CreateObjectDrawItem(
                chest,
                shapeNumber: chest.HasFlag(ObjectFlags.Corpse) ? 20 : 21,
                () => DrawChest(chest)));
        }

        foreach (var monster in visibleObjects.Where(candidate =>
                     candidate.HasFlag(ObjectFlags.Monster) &&
                     candidate.IsAlive))
        {
            items.Add(CreateObjectDrawItem(
                monster,
                shapeNumber:
                    monster.ShapeId == "monster.many-eyed" ? 30 : 31,
                () => DrawMonster(monster)));
        }

        foreach (var item in visibleObjects.Where(candidate =>
                     candidate.HasFlag(ObjectFlags.Item)))
        {
            items.Add(CreateObjectDrawItem(
                item,
                shapeNumber: 40,
                () => DrawGroundItem(item)));
        }

        foreach (var prop in visibleObjects.Where(candidate =>
                     candidate.TypeId.StartsWith(
                         "prop.",
                         StringComparison.Ordinal)))
        {
            items.Add(CreateObjectDrawItem(
                prop,
                shapeNumber: 22,
                () => DrawProp(prop)));
        }

        items.Add(CreateObjectDrawItem(
            _world.Player,
            shapeNumber: 1,
            DrawPlayer));
        return items;
    }

    /// <summary>
    /// Physical props are drawn straight from their authoritative footprint and
    /// height, so what the collision solver uses is what the player sees.
    /// </summary>
    private void DrawProp(WorldObject prop)
    {
        var position = prop.Location.Position;
        var at = Iso(_world.GetGridPosition(prop.Id)) +
            new Vector2(0, 3) +
            Elevation(position.Z);
        var halfWidth = Math.Max(2, prop.Footprint.Width * TileHalfWidth / 256);
        var halfDepth = Math.Max(1, prop.Footprint.Depth * TileHalfHeight / 256);
        var height = Math.Max(1, prop.Height / 4);
        var top = at + new Vector2(0, -height);
        var left = top + new Vector2(-halfWidth, 0);
        var bottom = top + new Vector2(0, halfDepth);
        var right = top + new Vector2(halfWidth, 0);
        var side = prop.HasFlag(ObjectFlags.Solid)
            ? new Color("6b563a")
            : new Color("4f4738");
        DrawColoredPolygon(
        [
            left,
            bottom,
            bottom + new Vector2(0, height),
            left + new Vector2(0, height),
        ],
            side);
        DrawColoredPolygon(
        [
            bottom,
            right,
            right + new Vector2(0, height),
            bottom + new Vector2(0, height),
        ],
            side.Darkened(0.2f));
        DrawColoredPolygon(
            Diamond(top, halfWidth, halfDepth, close: false),
            side.Lightened(0.18f));
        DrawPolyline(
            Diamond(top, halfWidth, halfDepth, close: true),
            Mortar,
            1);
    }

    private WorldDrawItem CreateObjectDrawItem(
        WorldObject value,
        int shapeNumber,
        Action draw)
    {
        var shape = _shapePack?.Shapes.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Id,
                value.ShapeId,
                StringComparison.Ordinal));
        return CreateDrawItem(
            SortId(value.Id),
            _world.GetGridPosition(value.Id),
            value.Footprint.Width,
            value.Footprint.Depth,
            value.Height,
            shapeNumber,
            draw,
            shape?.SortBias ?? 0,
            shape is null
                ? SortItemFlags.None
                : ToSortFlags(shape.Flags),
            value.Location.Position.Z);
    }

    private static int SortId(ObjectId id) => unchecked((int)id.Value);

    private static string FormatSortIdentity(int sortId)
    {
        var packed = unchecked((uint)sortId);
        var generation = (byte)(packed >> 24);
        return generation == 0
            ? sortId.ToString()
            : $"{packed & 0x00FF_FFFF}g{generation}";
    }

    private static WorldDrawItem CreateDrawItem(
        int id,
        GridPosition position,
        int footprintWidth,
        int footprintDepth,
        int height,
        int shapeNumber,
        Action draw,
        int sortBias = 0,
        SortItemFlags flags = SortItemFlags.None,
        int baseZ = 0) =>
        new(
            new SortItem(
                id,
                SortVolumeAt(
                    position,
                    footprintWidth,
                    footprintDepth,
                    height,
                    baseZ),
                IsSprite: false,
                SortBias: sortBias,
                Flags: flags,
                ShapeNumber: shapeNumber),
            draw);

    private static SortVolume SortVolumeAt(
        GridPosition position,
        int footprintWidth,
        int footprintDepth,
        int height,
        int baseZ = 0)
    {
        var xMax = position.X * WorldUnitsPerTile;
        var yMax = position.Y * WorldUnitsPerTile;
        return new SortVolume(
            xMax - footprintWidth,
            xMax,
            yMax - footprintDepth,
            yMax,
            ZMin: baseZ,
            ZMax: baseZ + height);
    }

    /// <summary>
    /// Compact-pixel offset for an elevation. One vertical level is eight world
    /// units and four world units are one compact pixel, so a level lifts a
    /// sprite by two compact pixels.
    /// </summary>
    private static Vector2 Elevation(int z) => new(0, -(z / 4));

    private static SortItemFlags ToSortFlags(ShapeFlags flags)
    {
        var result = SortItemFlags.None;
        if (flags.HasFlag(ShapeFlags.Animated))
        {
            result |= SortItemFlags.Animated;
        }

        if (flags.HasFlag(ShapeFlags.Translucent))
        {
            result |= SortItemFlags.Translucent;
        }

        if (flags.HasFlag(ShapeFlags.Draw))
        {
            result |= SortItemFlags.Draw;
        }

        if (flags.HasFlag(ShapeFlags.Solid))
        {
            result |= SortItemFlags.Solid;
        }

        if (flags.HasFlag(ShapeFlags.Occludes))
        {
            result |= SortItemFlags.Occludes;
        }

        if (flags.HasFlag(ShapeFlags.LargeFlatSquare))
        {
            result |= SortItemFlags.LargeFlatSquare;
        }

        if (flags.HasFlag(ShapeFlags.Fixed))
        {
            result |= SortItemFlags.Fixed;
        }

        // ShapeFlags.Sprite describes an atlas-backed shape. SortItem.IsSprite
        // is Pentagram's Crusader-style always-on-top billboard rule and remains
        // deliberately false for Ultima VIII-style world actors.
        return result;
    }

    private void DrawBarrel(Vector2 at)
    {
        DrawShadow(at, 7);
        DrawRect(
            new Rect2(at + new Vector2(-6, -12), new Vector2(12, 11)),
            new Color("744922"));
        DrawCircle(at + new Vector2(0, -12), 6, new Color("9a6a34"));
        DrawLine(
            at + new Vector2(-6, -8),
            at + new Vector2(6, -8),
            new Color("33261d"),
            2);
        DrawLine(
            at + new Vector2(-5, -3),
            at + new Vector2(5, -3),
            new Color("33261d"),
            2);
    }

    private void DrawAltar(Vector2 at)
    {
        DrawShadow(at, 10);
        DrawColoredPolygon(
        [
            at + new Vector2(-11, -10),
            at + new Vector2(0, -5),
            at + new Vector2(11, -10),
            at + new Vector2(0, -15),
        ],
            new Color("78736d"));
        DrawRect(
            new Rect2(at + new Vector2(-7, -9), new Vector2(14, 9)),
            new Color("4d4b4b"));
        DrawLine(
            at + new Vector2(-6, -12),
            at + new Vector2(6, -8),
            new Color("7b2730"),
            2);
        DrawRect(
            new Rect2(at + new Vector2(-1, -20), new Vector2(2, 7)),
            new Color("e6d7a0"));
        DrawCircle(at + new Vector2(0, -22), 2, new Color("f09328"));
    }

    private void DrawBrazier(Vector2 at)
    {
        DrawShadow(at, 5);
        DrawLine(
            at + new Vector2(0, -1),
            at + new Vector2(0, -12),
            new Color("77716b"),
            3);
        DrawColoredPolygon(
        [
            at + new Vector2(-5, -14),
            at + new Vector2(5, -14),
            at + new Vector2(3, -10),
            at + new Vector2(-3, -10),
        ],
            new Color("5d5047"));
        DrawCircle(at + new Vector2(0, -16), 4, new Color("9d301d"));
        DrawCircle(at + new Vector2(0, -18), 3, new Color("ef7f24"));
        DrawCircle(at + new Vector2(1, -20), 1.5f, new Color("ffe169"));
    }

    private void DrawManyEyedTyrant(Vector2 at)
    {
        DrawShadow(at, 9);
        var shell = new Color("898679");
        var shellDark = new Color("57584f");
        var eye = new Color("b9a555");
        var pupil = new Color("211d19");

        DrawLine(at + new Vector2(-6, -14), at + new Vector2(-11, -23), shellDark, 2);
        DrawLine(at + new Vector2(-3, -17), at + new Vector2(-4, -28), shellDark, 2);
        DrawLine(at + new Vector2(3, -17), at + new Vector2(5, -28), shellDark, 2);
        DrawLine(at + new Vector2(6, -13), at + new Vector2(12, -21), shellDark, 2);
        DrawCircle(at + new Vector2(-11, -24), 2, eye);
        DrawCircle(at + new Vector2(-4, -29), 2, eye);
        DrawCircle(at + new Vector2(5, -29), 2, eye);
        DrawCircle(at + new Vector2(12, -22), 2, eye);

        DrawCircle(at + new Vector2(0, -13), 11, shellDark);
        DrawCircle(at + new Vector2(0, -15), 10, shell);
        DrawCircle(at + new Vector2(0, -16), 6, new Color("c2b66e"));
        DrawCircle(at + new Vector2(0, -16), 3, pupil);
        DrawRect(
            new Rect2(at + new Vector2(-6, -9), new Vector2(12, 2)),
            new Color("392521"));
        DrawLine(
            at + new Vector2(-5, -8),
            at + new Vector2(5, -8),
            new Color("d1c8ad"),
            1);

        // The eye stalks retain the strongest color in an otherwise muted
        // creature, keeping the threat readable against desaturated rooms.
        DrawRect(
            new Rect2(at + new Vector2(-1, -17), new Vector2(2, 2)),
            new Color("e4ca55"));
    }

    private void DrawCaveRat(Vector2 at)
    {
        DrawColoredPolygon(
        [
            at + new Vector2(-7, -4),
            at + new Vector2(-2, -9),
            at + new Vector2(7, -7),
            at + new Vector2(8, -2),
            at + new Vector2(-3, 0),
        ],
            new Color("783f38"));
        DrawCircle(at + new Vector2(7, -7), 3, new Color("995047"));
        DrawLine(
            at + new Vector2(-6, -4),
            at + new Vector2(-11, -7),
            new Color("a66e5d"),
            1);
        DrawRect(
            new Rect2(at + new Vector2(8, -8), new Vector2(1, 1)),
            new Color("f0b14b"));
    }

    private void DrawShadow(Vector2 at, float radius)
    {
        DrawColoredPolygon(
            Diamond(at, radius, Math.Max(2, radius / 3), close: false),
            new Color("120d0ba0"));
    }

    private void DrawHud()
    {
        DrawRect(new Rect2(240, 0, 80, 200), Panel);
        DrawRect(new Rect2(242, 2, 76, 196), PanelInset);
        DrawRect(
            new Rect2(242, 2, 76, 196),
            PanelEdge,
            filled: false,
            width: 1);

        DrawText(new Vector2(246, 13), "ASH", 12, Highlight);
        DrawText(
            new Vector2(246, 27),
            $"HP {_world.PlayerHealth}/{_world.PlayerMaxHealth}",
            8,
            Text);

        DrawText(new Vector2(246, 45), "MOVE WASD", 7, MutedText);
        DrawText(new Vector2(246, 55), "E OPEN", 7, MutedText);
        DrawText(new Vector2(246, 65), "F ATTACK", 7, MutedText);
        DrawText(new Vector2(246, 75), "B PACK", 7, MutedText);
        DrawText(new Vector2(246, 85), "Q HAND", 7, MutedText);
        DrawText(new Vector2(246, 95), "G GET  X DROP", 7, MutedText);
        DrawText(new Vector2(246, 105), "R RESET", 7, MutedText);
        DrawText(
            new Vector2(246, 115),
            "F5 SAVE  F9 LOAD",
            7,
            _world.SaveGate.IsSavePending ? Highlight : MutedText);
        DrawText(
            new Vector2(246, 125),
            _debugOverlayVisible ? "F3 DEBUG ON" : "F3 DEBUG",
            7,
            _debugOverlayVisible ? DebugSortOrder : MutedText);

        if (_world.BackpackOpen && _world.ActiveChest is null)
        {
            DrawText(new Vector2(246, 127), "BACKPACK", 8, Highlight);
            var mainHand = _world.EquippedIn(EquipmentSlot.MainHand);
            DrawText(
                new Vector2(246, 139),
                mainHand is null
                    ? "HAND (empty)"
                    : $"HAND {Shorten(mainHand.Value.Name, 9)}",
                7,
                mainHand is null ? MutedText : Text);
            DrawText(
                new Vector2(246, 148),
                "click item: equip",
                6,
                MutedText);
            DrawInventory(
                _world.BackpackItems,
                _world.BackpackCapacity,
                new Vector2(246, 150),
                width: 70,
                maxRows: 3);
        }

        if (!_world.BackpackOpen || _world.ActiveChest is not null)
        {
            DrawWrappedMessage(_world.LastMessage);
        }
    }

    private void DrawChestTransfer()
    {
        var chest = _world.ActiveChest!.Value;
        DrawRect(new Rect2(18, 18, 210, 145), new Color("17110de8"));
        DrawRect(new Rect2(21, 21, 204, 139), PanelInset);
        DrawRect(new Rect2(18, 18, 210, 145), PanelEdge, filled: false, width: 1);

        DrawText(new Vector2(28, 34), "BACKPACK", 9, Highlight);
        DrawText(new Vector2(123, 34), chest.Name.ToUpperInvariant(), 8, Highlight);
        DrawText(new Vector2(28, 44), "click to store", 7, MutedText);
        DrawText(new Vector2(123, 44), "click to take", 7, MutedText);

        DrawInventory(
            _world.BackpackItems,
            _world.BackpackCapacity,
            new Vector2(28, 49),
            89,
            maxRows: 9);
        DrawInventory(
            _world.ContentsOf(chest.Id),
            chest.ContainerCapacity,
            new Vector2(123, 49),
            95,
            maxRows: 9);

        DrawText(new Vector2(28, 157), "E CLOSE", 7, MutedText);
    }

    private void DrawInventory(
        IReadOnlyList<WorldObject> items,
        int capacity,
        Vector2 origin,
        float width,
        int maxRows)
    {
        var rows = Math.Min(items.Count, maxRows);
        for (var index = 0; index < rows; index++)
        {
            var y = origin.Y + (index * InventoryRowHeight);
            var item = items[index];
            var hasSwordIcon = item.ShapeId == "loot.shortsword" ||
                item.Name.Contains(
                "sword",
                StringComparison.OrdinalIgnoreCase);
            DrawRect(new Rect2(origin.X, y, width, 10), new Color("38291d"));
            DrawLine(
                new Vector2(origin.X, y + 10),
                new Vector2(origin.X + width, y + 10),
                new Color("56402a"),
                1);
            if (hasSwordIcon)
            {
                DrawShape(
                    "loot.shortsword",
                    "power",
                    direction: 0,
                    new Vector2(origin.X + 7, y + 9),
                    fixedSequence: 0);
            }

            DrawText(
                new Vector2(origin.X + (hasSwordIcon ? 15 : 3), y + 8),
                Shorten(item.Name, width < 80 ? 12 : 16),
                7,
                Text);
        }

        if (items.Count == 0)
        {
            DrawText(origin + new Vector2(3, 8), "(empty)", 7, MutedText);
        }

        DrawText(
            new Vector2(origin.X, origin.Y + (maxRows * InventoryRowHeight) + 8),
            $"{items.Count}/{capacity}",
            7,
            MutedText);
    }

    private void DrawWrappedMessage(string message)
    {
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            if ((current.Length + word.Length + 1) > 15)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = current.Length == 0 ? word : $"{current} {word}";
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        var visibleLines = lines.TakeLast(4).ToArray();
        for (var index = 0; index < visibleLines.Length; index++)
        {
            DrawText(
                new Vector2(246, 162 + (index * 9)),
                visibleLines[index],
                7,
                Text);
        }
    }

    private void DrawText(Vector2 at, string value, int size, Color colour)
    {
        // World geometry is authored at 320x200 and enlarged into the 1280x800
        // logical viewport. Rasterize glyphs directly at their final size so
        // the HUD does not enlarge tiny 7 px letters into blocky shapes.
        DrawSetTransform(Vector2.Zero, rotation: 0, scale: Vector2.One);
        var nativeAt = at * RenderScale;
        var nativeSize = Mathf.RoundToInt(size * RenderScale);
        DrawString(
            ThemeDB.FallbackFont,
            nativeAt + new Vector2(2, 2),
            value,
            HorizontalAlignment.Left,
            width: -1,
            fontSize: nativeSize,
            modulate: TextShadow);
        DrawString(
            ThemeDB.FallbackFont,
            nativeAt,
            value,
            HorizontalAlignment.Left,
            width: -1,
            fontSize: nativeSize,
            modulate: colour);
        DrawSetTransform(
            Vector2.Zero,
            rotation: 0,
            scale: new Vector2(RenderScale, RenderScale));
    }

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : $"{value[..(length - 1)]}…";

    private void SnapCameraToPlayer() =>
        _camera.SnapTo(CameraTargetForPlayer());

    private Vec2i CameraTargetForPlayer()
    {
        var playerOrigin =
            IsoWithoutCamera(_world.PlayerPosition) + new Vector2(0, 3);
        var desiredX = Mathf.RoundToInt(
            (WorldViewportWidth / 2f) - playerOrigin.X);
        var desiredY = Mathf.RoundToInt(
            (WorldViewportHeight / 2f) - playerOrigin.Y);

        var worldMinX =
            IsoOriginX -
            ((PlayableSliceWorld.MapHeight - 1) * TileHalfWidth) -
            TileHalfWidth;
        var worldMaxX =
            IsoOriginX +
            ((PlayableSliceWorld.MapWidth - 1) * TileHalfWidth) +
            TileHalfWidth;
        // Pillar caps extend six compact pixels above the wall courses.
        var worldMinY = IsoOriginY - BackWallHeight - 6;
        var worldMaxY =
            IsoOriginY +
            ((PlayableSliceWorld.MapWidth +
              PlayableSliceWorld.MapHeight -
              2) *
             TileHalfHeight) +
            TileHalfHeight;

        return new Vec2i(
            Math.Clamp(
                desiredX,
                WorldViewportWidth - worldMaxX,
                -worldMinX),
            Math.Clamp(
                desiredY,
                WorldViewportHeight - worldMaxY,
                -worldMinY));
    }

    private Vector2 CameraOffset =>
        new(_camera.Offset.X, _camera.Offset.Y);

    private Vector2 Iso(GridPosition position) =>
        IsoWithoutCamera(position) + CameraOffset;

    private static Vector2 IsoWithoutCamera(GridPosition position) =>
        new(
            IsoOriginX + ((position.X - position.Y) * TileHalfWidth),
            IsoOriginY + ((position.X + position.Y) * TileHalfHeight));

    private Vector2 WorldToCompact(int x, int y, int z) =>
        new Vector2(
            IsoOriginX +
            ((x - y) /
             (float)(ObliqueProjection.UnitsPerHorizontalPixel * RenderScale)),
            IsoOriginY +
            ((x + y) /
             (float)(
                 ObliqueProjection.UnitsPerHorizontalPixel *
                 RenderScale *
                 2)) -
            (z /
             (float)(ObliqueProjection.UnitsPerVerticalPixel * RenderScale))) +
        CameraOffset;

    private static Vector2[] Diamond(
        Vector2 centre,
        float halfWidth,
        float halfHeight,
        bool close)
    {
        var points = new List<Vector2>
        {
            centre + new Vector2(0, -halfHeight),
            centre + new Vector2(halfWidth, 0),
            centre + new Vector2(0, halfHeight),
            centre + new Vector2(-halfWidth, 0),
        };

        if (close)
        {
            points.Add(points[0]);
        }

        return [.. points];
    }
}
