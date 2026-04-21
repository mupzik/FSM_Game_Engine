using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows; // Для Rect

namespace GameProj.src
{
    /// <summary>
    /// Интерфейс для триггеров клетки.
    /// Триггеры вызываются при входе/выходе персонажа из клетки.
    /// </summary>
    public interface ICellTrigger
    {
        /// <summary>
        /// Вызывается при входе персонажа в клетку.
        /// </summary>
        bool OnEnter(Character character, GameGrid grid, int x, int y);

        /// <summary>
        /// Вызывается при выходе персонажа из клетки.
        /// </summary>
        void OnExit(Character character, GameGrid grid, int x, int y);
    }

    /// <summary>
    /// Реализация триггера через лямбда-выражения.
    /// </summary>
    public class LambdaTrigger : ICellTrigger
    {
        private readonly Func<Character, GameGrid, int, int, bool> _onEnter;
        private readonly Action<Character, GameGrid, int, int> _onExit;

        public LambdaTrigger(Func<Character, GameGrid, int, int, bool> onEnter = null,
                           Action<Character, GameGrid, int, int> onExit = null)
        {
            _onEnter = onEnter;
            _onExit = onExit;
        }

        public bool OnEnter(Character ch, GameGrid g, int x, int y) => _onEnter?.Invoke(ch, g, x, y) ?? true;

        public void OnExit(Character ch, GameGrid g, int x, int y) => _onExit?.Invoke(ch, g, x, y);
    }

    /// <summary>
    /// Типы клеток игровой сетки.
    /// </summary>
    public enum TileType
    {
        Floor,  // Пол - проходимая клетка
        Wall    // Стена - непроходимая клетка
    }

    /// <summary>
    /// Представляет одну клетку игровой сетки.
    /// </summary>
    public class Tile
    {
        public TileType Type { get; set; }
        public string BackgroundSpriteId { get; set; }
        public string DecorSpriteId { get; set; } = string.Empty;
        public ICellTrigger Trigger { get; set; }
        public Item ItemOnGround { get; set; }

        public Tile(TileType type, string backgroundSpriteId, string decorSpriteId = null,
                    ICellTrigger trigger = null)
        {
            Type = type;
            BackgroundSpriteId = backgroundSpriteId;
            DecorSpriteId = decorSpriteId;
            Trigger = trigger;
        }

        public bool IsWalkable() => Type == TileType.Floor;
    }

    /// <summary>
    /// Игровая сетка (карта).
    /// </summary>
    public class GameGrid
    {
        private readonly Tile[,] _grid;

        // ✅ Теперь это обычное поле, а не константа. Можно менять в конструкторе.
        public int TileSize { get; private set; }

        public int Width { get; }
        public int Height { get; }

        // Добавим параметр tileSize в конструктор
        public GameGrid(int width, int height, int tileSize = 32, string sprite = null)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Размеры должны быть положительными.");

            TileSize = tileSize; // Запоминаем размер

            Width = width;
            Height = height;
            _grid = new Tile[width, height];

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _grid[x, y] = new Tile(TileType.Floor, sprite);
        }

        // ... (остальные методы: PlaceItem, indexer, InBounds, IsWalkable, UpdateCell - без изменений) ...

        public void PlaceItem(int x, int y, Item item)
        {
            if (InBounds(x, y))
                _grid[x, y].ItemOnGround = item;
            else throw new IndexOutOfRangeException("Индекс клетки вне допустимых значений");
        }

        public Tile this[int x, int y]
        {
            get
            {
                if (!InBounds(x, y)) throw new IndexOutOfRangeException();
                return _grid[x, y];
            }
            set
            {
                if (!InBounds(x, y)) throw new IndexOutOfRangeException();
                _grid[x, y] = value;
            }
        }

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public bool IsWalkable(int x, int y) => InBounds(x, y) && _grid[x, y]?.IsWalkable() == true;

        public void UpdateCell(int x, int y, TileType? type = null, string backgroundSpriteId = null,
                              string decorSpriteId = null, ICellTrigger trigger = null)
        {
            if (!InBounds(x, y)) return;
            var tile = this[x, y];

            if (type.HasValue) tile.Type = type.Value;
            if (backgroundSpriteId != null) tile.BackgroundSpriteId = backgroundSpriteId;
            if (decorSpriteId != null) tile.DecorSpriteId = decorSpriteId;
            if (trigger != null) tile.Trigger = trigger;
        }

        // ✅ ИСПРАВЛЕННЫЙ GetTileBounds
        // Rect принимает (X, Y, Width, Height), а не (Left, Top, Right, Bottom)
        public Rect GetTileBounds(int x, int y)
        {
            if (!InBounds(x, y)) return new Rect();
            return new Rect(x * TileSize, y * TileSize, TileSize, TileSize);
        }

        // ✅ МЕТОДЫ СТАЛИ НЕСТАТИЧЕСКИМИ
        // Теперь они используют this.TileSize

        /// <summary>
        /// Переводит координаты клетки (x, y) в пиксельные координаты ЦЕНТРА этой клетки.
        /// </summary>
        public Vector2D GridToPixelCenter(int gridX, int gridY)
        {
            return new Vector2D(
                gridX * TileSize + (TileSize / 2.0),
                gridY * TileSize + (TileSize / 2.0)
            );
        }

        /// <summary>
        /// Переводит координаты клетки (x, y) в пиксельные координаты ЛЕВОГО ВЕРХНЕГО УГЛА.
        /// </summary>
        public Vector2D GridToPixelTopLeft(int gridX, int gridY)
        {
            return new Vector2D(
                gridX * TileSize,
                gridY * TileSize
            );
        }
    }
}