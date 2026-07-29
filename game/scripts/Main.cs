using Ash.Sim;
using Godot;

namespace Ash.Game;

public partial class Main : Node2D
{
    private const int TileSize = 12;
    private const int MapOriginX = 6;
    private const int MapOriginY = 6;
    private const int InventoryRowHeight = 12;

    private static readonly Color GroundA = new("33442f");
    private static readonly Color GroundB = new("2b3a29");
    private static readonly Color Grid = new("40513b");
    private static readonly Color Panel = new("171a20");
    private static readonly Color PanelEdge = new("9a835d");
    private static readonly Color Text = new("eee5ce");
    private static readonly Color MutedText = new("a9a38f");
    private static readonly Color Highlight = new("f1d96a");

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
        DrawRect(new Rect2(0, 0, 240, 200), new Color("202a20"));

        for (var y = 0; y < PlayableSliceWorld.MapHeight; y++)
        {
            for (var x = 0; x < PlayableSliceWorld.MapWidth; x++)
            {
                var colour = (x + y) % 2 == 0 ? GroundA : GroundB;
                var rect = CellRect(new GridPosition(x, y));
                DrawRect(rect, colour);
                DrawRect(rect, Grid, filled: false, width: 1);
            }
        }

        foreach (var chest in _world.Chests)
        {
            DrawChest(chest);
        }

        foreach (var monster in _world.Monsters)
        {
            DrawMonster(monster);
        }

        DrawPlayer();
    }

    private void DrawPlayer()
    {
        var rect = CellRect(_world.PlayerPosition);
        DrawRect(
            new Rect2(rect.Position + new Vector2(2, 4), new Vector2(8, 7)),
            new Color("3c70c6"));
        DrawRect(
            new Rect2(rect.Position + new Vector2(4, 1), new Vector2(5, 4)),
            new Color("e8bd8e"));

        // The backpack is always visible on the character's back.
        DrawRect(
            new Rect2(rect.Position + new Vector2(1, 5), new Vector2(3, 5)),
            new Color("855631"));
        DrawRect(
            new Rect2(rect.Position + new Vector2(0, 6), new Vector2(2, 3)),
            new Color("b07940"));

        if (_world.Chests.Any(chest =>
                _world.PlayerPosition.ManhattanDistance(chest.Position) <= 1) ||
            _world.Monsters.Any(monster =>
                monster.IsAlive &&
                _world.PlayerPosition.ManhattanDistance(monster.Position) <= 1))
        {
            DrawRect(rect.Grow(1), Highlight, filled: false, width: 1);
        }
    }

    private void DrawChest(ChestState chest)
    {
        var rect = CellRect(chest.Position);
        var isCorpse = chest.Id.StartsWith("remains-", StringComparison.Ordinal);
        var body = isCorpse ? new Color("77716a") : new Color("83552f");
        var edge = isCorpse ? new Color("b9b2a5") : new Color("d0a05d");

        DrawRect(
            new Rect2(rect.Position + new Vector2(1, 5), new Vector2(10, 6)),
            body);

        var lidY = chest.IsOpen ? rect.Position.Y + 1 : rect.Position.Y + 3;
        DrawRect(new Rect2(rect.Position.X + 1, lidY, 10, 3), edge);
        DrawRect(
            new Rect2(rect.Position + new Vector2(5, 6), new Vector2(2, 3)),
            new Color("e4c35f"));
    }

    private void DrawMonster(MonsterState monster)
    {
        var rect = CellRect(monster.Position);
        if (!monster.IsAlive)
        {
            DrawLine(
                rect.Position + new Vector2(2, 9),
                rect.Position + new Vector2(10, 9),
                new Color("803838"),
                2);
            return;
        }

        var colour = monster.Id == "skeleton"
            ? new Color("d2d0bd")
            : new Color("9a4343");
        DrawCircle(rect.Position + new Vector2(6, 6), 5, colour);
        DrawRect(new Rect2(rect.Position + new Vector2(3, 4), new Vector2(2, 2)), Colors.Black);
        DrawRect(new Rect2(rect.Position + new Vector2(7, 4), new Vector2(2, 2)), Colors.Black);

        var healthWidth = 10f * monster.Health / monster.MaxHealth;
        DrawRect(new Rect2(rect.Position.X + 1, rect.Position.Y - 2, 10, 2), new Color("381616"));
        DrawRect(new Rect2(rect.Position.X + 1, rect.Position.Y - 2, healthWidth, 2), new Color("d34b4b"));
    }

    private void DrawHud()
    {
        DrawRect(new Rect2(240, 0, 80, 200), Panel);
        DrawLine(new Vector2(240, 0), new Vector2(240, 200), PanelEdge);

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
        DrawRect(new Rect2(18, 18, 210, 145), new Color("111319e8"));
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
            DrawRect(new Rect2(origin.X, y, width, 10), new Color("252a31"));
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

    private static Rect2 CellRect(GridPosition position) =>
        new(
            MapOriginX + (position.X * TileSize),
            MapOriginY + (position.Y * TileSize),
            TileSize,
            TileSize);
}
