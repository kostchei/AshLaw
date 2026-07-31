using Ash.Content;
using Ash.Core;
using Ash.Rules;
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
    private const int CarriedGridColumns = 5;
    private const int CarriedCellSize = 13;
    private const int CarriedCellStride = 14;
    private const int CarriedGridX = 245;
    private const int CarriedGridY = 51;

    /// <summary>
    /// The pack always shows at least two rows, and grows downward from there
    /// as capacity allows — up to five rows for the twenty-five slots a class
    /// bonus can reach.
    /// </summary>
    private const int CarriedGridMinimumRows = 2;

    /// <summary>
    /// How much of the default walk goes east before it turns south. The walk
    /// spends the round's whole move, and spending it 4-then-2 rather than
    /// alternating leaves the Avatar within arm's reach of the demo map's oak
    /// table — so <c>--smoke-drag-drop</c> has something it can actually pick
    /// up when the run finishes walking.
    /// </summary>
    private const int DefaultWalkEastwardSteps = 4;

    /// <summary>How many messages the side log remembers.</summary>
    private const int LogHistory = 12;
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

    /// <summary>
    /// The world a run generates when it is not told otherwise. A fixed seed,
    /// so launching the game twice gives the same eighteen subzones and a
    /// screenshot means the same thing tomorrow.
    /// </summary>
    private const ulong DefaultWorldSeed = 20260731;

    private PlayableSliceWorld _world = null!;
    private ulong _worldSeed = DefaultWorldSeed;

    /// <summary>
    /// Play the hand-built acceptance map instead of a generated world. It
    /// holds the physics area — the trestle, the stacked crates, the bridge
    /// over the pit — which no generated subzone carries.
    /// </summary>
    private bool _demoWorld;

    private readonly FollowCamera _camera = new(Vec2i.Zero);
    private readonly AlertVoices _voices = new();
    private ShapePackDefinition? _shapePack;
    private IReadOnlyDictionary<string, Texture2D> _shapeTextures =
        new Dictionary<string, Texture2D>();
    private double _animationElapsedMilliseconds;
    private int _playerDirection = 6;
    private bool _debugOverlayVisible;
    private bool _helpVisible;
    private readonly List<string> _log = [];
    private string _lastLogged = string.Empty;
    private SmokeRun? _smoke;
    private IReadOnlyDictionary<string, byte[]> _shapeMasks =
        new Dictionary<string, byte[]>();

    private readonly List<DrawnSprite> _drawnSprites = [];
    private int _pressedCarriedCell = -1;
    private int _drawingSortId;
    private Vector2 _cursor;

    private sealed record WorldDrawItem(SortItem SortItem, Action Draw);

    /// <summary>
    /// A sprite as it was actually drawn this frame, in native pixels. Picking
    /// reads this rather than recomputing placements, so what the player can
    /// click is exactly what the renderer put on screen.
    /// </summary>
    private readonly record struct DrawnSprite(
        int SortId,
        ShapeDefinition Shape,
        ShapeFrame Frame,
        int OriginX,
        int OriginY);

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

        /// <summary>Pick a world object up and drop it into the backpack.</summary>
        public bool DragDrop { get; init; }

        /// <summary>Keep the object in hand, so the report shows a live drag.</summary>
        public bool HoldDrag { get; init; }

        public string? DragMessage { get; set; }

        public string? DropMessage { get; set; }

        public string? SaveMessage { get; set; }

        /// <summary>
        /// Whether the save this run asked for actually reached the disk. A
        /// deferred save that never came back has written nothing, and the
        /// file at the save path is then some earlier run's.
        /// </summary>
        public bool SaveLanded { get; set; }

        public string? LoadMessage { get; set; }

        public int Frame { get; set; }

        public int WalkStepsTaken { get; set; }
    }

    public override void _Ready()
    {
        var userArguments = OS.GetCmdlineUserArgs();
        _demoWorld = userArguments.Contains("--demo", StringComparer.Ordinal);
        if (Argument(userArguments, "--world-seed") is { } seed)
        {
            _worldSeed = ulong.Parse(
                seed,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        _world = CreateConfiguredWorld();
        _debugOverlayVisible = userArguments.Contains(
            "--debug-overlay",
            StringComparer.Ordinal);
        _smoke = ParseSmokeRun(userArguments);
        _helpVisible = userArguments.Contains(
            "--help-open",
            StringComparer.Ordinal);
        if (userArguments.Contains("--backpack-open", StringComparer.Ordinal))
        {
            _world.ToggleBackpack();
        }

        if (userArguments.Contains("--equip-main-hand", StringComparer.Ordinal))
        {
            _world.ToggleRightHand();
        }

        LoadShapePack();
        AddChild(_voices);
        SnapCameraToPlayer();

        // Until the mouse moves there is no cursor to hang a held object on, so
        // start it at the middle of the world view rather than at the origin.
        _cursor = new Vector2(WorldViewportWidth / 2, WorldViewportHeight / 2);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _animationElapsedMilliseconds += delta * 1000;
        RecordMessage();
        AdvanceSmokeRun();
        QueueRedraw();
    }

    /// <summary>
    /// A new world of whichever kind this run asked for, so restarting gives
    /// back what was launched rather than always the demo.
    /// </summary>
    private PlayableSliceWorld CreateConfiguredWorld() =>
        _demoWorld
            ? PlayableSliceWorld.CreateDemo()
            : PlayableSliceWorld.CreateGenerated(_worldSeed);

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
            DragDrop = arguments.Contains(
                "--smoke-drag-drop",
                StringComparer.Ordinal),
            HoldDrag = arguments.Contains(
                "--smoke-hold",
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
            // The whole default walk falls inside one six-second round — ninety
            // frames is nowhere near the 360 physics ticks a round runs for —
            // so it asks for exactly what the round allows and no more. Asking
            // for more would not walk further; it would just spend the last
            // steps being refused.
            else if (smoke.Frame % 5 == 0 &&
                     smoke.WalkStepsTaken < MovementAllowance.StepsPerRound)
            {
                smoke.WalkStepsTaken++;
                _ = smoke.WalkStepsTaken <= DefaultWalkEastwardSteps
                    ? _world.MovePlayer(1, 0)
                    : _world.MovePlayer(0, 1);
            }
        }

        // One full drag gesture through the same service the mouse drives:
        // pick the nearest movable object up, then drop it into the backpack.
        if (smoke.DragDrop && smoke.Frame == Math.Max(1, smoke.Frames - 14))
        {
            var target = _world.VisibleObjects(radiusTiles: 4)
                .Where(value =>
                    value.HasFlag(ObjectFlags.Movable) &&
                    value.HasFlag(ObjectFlags.Item))
                .OrderBy(value =>
                    _world.PlayerPosition.ManhattanDistance(
                        _world.GetGridPosition(value.Id)))
                .ThenBy(value => value.Id)
                .Cast<WorldObject?>()
                .FirstOrDefault();
            smoke.DragMessage = target is null
                ? "no movable item in range"
                : _world.BeginDrag(target.Value.Id).Message;
        }

        if (smoke.DragDrop &&
            !smoke.HoldDrag &&
            smoke.Frame == Math.Max(2, smoke.Frames - 11))
        {
            smoke.DropMessage = _world.DropDragInBackpack().Message;
        }

        // Save, then load it back into the running app, through exactly the
        // commands F5 and F9 use.
        if (smoke.SaveLoad && smoke.Frame == Math.Max(1, smoke.Frames - 8))
        {
            smoke.SaveMessage = _world.RequestSave(SavePath).Message;
        }

        if (smoke.SaveLoad && smoke.Frame == Math.Max(2, smoke.Frames - 4))
        {
            // Only load what this run wrote. A save is deferred while anything
            // is still in transfer — which is exactly what --smoke-hold leaves
            // behind — and a deferral that never comes back has written
            // nothing. Loading anyway would adopt whatever file an earlier run
            // left at the save path, and the report would then describe, and
            // confirm the invariants of, a world this run never built.
            smoke.SaveLanded = !_world.SaveGate.IsSavePending;
            if (smoke.SaveLanded)
            {
                LoadSavedWorld();
                smoke.LoadMessage = _world.LastMessage;
            }
            else
            {
                smoke.LoadMessage =
                    "skipped: the save is still deferred, so this run has " +
                    "written nothing to load.";
                _world.Report(smoke.LoadMessage);
            }
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
        var falling = _world.CurrentMap.QueryAll()
            .Count(value => value.Motion == MotionState.Falling);
        var lines = new List<string>
        {
            $"godot={Engine.GetVersionInfo()["string"]}",
            $"headless={DisplayServer.GetName() == "headless"}",
            $"frames={smoke.Frame}",
            $"walk_steps={smoke.WalkStepsTaken}",
            $"physics_tick={_world.Physics.Tick}",
            $"map_id={_world.CurrentMapId}",
            $"map_size={_world.CurrentMap.Width}x{_world.CurrentMap.Depth}",
            $"player_grid={_world.PlayerPosition.X},{_world.PlayerPosition.Y}",
            $"player_world={position.X},{position.Y},{position.Z}",
            $"player_motion={player.Motion}",
            $"player_support={DescribeSupport(player.Support)}",
            $"map_revision={_world.CurrentMap.Revision}",
            $"map_objects={_world.CurrentMap.IndexedObjectCount}",
            $"draw_items={drawItems.Count}",
            $"sort_cycles={sortResult.Cycles.Count}",
            $"falling_objects={falling}",
            $"last_message={_world.LastMessage}",
            $"save_pending={_world.SaveGate.IsSavePending}",
            $"backpack_open={_world.BackpackOpen}",
            $"backpack_slots={_world.BackpackSlotsUsed}/{_world.BackpackCapacity}",
        };

        if (smoke.DragDrop)
        {
            lines.Add($"drag={smoke.DragMessage}");
            lines.Add($"drop={smoke.DropMessage}");
            lines.Add($"held={_world.HeldObject?.Name}");
            lines.Add($"backpack={_world.BackpackItems.Count}");
        }

        if (smoke.SaveLoad)
        {
            lines.Add($"save={smoke.SaveMessage}");
            lines.Add($"save_landed={smoke.SaveLanded}");
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
        _world.CurrentMap.ValidateIndex();
        lines.Add("invariants=ok");
        System.IO.File.WriteAllLines(smoke.ReportPath, lines);
        GD.Print($"smoke report written to {smoke.ReportPath}");
    }

    public override void _PhysicsProcess(double _delta)
    {
        // The Godot physics step is the simulation's fixed tick: gravity,
        // support maintenance and landings all advance here, never in _Draw.
        // Combat rides the same tick at a twelfth of the rate — one 200 ms beat
        // per twelve physics steps — so a fight paces identically whatever the
        // frame rate is doing.
        _world.AdvancePhysics();
        foreach (var combat in _world.LastCombatEvents)
        {
            if (combat.Kind == CombatEventKind.Alerted)
            {
                _voices.Play(combat.Voice);
            }
        }

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

        if (_helpVisible)
        {
            DrawHelp();
        }
    }

    /// <summary>
    /// The controls, on request. They do not belong on the panel: that space is
    /// the log.
    /// </summary>
    private void DrawHelp()
    {
        DrawRect(new Rect2(30, 22, 180, 156), new Color("17110df2"));
        DrawRect(new Rect2(33, 25, 174, 150), PanelInset);
        DrawRect(new Rect2(30, 22, 180, 156), PanelEdge, filled: false, width: 1);
        DrawText(new Vector2(40, 38), "CONTROLS", 10, Highlight);

        var lines = new[]
        {
            ("WASD / ARROWS", "walk"),
            ("MOUSE DRAG", "pick up and place"),
            ("RIGHT CLICK", "let go"),
            ("I / B", "carried gear"),
            ("E", "open, or take the way on"),
            ("F / SPACE", "attack (6s round)"),
            ("Q", "hand the top weapon"),
            ("G / X", "grab / drop"),
            ("T", "collapse the trestle"),
            ("F5 / F9", "save / load"),
            ("R", "restart"),
            ("F3", "debug overlay"),
            ("ESC", "close, or cancel a drag"),
        };
        for (var index = 0; index < lines.Length; index++)
        {
            var y = 54 + (index * 9);
            DrawText(new Vector2(40, y), lines[index].Item1, 7, Text);
            DrawText(new Vector2(110, y), lines[index].Item2, 7, MutedText);
        }

        DrawText(new Vector2(40, 172), "H CLOSES THIS", 7, MutedText);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var handled = @event switch
        {
            InputEventKey key when key.Pressed && !key.Echo => HandleKey(key.Keycode),
            InputEventMouseMotion motion => TrackCursor(motion.Position),
            InputEventMouseButton mouse when
                mouse.ButtonIndex == MouseButton.Left =>
                HandleLeftButton(mouse.Position, mouse.Pressed),
            InputEventMouseButton mouse when
                mouse.Pressed && mouse.ButtonIndex == MouseButton.Right =>
                HandleRightButton(),
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
                _world.ToggleRightHand();
                return true;
            case Key.G:
                _world.PickUpAtPlayerFeet();
                return true;
            case Key.X:
                _world.DropFromBackpack();
                return true;
            case Key.E:
                _world.Interact();
                return true;
            case Key.F:
            case Key.Space:
                _world.AttackAdjacentMonster();
                return true;
            case Key.Escape:
                if (_world.Drag.IsDragging)
                {
                    _world.CancelDrag();
                    return true;
                }

                if (_helpVisible)
                {
                    _helpVisible = false;
                    return true;
                }

                _world.ClosePanels();
                return true;
            case Key.R:
                Adopt(CreateConfiguredWorld());
                return true;
            case Key.F5:
                _world.RequestSave(SavePath);
                return true;
            case Key.F9:
                LoadSavedWorld();
                return true;
            case Key.H:
                _helpVisible = !_helpVisible;
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

    private bool TrackCursor(Vector2 nativePosition)
    {
        _cursor = nativePosition / RenderScale;
        return _world.Drag.IsDragging;
    }

    /// <summary>
    /// Press picks an object up, release drops it wherever the cursor is. The
    /// panel clicks keep working unchanged when nothing is in hand.
    /// </summary>
    private bool HandleLeftButton(Vector2 nativePosition, bool pressed)
    {
        _cursor = nativePosition / RenderScale;
        if (_world.Drag.IsDragging)
        {
            return pressed || DropHeldObject(_cursor);
        }

        if (!pressed)
        {
            // A press and release on the same gear slot is a click, not a
            // drag: it wears the thing rather than moving it.
            var released = CarriedCellAt(_cursor);
            var wasPressed = _pressedCarriedCell;
            _pressedCarriedCell = -1;
            if (released >= 0 && released == wasPressed)
            {
                var cells = BuildCarriedCells();
                if (released < cells.Count)
                {
                    _world.EquipFromBackpack(cells[released].ObjectId);
                    return true;
                }
            }

            return false;
        }

        var cell = CarriedCellAt(_cursor);
        if (cell >= 0)
        {
            _pressedCarriedCell = cell;
            var cells = BuildCarriedCells();
            if (cell < cells.Count)
            {
                _world.BeginDrag(cells[cell].ObjectId);
            }

            return true;
        }

        return HandleClick(_cursor) || BeginDragAt(nativePosition);
    }

    private bool HandleRightButton()
    {
        if (!_world.Drag.IsDragging)
        {
            return false;
        }

        _world.CancelDrag();
        return true;
    }

    /// <summary>
    /// Picks the topmost object whose sprite covers the cursor. Objects the
    /// slice still draws procedurally have no mask, so they are picked by the
    /// cell they stand on instead; both paths go through the same drag service.
    /// </summary>
    private bool BeginDragAt(Vector2 nativePosition)
    {
        if (nativePosition.X / RenderScale >= WorldViewportWidth)
        {
            return false;
        }

        var byId = _world.VisibleObjects()
            .ToDictionary(value => SortId(value.Id));
        for (var index = _drawnSprites.Count - 1; index >= 0; index--)
        {
            var sprite = _drawnSprites[index];
            if (!byId.TryGetValue(sprite.SortId, out var value) ||
                !_shapeMasks.TryGetValue(sprite.Shape.Id, out var mask))
            {
                continue;
            }

            if (ShapePackLoader.CoversScreenPixel(
                    sprite.Shape,
                    sprite.Frame,
                    mask,
                    sprite.OriginX,
                    sprite.OriginY,
                    Mathf.RoundToInt(nativePosition.X),
                    Mathf.RoundToInt(nativePosition.Y)) &&
                value.HasFlag(ObjectFlags.Movable))
            {
                return _world.BeginDrag(value.Id).Succeeded;
            }
        }

        var cell = CellAt(_cursor);
        var onCell = _world.VisibleObjects()
            .Where(value =>
                value.HasFlag(ObjectFlags.Movable) &&
                _world.GetGridPosition(value.Id) == cell)
            .OrderByDescending(value => value.Location.Position.Z)
            .ThenByDescending(value => value.Id)
            .ToArray();
        return onCell.Length > 0 && _world.BeginDrag(onCell[0].Id).Succeeded;
    }

    private bool DropHeldObject(Vector2 mouse)
    {
        _pressedCarriedCell = -1;
        if (_world.ActiveChest is not null && mouse.X < WorldViewportWidth)
        {
            _world.DropDragInOpenChest();
            return true;
        }

        if (mouse.X >= 242)
        {
            // Anywhere on the pack panel stows it; the hand line equips it.
            _ = mouse.Y is >= 37 and < 47
                ? _world.DropDragOnRightHand()
                : _world.DropDragInBackpack();
            return true;
        }

        _world.DropDragOnMap(CellAt(mouse));
        return true;
    }

    /// <summary>
    /// The ground-plane cell under a compact-pixel position: the exact inverse
    /// of <see cref="IsoWithoutCamera"/>.
    /// </summary>
    private GridPosition CellAt(Vector2 compact)
    {
        var x = compact.X - CameraOffset.X - IsoOriginX;
        var y = compact.Y - CameraOffset.Y - IsoOriginY;
        var sum = y / TileHalfHeight;
        var difference = x / TileHalfWidth;
        return new GridPosition(
            Mathf.FloorToInt((sum + difference) / 2),
            Mathf.FloorToInt((sum - difference) / 2));
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
                _world.UnequipToBackpack(EquipmentSlot.RightHand);
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
            var masks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
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
                masks.Add(shape.Id, mask.GetBuffer((long)mask.GetLength()));
            }

            _shapePack = pack;
            _shapeTextures = textures;
            _shapeMasks = masks;
            GD.Print(
                $"Loaded shape pack '{pack.PackId}' with " +
                $"{pack.Shapes.Count} shapes.");
        }
        catch (Exception exception)
        {
            _shapePack = null;
            _shapeTextures = new Dictionary<string, Texture2D>();
            _shapeMasks = new Dictionary<string, byte[]>();
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

        // Record the placement so selection can hit-test the same pixels.
        _drawnSprites.Add(
            new DrawnSprite(
                _drawingSortId,
                shape,
                frame,
                Mathf.RoundToInt(nativeAt.X),
                Mathf.RoundToInt(nativeAt.Y)));
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

        // The map being played decides how much floor there is, not a
        // compiled-in size: a generated subzone is 65 cells across and between
        // 21 and 29 deep, and the demo map is neither.
        var map = _world.CurrentMap;
        for (var y = 0; y < map.Depth; y++)
        {
            for (var x = 0; x < map.Width; x++)
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
        _drawnSprites.Clear();
        foreach (var objectId in sortResult.DrawOrder)
        {
            _drawingSortId = objectId;
            drawById[objectId].Draw();
        }

        _drawingSortId = 0;
        DrawHeldObject();

        if (_debugOverlayVisible)
        {
            DrawDebugOverlay(drawById, sortResult);
        }
    }

    /// <summary>
    /// The object on the cursor during a drag. It is genuinely out of the world
    /// while held — <c>InTransfer</c> in the store — so it is drawn here rather
    /// than in the sorted pass.
    /// </summary>
    private void DrawHeldObject()
    {
        if (_world.HeldObject is not { } held)
        {
            return;
        }

        var at = _cursor + new Vector2(0, -2);
        if (!DrawShape(held.ShapeId, "power", direction: 0, at, fixedSequence: 0))
        {
            DrawColoredPolygon(
                Diamond(at, 4, 2, close: false),
                new Color("e0b45a"));
        }

        DrawText(
            _cursor + new Vector2(6, 4),
            Shorten(held.Name, 14),
            6,
            Highlight);
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
            $"MAP r{_world.CurrentMap.Revision} n{_world.CurrentMap.IndexedObjectCount}",
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
        var map = _world.CurrentMap;
        for (var x = 0; x < map.Width; x++)
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

        for (var y = 0; y < map.Depth; y++)
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
        for (var x = 7; x < map.Width; x += 7)
        {
            DrawWallPillar(
                Iso(new GridPosition(x, 0)) + new Vector2(-5, -1));
        }

        for (var y = 6; y < map.Depth; y += 6)
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
        var terrain = _world.CurrentMap.GetTerrain(position.X, position.Y);
        var ground = Iso(position);
        var at = ground + Elevation(terrain.FloorZ);
        if (terrain.FloorZ != 0)
        {
            DrawFloorRiser(ground, at);
        }

        // Carpet and floorboards are the demo map's dressing, laid out against
        // its cells. A generated subzone is stone throughout until the theme
        // tag drives a palette of its own.
        var dressed = _world.CurrentMapId == PlayableSliceWorld.DemoMapId;
        Color colour;
        if (dressed && IsCarpetTile(position))
        {
            colour = (position.X + position.Y) % 2 == 0
                ? CarpetA
                : CarpetB;
        }
        else if (dressed &&
                 position.X <= 13 &&
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

        if (dressed && IsCarpetBorder(position))
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

    /// <summary>
    /// The demo map's hand-placed dressing: barrels, altars and braziers at
    /// cells chosen for that map and no other.
    /// </summary>
    private IReadOnlyList<WorldDrawItem> DemoScenery() =>
    [
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
    ];

    private List<WorldDrawItem> BuildWorldDrawItems()
    {
        var visibleObjects = _world.VisibleObjects();
        var items = new List<WorldDrawItem>();

        // What has already been given a drawing. The last pass below draws
        // whatever is left, so a generated subzone can never put something in
        // the world that the player has no way of seeing.
        var drawn = new HashSet<ObjectId> { _world.PlayerId };

        // Scenery placed by hand at demo-map cells. A generated subzone is
        // carved out of rock and dresses itself, so hanging a brazier on cell
        // (1, 8) of one would put it inside a wall.
        if (_world.CurrentMapId == PlayableSliceWorld.DemoMapId)
        {
            items.AddRange(DemoScenery());
        }

        foreach (var chest in visibleObjects.Where(candidate =>
                     candidate.Id != _world.PlayerId &&
                     candidate.HasFlag(ObjectFlags.Container) &&
                     !candidate.HasFlag(ObjectFlags.Monster)))
        {
            drawn.Add(chest.Id);
            items.Add(CreateObjectDrawItem(
                chest,
                shapeNumber: chest.HasFlag(ObjectFlags.Corpse) ? 20 : 21,
                () => DrawChest(chest)));
        }

        foreach (var monster in visibleObjects.Where(candidate =>
                     candidate.HasFlag(ObjectFlags.Monster) &&
                     candidate.IsAlive))
        {
            drawn.Add(monster.Id);
            items.Add(CreateObjectDrawItem(
                monster,
                shapeNumber:
                    monster.ShapeId == "monster.many-eyed" ? 30 : 31,
                () => DrawMonster(monster)));
        }

        foreach (var item in visibleObjects.Where(candidate =>
                     candidate.HasFlag(ObjectFlags.Item)))
        {
            drawn.Add(item.Id);
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
            drawn.Add(prop.Id);
            items.Add(CreateObjectDrawItem(
                prop,
                shapeNumber: 22,
                () => DrawProp(prop)));
        }

        foreach (var waymark in visibleObjects.Where(IsWaymark))
        {
            drawn.Add(waymark.Id);
            items.Add(CreateObjectDrawItem(
                waymark,
                shapeNumber: 5,
                () => DrawWaymark(waymark)));
        }

        // Everything else that is visible and has no drawing of its own — the
        // cracked floor hinting at a hazard, say. A hint the player cannot see
        // is a trap, and the generator promises a warning rather than one.
        foreach (var marking in visibleObjects.Where(candidate =>
                     !drawn.Contains(candidate.Id)))
        {
            items.Add(CreateObjectDrawItem(
                marking,
                shapeNumber: 6,
                () => DrawMarking(marking)));
        }

        items.Add(CreateObjectDrawItem(
            _world.Player,
            shapeNumber: 1,
            DrawPlayer));
        return items;
    }

    /// <summary>
    /// A flat marking on the floor: something painted on the cell rather than
    /// standing on it. Sized from its own footprint, so what is drawn is what
    /// the object says it covers.
    /// </summary>
    private void DrawMarking(WorldObject marking)
    {
        var at = Iso(_world.GetGridPosition(marking.Id)) +
            new Vector2(0, 3) +
            Elevation(marking.Location.Position.Z);
        var halfWidth = Math.Max(
            2,
            marking.Footprint.Width * TileHalfWidth / WorldUnitsPerTile);
        var halfDepth = Math.Max(
            1,
            marking.Footprint.Depth * TileHalfHeight / WorldUnitsPerTile);
        DrawColoredPolygon(
            Diamond(at, halfWidth - 1, halfDepth - 1, close: false),
            new Color("2e2823"));
        DrawPolyline(
            Diamond(at, halfWidth - 1, halfDepth - 1, close: true),
            new Color("8a5a3a"),
            1);
        DrawLine(
            at + new Vector2(-halfWidth / 2f, -1),
            at + new Vector2(halfWidth / 2f, 1),
            new Color("6d4a33"),
            1);
    }

    private static bool IsWaymark(WorldObject value) =>
        value.TypeId == SubzoneBuilder.EntranceTypeId ||
        value.TypeId == SubzoneBuilder.ExitTypeId;

    /// <summary>
    /// A threshold: the way on out of a subzone, or the way back into the last
    /// one. It lies flat on the cell it marks, because it is something to be
    /// stood on rather than walked round.
    /// </summary>
    private void DrawWaymark(WorldObject waymark)
    {
        var at = Iso(_world.GetGridPosition(waymark.Id)) +
            new Vector2(0, 3) +
            Elevation(waymark.Location.Position.Z);
        var onward = waymark.TypeId == SubzoneBuilder.ExitTypeId;
        var colour = onward ? new Color("d8b25a") : new Color("6f8fa8");

        DrawColoredPolygon(
            Diamond(at, TileHalfWidth - 1, TileHalfHeight - 1, close: false),
            new Color("1c1a17"));
        DrawPolyline(
            Diamond(at, TileHalfWidth - 1, TileHalfHeight - 1, close: true),
            colour,
            1);

        // An arrow along the axis of travel: out of the subzone, or back into
        // the one behind.
        var reach = onward ? 4 : -4;
        DrawLine(
            at + new Vector2(-reach / 2f, 0),
            at + new Vector2(reach, 0),
            colour,
            1);
        DrawLine(
            at + new Vector2(reach, 0),
            at + new Vector2(reach / 2f, -2),
            colour,
            1);
        DrawLine(
            at + new Vector2(reach, 0),
            at + new Vector2(reach / 2f, 2),
            colour,
            1);
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
        var rightHand = _world.EquippedIn(EquipmentSlot.RightHand);
        DrawText(
            new Vector2(246, 39),
            rightHand is null
                ? "HAND (empty)"
                : $"HAND {Shorten(rightHand.Value.Name, 9)}",
            7,
            rightHand is null ? MutedText : Text);
        DrawSwingReadiness();

        if (_world.BackpackOpen && _world.ActiveChest is null)
        {
            DrawCarriedGrid();
        }
        else
        {
            DrawText(
                new Vector2(246, 53),
                _world.SaveGate.IsSavePending ? "SAVING…" : "H HELP",
                7,
                _world.SaveGate.IsSavePending ? Highlight : MutedText);

        }

        // Whatever the pack does not use is the log.
        var logTop = _world.BackpackOpen && _world.ActiveChest is null
            ? CarriedGridY + (CarriedGridRows() * CarriedCellStride) + 8
            : 66;
        DrawMessageLog(logTop);
    }

    /// <summary>
    /// How much of the six-second round the player's weapon has left to run.
    /// The bar fills as the swing comes back round, so a player deciding
    /// whether to close or back off can see the answer rather than count.
    /// </summary>
    private void DrawSwingReadiness()
    {
        const int BarX = 246;
        const int BarY = 44;
        const int BarWidth = 68;
        const int BarHeight = 3;

        var remaining = _world.Combat.PlayerCooldownRemainingMilliseconds;
        var round = AttackSpeed.RoundMilliseconds;
        var filled = round == 0
            ? BarWidth
            : (BarWidth * (round - Math.Min(remaining, round))) / round;

        DrawRect(new Rect2(BarX, BarY, BarWidth, BarHeight), PanelInset);
        if (filled > 0)
        {
            DrawRect(
                new Rect2(BarX, BarY, filled, BarHeight),
                remaining == 0 ? Highlight : MutedText);
        }

        DrawRect(
            new Rect2(BarX, BarY, BarWidth, BarHeight),
            PanelEdge,
            filled: false,
            width: 1);

        // The round's other allowance. Movement and attacks are separate
        // budgets, so they are drawn as separate things.
        DrawStepAllowance(BarX, BarY + BarHeight + 2);
    }

    /// <summary>
    /// The tiles left in the round's move, one pip each, so a player can see
    /// whether they can still close the distance before committing to it.
    /// </summary>
    private void DrawStepAllowance(int x, int y)
    {
        const int PipWidth = 3;
        const int PipStride = 5;
        const int PipHeight = 3;

        var remaining = _world.Combat.PlayerStepsRemainingInRound;
        for (var pip = 0; pip < MovementAllowance.StepsPerRound; pip++)
        {
            DrawRect(
                new Rect2(x + (pip * PipStride), y, PipWidth, PipHeight),
                pip < remaining ? Highlight : PanelInset);
        }
    }

    /// <summary>
    /// The carried inventory as gear slots: one cell per slot, five to a row,
    /// hanging from the top right and growing downward as capacity allows. A
    /// two-slot item fills two cells, because that is what it costs.
    /// </summary>
    private void DrawCarriedGrid()
    {
        var capacity = _world.BackpackCapacity;
        var used = _world.BackpackSlotsUsed;
        DrawText(
            new Vector2(CarriedGridX, CarriedGridY - 4),
            $"PACK {used}/{capacity}",
            8,
            used >= capacity ? Highlight : Highlight);

        var cells = BuildCarriedCells();
        var rows = CarriedGridRows();
        for (var index = 0; index < rows * CarriedGridColumns; index++)
        {
            var at = CarriedCellOrigin(index);
            var beyondCapacity = index >= capacity;
            DrawRect(
                new Rect2(at, new Vector2(CarriedCellSize, CarriedCellSize)),
                beyondCapacity ? new Color("1a1512") : new Color("2b211a"));
            DrawRect(
                new Rect2(at, new Vector2(CarriedCellSize, CarriedCellSize)),
                beyondCapacity ? new Color("2a2420") : new Color("56402a"),
                filled: false,
                width: 1);
            if (index >= cells.Count)
            {
                continue;
            }

            DrawCarriedCell(at, cells[index], index == _pressedCarriedCell);
        }

    }

    /// <summary>
    /// Two rows at least, and one more for every five slots of capacity, so
    /// the pack grows downward rather than needing a scrollbar.
    /// </summary>
    private int CarriedGridRows() =>
        Math.Max(
            CarriedGridMinimumRows,
            (Math.Max(_world.BackpackCapacity, _world.BackpackSlotsUsed) +
                CarriedGridColumns - 1) / CarriedGridColumns);

    private void DrawCarriedCell(
        Vector2 at,
        CarriedCell cell,
        bool pressed)
    {
        if (pressed)
        {
            DrawRect(
                new Rect2(at, new Vector2(CarriedCellSize, CarriedCellSize)),
                Highlight,
                filled: false,
                width: 1);
        }

        // A continuation cell shows the item carries on rather than repeating
        // its icon: two cells, one thing.
        if (cell.Continuation)
        {
            DrawLine(
                at + new Vector2(2, CarriedCellSize / 2),
                at + new Vector2(CarriedCellSize - 2, CarriedCellSize / 2),
                MutedText,
                1);
            return;
        }

        // Atlas frames are authored for the world, not for a thirteen-pixel
        // cell, so a slot draws a glyph coloured by what kind of thing it is.
        var centre = at + new Vector2(CarriedCellSize / 2f, 6);
        var colour = cell.ShapeId switch
        {
            "loot.shortsword" => new Color("c9c2b4"),
            "container.chest" => new Color("8a6a3a"),
            _ => new Color("b8873d"),
        };
        if (cell.ShapeId == "loot.shortsword")
        {
            DrawLine(
                centre + new Vector2(-3, 3),
                centre + new Vector2(3, -3),
                colour,
                1);
            DrawLine(
                centre + new Vector2(-1, -1),
                centre + new Vector2(2, 2),
                colour.Darkened(0.3f),
                1);
        }
        else
        {
            DrawColoredPolygon(Diamond(centre, 4, 3, close: false), colour);
        }

        if (cell.Quantity > 1)
        {
            DrawText(
                at + new Vector2(1, CarriedCellSize - 1),
                cell.Quantity > 999 ? "999+" : cell.Quantity.ToString(),
                6,
                Text);
        }
    }

    /// <summary>
    /// One cell per gear slot, in the order the slot layout reports them, so
    /// the panel never has to know what a slot costs.
    /// </summary>
    private List<CarriedCell> BuildCarriedCells()
    {
        var cells = new List<CarriedCell>();
        foreach (var entry in _world.BackpackSlots)
        {
            var shapeId = _world.Objects.TryGet(entry.ObjectId, out var value)
                ? value.ShapeId
                : "loot.generic";
            for (var cell = 0; cell < entry.Cells; cell++)
            {
                cells.Add(
                    new CarriedCell(
                        entry.ObjectId,
                        entry.Label,
                        shapeId,
                        cell == 0 ? entry.Quantity : 0,
                        cell > 0));
            }
        }

        return cells;
    }

    private static Vector2 CarriedCellOrigin(int index) =>
        new(
            CarriedGridX + ((index % CarriedGridColumns) * CarriedCellStride),
            CarriedGridY + ((index / CarriedGridColumns) * CarriedCellStride));

    /// <summary>The carried cell under a compact-pixel position, or -1.</summary>
    private int CarriedCellAt(Vector2 compact)
    {
        if (!_world.BackpackOpen || _world.ActiveChest is not null)
        {
            return -1;
        }

        var column = (int)((compact.X - CarriedGridX) / CarriedCellStride);
        var row = (int)((compact.Y - CarriedGridY) / CarriedCellStride);
        if (compact.X < CarriedGridX ||
            compact.Y < CarriedGridY ||
            column < 0 ||
            column >= CarriedGridColumns ||
            row < 0)
        {
            return -1;
        }

        var index = (row * CarriedGridColumns) + column;
        return index < Math.Max(
            _world.BackpackCapacity,
            CarriedGridMinimumRows * CarriedGridColumns)
            ? index
            : -1;
    }

    private sealed record CarriedCell(
        ObjectId ObjectId,
        string Label,
        string ShapeId,
        int Quantity,
        bool Continuation);

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
            chest.CarryCapacity,
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

            // A stack is one object carrying a count, so the row shows the
            // count rather than repeating the row N times.
            var label = item.Quantity > 1
                ? $"{Shorten(item.Name, width < 80 ? 9 : 13)} x{item.Quantity}"
                : Shorten(item.Name, width < 80 ? 12 : 16);
            DrawText(
                new Vector2(origin.X + (hasSwordIcon ? 15 : 3), y + 8),
                label,
                7,
                Text);
        }

        if (items.Count == 0)
        {
            DrawText(origin + new Vector2(3, 8), "(empty)", 7, MutedText);
        }

        // Rows are objects; the number that matters is gear slots, where a
        // hundred coins are one slot and plate is two.
        var used = GearSlots.UsedBy(items);
        var overflow = items.Count - rows;
        DrawText(
            new Vector2(origin.X, origin.Y + (maxRows * InventoryRowHeight) + 8),
            overflow > 0
                ? $"{used}/{capacity} slots  +{overflow} more"
                : $"{used}/{capacity} slots",
            7,
            used > capacity ? Highlight : MutedText);
    }

    /// <summary>
    /// The world reports one message at a time; the panel keeps a scrolling
    /// log of them, so what just happened does not vanish on the next step.
    /// </summary>
    private void RecordMessage()
    {
        var message = _world.LastMessage;
        if (message.Length == 0 ||
            string.Equals(message, _lastLogged, StringComparison.Ordinal))
        {
            return;
        }

        _lastLogged = message;
        _log.Add(message);
        if (_log.Count > LogHistory)
        {
            _log.RemoveRange(0, _log.Count - LogHistory);
        }
    }

    /// <summary>
    /// The log, newest at the bottom, filling whatever panel space is left
    /// under the pack.
    /// </summary>
    private void DrawMessageLog(float top)
    {
        var lines = new List<string>();
        foreach (var message in _log)
        {
            lines.AddRange(WrapMessage(message));
        }

        var rows = Math.Max(0, (int)((196 - top) / 9));
        var visible = lines.TakeLast(rows).ToArray();
        for (var index = 0; index < visible.Length; index++)
        {
            // The newest line reads brightest; older ones recede.
            var age = visible.Length - 1 - index;
            DrawText(
                new Vector2(246, top + (index * 9)),
                visible[index],
                7,
                age == 0 ? Text : MutedText);
        }
    }

    private static IReadOnlyList<string> WrapMessage(string message)
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

        return lines;
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

        // The camera is held inside the map actually being played, so walking
        // into another subzone reframes on that subzone's extents rather than
        // on the ones the first map happened to have.
        var map = _world.CurrentMap;
        var worldMinX =
            IsoOriginX -
            ((map.Depth - 1) * TileHalfWidth) -
            TileHalfWidth;
        var worldMaxX =
            IsoOriginX +
            ((map.Width - 1) * TileHalfWidth) +
            TileHalfWidth;
        // Pillar caps extend six compact pixels above the wall courses.
        var worldMinY = IsoOriginY - BackWallHeight - 6;
        var worldMaxY =
            IsoOriginY +
            ((map.Width + map.Depth - 2) * TileHalfHeight) +
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
