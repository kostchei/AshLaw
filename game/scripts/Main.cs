using Ash.Sim;
using Godot;

namespace Ash.Game;

public partial class Main : Node2D
{
    private const int IsoOriginX = 100;
    private const int IsoOriginY = 27;
    private const int TileHalfWidth = 8;
    private const int TileHalfHeight = 4;
    private const int InventoryRowHeight = 12;

    private static readonly Color Void = new("100d0b");
    private static readonly Color Mortar = new("332a24");
    private static readonly Color StoneA = new("55483b");
    private static readonly Color StoneB = new("4a4037");
    private static readonly Color TimberA = new("71512f");
    private static readonly Color TimberB = new("634529");
    private static readonly Color CarpetA = new("6d1819");
    private static readonly Color CarpetB = new("861f1c");
    private static readonly Color Panel = new("17120f");
    private static readonly Color PanelInset = new("2a211a");
    private static readonly Color PanelEdge = new("a16d35");
    private static readonly Color Text = new("e6c78c");
    private static readonly Color MutedText = new("a68159");
    private static readonly Color Highlight = new("f0a83c");

    private PlayableSliceWorld _world = PlayableSliceWorld.CreateDemo();

    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
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
                HandleClick(mouse.Position),
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
                _world.MovePlayer(0, -1);
                return true;
            case Key.S:
            case Key.Down:
                _world.MovePlayer(0, 1);
                return true;
            case Key.A:
            case Key.Left:
                _world.MovePlayer(-1, 0);
                return true;
            case Key.D:
            case Key.Right:
                _world.MovePlayer(1, 0);
                return true;
            case Key.B:
            case Key.I:
                _world.ToggleBackpack();
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
                _world = PlayableSliceWorld.CreateDemo();
                return true;
            default:
                return false;
        }
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
            return true;
        }

        return false;
    }

    private void DrawWorld()
    {
        DrawRect(new Rect2(0, 0, 240, 200), Void);
        DrawOutsideRock();
        DrawBackWalls();

        for (var y = 0; y < PlayableSliceWorld.MapHeight; y++)
        {
            for (var x = 0; x < PlayableSliceWorld.MapWidth; x++)
            {
                DrawFloorTile(new GridPosition(x, y));
            }
        }

        // Ultima VIII's look depends on a painter's algorithm. Everything sharing
        // the floor is emitted by diagonal depth so feet, furniture and bodies
        // overlap naturally as the player moves through the room.
        var maximumDepth = PlayableSliceWorld.MapWidth + PlayableSliceWorld.MapHeight;
        for (var depth = 0; depth < maximumDepth; depth++)
        {
            DrawDecorations(depth);

            foreach (var chest in _world.Chests.Where(chest =>
                         Depth(chest.Position) == depth))
            {
                DrawChest(chest);
            }

            foreach (var monster in _world.Monsters.Where(monster =>
                         Depth(monster.Position) == depth))
            {
                DrawMonster(monster);
            }

            if (Depth(_world.PlayerPosition) == depth)
            {
                DrawPlayer();
            }
        }
    }

    private void DrawPlayer()
    {
        var at = Iso(_world.PlayerPosition) + new Vector2(0, 3);
        DrawShadow(at, 7);

        // The backpack is always visible on the character's back.
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
        DrawLine(
            at + new Vector2(2, -15),
            at + new Vector2(5, -6),
            new Color("79a2bc"),
            1);
        DrawCircle(at + new Vector2(0, -20), 4, new Color("c6976d"));
        DrawRect(
            new Rect2(at + new Vector2(-4, -23), new Vector2(8, 3)),
            new Color("3b2a20"));
        DrawLine(
            at + new Vector2(6, -13),
            at + new Vector2(8, -5),
            new Color("b7a58b"),
            2);

        if (_world.Chests.Any(chest =>
                _world.PlayerPosition.ManhattanDistance(chest.Position) <= 1) ||
            _world.Monsters.Any(monster =>
                monster.IsAlive &&
                _world.PlayerPosition.ManhattanDistance(monster.Position) <= 1))
        {
            DrawPolyline(
                Diamond(at + new Vector2(0, -2), 9, 4, close: true),
                Highlight,
                1);
        }
    }

    private void DrawChest(ChestState chest)
    {
        var at = Iso(chest.Position) + new Vector2(0, 3);
        var isCorpse = chest.Id.StartsWith("remains-", StringComparison.Ordinal);
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

        if (chest.IsOpen)
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

    private void DrawMonster(MonsterState monster)
    {
        var at = Iso(monster.Position) + new Vector2(0, 3);
        if (!monster.IsAlive)
        {
            DrawShadow(at, 7);
            DrawLine(
                at + new Vector2(-7, -2),
                at + new Vector2(7, -5),
                new Color("803838"),
                3);
            return;
        }

        if (monster.Id == "skeleton")
        {
            DrawSkeleton(at);
        }
        else
        {
            DrawCaveRat(at);
        }

        if (monster.Health < monster.MaxHealth)
        {
            var healthWidth = 14f * monster.Health / monster.MaxHealth;
            DrawRect(
                new Rect2(at.X - 7, at.Y - 27, 14, 2),
                new Color("351212"));
            DrawRect(
                new Rect2(at.X - 7, at.Y - 27, healthWidth, 2),
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
                ? new Color("2d2119")
                : new Color("211915");
            DrawRect(new Rect2(x, y, width, 2), colour);
        }
    }

    private void DrawBackWalls()
    {
        const int wallHeight = 22;

        for (var x = 0; x < PlayableSliceWorld.MapWidth; x++)
        {
            var at = Iso(new GridPosition(x, 0));
            var left = at + new Vector2(-TileHalfWidth, 0);
            var top = at + new Vector2(0, -TileHalfHeight);
            DrawColoredPolygon(
            [
                left,
                top,
                top + new Vector2(0, -wallHeight),
                left + new Vector2(0, -wallHeight),
            ],
                x % 2 == 0 ? new Color("77362a") : new Color("693027"));
            DrawWallCourses(left, top, wallHeight, x);
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
                right + new Vector2(0, -wallHeight),
                top + new Vector2(0, -wallHeight),
            ],
                y % 2 == 0 ? new Color("62352b") : new Color("583027"));
            DrawWallCourses(top, right, wallHeight, y);
        }

        DrawWallPillar(Iso(new GridPosition(0, 0)) + new Vector2(0, -3));
        DrawWallPillar(Iso(new GridPosition(7, 0)) + new Vector2(-5, -1));
        DrawWallPillar(Iso(new GridPosition(14, 0)) + new Vector2(-5, -1));
        DrawWallPillar(Iso(new GridPosition(0, 6)) + new Vector2(5, -1));
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
                new Color("3e2520"),
                1);
        }

        var middle = start.Lerp(end, offset % 2 == 0 ? 0.35f : 0.65f);
        DrawLine(
            middle + new Vector2(0, -6),
            middle + new Vector2(0, -11),
            new Color("44251f"),
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
        var at = Iso(position);
        Color colour;
        if (position.X is >= 9 and <= 15 &&
            position.Y is >= 7 and <= 10)
        {
            colour = (position.X + position.Y) % 2 == 0
                ? CarpetA
                : CarpetB;
        }
        else if (position.X <= 7 && position.Y <= 6)
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

        if (position.X is >= 9 and <= 15 &&
            position.Y is >= 7 and <= 10 &&
            (position.X is 9 or 15 || position.Y is 7 or 10))
        {
            DrawLine(
                at + new Vector2(-5, 0),
                at + new Vector2(5, 0),
                new Color("d1903b"),
                1);
        }
    }

    private void DrawDecorations(int depth)
    {
        if (depth == Depth(new GridPosition(3, 2)))
        {
            DrawBarrel(Iso(new GridPosition(3, 2)) + new Vector2(0, 3));
        }

        if (depth == Depth(new GridPosition(11, 2)))
        {
            DrawAltar(Iso(new GridPosition(11, 2)) + new Vector2(0, 3));
        }

        if (depth == Depth(new GridPosition(1, 8)))
        {
            DrawBrazier(Iso(new GridPosition(1, 8)) + new Vector2(0, 3));
        }

        if (depth == Depth(new GridPosition(15, 4)))
        {
            DrawBrazier(Iso(new GridPosition(15, 4)) + new Vector2(0, 3));
        }
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

    private void DrawSkeleton(Vector2 at)
    {
        DrawShadow(at, 7);
        var bone = new Color("c9c1a5");
        DrawLine(
            at + new Vector2(0, -5),
            at + new Vector2(0, -17),
            bone,
            3);
        DrawLine(
            at + new Vector2(-6, -13),
            at + new Vector2(6, -10),
            bone,
            2);
        DrawLine(
            at + new Vector2(0, -6),
            at + new Vector2(-5, 0),
            bone,
            2);
        DrawLine(
            at + new Vector2(0, -6),
            at + new Vector2(5, 0),
            bone,
            2);
        DrawCircle(at + new Vector2(0, -21), 5, bone);
        DrawRect(
            new Rect2(at + new Vector2(-3, -22), new Vector2(2, 2)),
            new Color("201b18"));
        DrawRect(
            new Rect2(at + new Vector2(2, -22), new Vector2(2, 2)),
            new Color("201b18"));
        DrawLine(
            at + new Vector2(6, -11),
            at + new Vector2(8, -22),
            new Color("8b8b85"),
            1);
    }

    private void DrawCaveRat(Vector2 at)
    {
        DrawShadow(at, 7);
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
        DrawText(new Vector2(246, 85), "R RESET", 7, MutedText);

        if (_world.BackpackOpen && _world.ActiveChest is null)
        {
            DrawText(new Vector2(246, 105), "BACKPACK", 8, Highlight);
            DrawInventory(
                _world.Backpack,
                new Vector2(246, 117),
                width: 70,
                maxRows: 6);
        }

        DrawWrappedMessage(_world.LastMessage);
    }

    private void DrawChestTransfer()
    {
        var chest = _world.ActiveChest!;
        DrawRect(new Rect2(18, 18, 210, 145), new Color("17110de8"));
        DrawRect(new Rect2(21, 21, 204, 139), PanelInset);
        DrawRect(new Rect2(18, 18, 210, 145), PanelEdge, filled: false, width: 1);

        DrawText(new Vector2(28, 34), "BACKPACK", 9, Highlight);
        DrawText(new Vector2(123, 34), chest.Name.ToUpperInvariant(), 8, Highlight);
        DrawText(new Vector2(28, 44), "click to store", 7, MutedText);
        DrawText(new Vector2(123, 44), "click to take", 7, MutedText);

        DrawInventory(_world.Backpack, new Vector2(28, 49), 89, maxRows: 9);
        DrawInventory(chest.Inventory, new Vector2(123, 49), 95, maxRows: 9);

        DrawText(new Vector2(28, 157), "E CLOSE", 7, MutedText);
    }

    private void DrawInventory(
        Inventory inventory,
        Vector2 origin,
        float width,
        int maxRows)
    {
        var rows = Math.Min(inventory.Items.Count, maxRows);
        for (var index = 0; index < rows; index++)
        {
            var y = origin.Y + (index * InventoryRowHeight);
            DrawRect(new Rect2(origin.X, y, width, 10), new Color("38291d"));
            DrawLine(
                new Vector2(origin.X, y + 10),
                new Vector2(origin.X + width, y + 10),
                new Color("56402a"),
                1);
            DrawText(
                new Vector2(origin.X + 3, y + 8),
                Shorten(inventory.Items[index], width < 80 ? 12 : 16),
                7,
                Text);
        }

        if (inventory.Items.Count == 0)
        {
            DrawText(origin + new Vector2(3, 8), "(empty)", 7, MutedText);
        }

        DrawText(
            new Vector2(origin.X, origin.Y + (maxRows * InventoryRowHeight) + 8),
            $"{inventory.Items.Count}/{inventory.Capacity}",
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
        DrawString(
            ThemeDB.FallbackFont,
            at,
            value,
            HorizontalAlignment.Left,
            width: -1,
            fontSize: size,
            modulate: colour);
    }

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : $"{value[..(length - 1)]}…";

    private static int Depth(GridPosition position) =>
        position.X + position.Y;

    private static Vector2 Iso(GridPosition position) =>
        new(
            IsoOriginX + ((position.X - position.Y) * TileHalfWidth),
            IsoOriginY + ((position.X + position.Y) * TileHalfHeight));

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
