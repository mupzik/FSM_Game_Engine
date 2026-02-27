using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Linq;

namespace GameProj
{
    /// <summary>
    /// Центральный класс управления игровым процессом.
    /// Координирует физику, состояния игры, отображение и логику FSM.
    /// </summary>
    public class GameManager
    {
        /// <summary>
        /// События глобального состояния игры.
        /// </summary>
        public enum Event
        {
            Trigger,        // Активация специального триггера (убийство дракона союзником)
            Heal,           // Найдено зелье лечения
            Sword,          // Найден меч
            DragonAlive,    // Дракон жив
            DragonDead      // Дракон убит
        }

        /// <summary>
        /// Глобальные состояния игры (сюжетный прогресс).
        /// </summary>
        public enum State_
        {
            Tutorial,
            Game,
            GameStarted,
            HealFound,
            SwordFound,
            AllFound,
            NothingFound,
            NothingFoundEnd,
            AllFoundEnd,
            SwordFoundEnd,
            HealFoundEnd,
            AllyKillsDragon
        }

        // Основные игровые объекты
        public Dragon _dragon;
        private readonly GameGrid _grid;
        private readonly GameCanvas _canvas;

        // ФИЗИЧЕСКИЙ ДВИЖОК
        private readonly PhysicsEngine _physics;

        // Системы хранения данных
        private readonly Dictionary<string, ImageSource> _spriteCache = new Dictionary<string, ImageSource>();
        private readonly List<Character> _characters = new List<Character>();
        private readonly Dictionary<Character, UIElement> _characterVisuals = new Dictionary<Character, UIElement>();
        private readonly Dictionary<(int x, int y), Image> _itemVisuals = new Dictionary<(int x, int y), Image>();

        private Ally _ally;
        private Player _player;
        private List<Vector2D> _interestPoints = new List<Vector2D>();

        internal readonly Random _rng = new Random();
        private const int TileSize = 32;

        private bool _wPressed, _aPressed, _sPressed, _dPressed;
        private FSM<State_, Event> _gameFSM;

        public IReadOnlyList<Character> Characters => _characters;
        public State_ CurrentGameState => _gameFSM.CurrentState.Id;
        public GameGrid Grid => _grid;
        internal int InterestPointsCount => _interestPoints.Count;

        public GameManager(GameCanvas canvas, int width, int height, Action<GameManager> mapInitializer = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _grid = new GameGrid(width, height);

            // Инициализация физического движка
            _physics = new PhysicsEngine(_grid);

            InitializeGameFSM();
            mapInitializer?.Invoke(this);
            PreloadSprites();
            DrawStaticMap();
            DrawItems();
        }

        public bool HasItemsOnGround()
        {
            for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (_grid[x, y]?.ItemOnGround != null) return true;
            return false;
        }

        private void InitializeGameFSM()
        {
            var tutorial = new State<State_, Event>(State_.Tutorial);
            var nothingFound = new State<State_, Event>(State_.NothingFound);
            var swordFound = new State<State_, Event>(State_.SwordFound);
            var healFound = new State<State_, Event>(State_.HealFound);
            var allFound = new State<State_, Event>(State_.AllFound);
            var nothingFoundEnd = new State<State_, Event>(State_.NothingFoundEnd);
            var swordFoundEnd = new State<State_, Event>(State_.SwordFoundEnd);
            var healFoundEnd = new State<State_, Event>(State_.HealFoundEnd);
            var allFoundEnd = new State<State_, Event>(State_.AllFoundEnd);
            var allyKillsDragon = new State<State_, Event>(State_.AllyKillsDragon);

            tutorial.SetUpdate(machine =>
            {
                if (_wPressed || _aPressed || _sPressed || _dPressed)
                    machine.SetState(nothingFound);
            });

            tutorial.SetEventHandler((machine, ev) =>
            {
                if (ev == Event.Trigger) machine.SetState(allyKillsDragon);
            });

            nothingFound.SetEventHandler((machine, ev) =>
            {
                switch (ev)
                {
                    case Event.Sword: machine.SetState(swordFound); break;
                    case Event.Heal: machine.SetState(healFound); break;
                    case Event.DragonDead: machine.SetState(nothingFoundEnd); break;
                    case Event.Trigger: machine.SetState(allyKillsDragon); break;
                }
            });

            swordFound.SetEventHandler((machine, ev) =>
            {
                if (ev == Event.Heal) machine.SetState(allFound);
                else if (ev == Event.DragonDead) machine.SetState(swordFoundEnd);
                else if (ev == Event.Trigger) machine.SetState(allyKillsDragon);
            });

            healFound.SetEventHandler((machine, ev) =>
            {
                if (ev == Event.Sword) machine.SetState(allFound);
                else if (ev == Event.DragonDead) machine.SetState(healFoundEnd);
                else if (ev == Event.Trigger) machine.SetState(allyKillsDragon);
            });

            allFound.SetEventHandler((machine, ev) =>
            {
                if (ev == Event.DragonDead) machine.SetState(allFoundEnd);
                else if (ev == Event.Trigger) machine.SetState(allyKillsDragon);
            });

            _gameFSM = new FSM<State_, Event>(tutorial);
        }

        public void SetDragon(Dragon dragon) => _dragon = dragon;
        public void SetAlly(Ally ally) => _ally = ally;

        public void OnTutorialKeyPress(Key key)
        {
            if (_gameFSM.CurrentState.Id != State_.Tutorial) return;
            switch (key)
            {
                case Key.W: _wPressed = true; break;
                case Key.A: _aPressed = true; break;
                case Key.S: _sPressed = true; break;
                case Key.D: _dPressed = true; break;
            }
        }

        public void OnItemPickedUp(string itemId, bool byAlly = false)
        {
            if (byAlly) return;
            if (_gameFSM.CurrentState.Id == State_.Tutorial) return;

            if (itemId == "Sword") _gameFSM.HandleEvent(Event.Sword);
            else if (itemId == "Potion") _gameFSM.HandleEvent(Event.Heal);
        }

        public void OnDragonKilled(bool byAlly = false)
        {
            if (byAlly) _gameFSM.HandleEvent(Event.Trigger);
            else _gameFSM.HandleEvent(Event.DragonDead);
        }

        private void PreloadSprites()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string tilesPath = Path.Combine(baseDir, "Tiles");
            string spritesPath = Path.Combine(baseDir, "Sprites");
            string itemsPath = Path.Combine(baseDir, "Items");

            var tileIds = new[] { "Grass", "Road", "Fence_H", "Fence_U", "Fence_Corner1", "Fence_Corner2", "2", "3", "9", "13", "Decor1", "Tree1", "Tree2" };
            foreach (var id in tileIds)
            {
                string filePath = Path.Combine(tilesPath, id + ".png");
                if (File.Exists(filePath)) _spriteCache[id] = new BitmapImage(new Uri(filePath));
                else throw new FileNotFoundException($"Sprite file for tile '{id}' not found.", filePath);
            }

            var animIds = new[] { "MC_D_Walk", "MC_U_Walk", "MC_L_Walk", "Orc_D_Walk", "Orc_U_Walk", "Orc_L_Walk" };
            foreach (var id in animIds)
            {
                string filePath = Path.Combine(spritesPath, id + ".png");
                if (File.Exists(filePath)) _spriteCache[id] = new BitmapImage(new Uri(filePath));
            }

            var itemIds = new[] { "Sword", "Potion" };
            foreach (var id in itemIds)
            {
                string filePath = Path.Combine(itemsPath, id + ".png");
                if (File.Exists(filePath)) _spriteCache[id] = new BitmapImage(new Uri(filePath));
                else
                {
                    var drawing = new GeometryDrawing { Brush = Brushes.Gray, Geometry = new EllipseGeometry(new Point(8, 8), 8, 8) };
                    _spriteCache[id] = new DrawingImage(drawing);
                }
            }

            var bossAnimIds = new[] { "Boss_idle", "Boss_attack", "Boss_death" };
            foreach (var id in bossAnimIds)
            {
                string filePath = Path.Combine(spritesPath, id + ".png");
                if (File.Exists(filePath)) _spriteCache[id] = new BitmapImage(new Uri(filePath));
                else
                {
                    var drawing = new GeometryDrawing { Brush = Brushes.Red, Geometry = new EllipseGeometry(new Point(24, 24), 20, 20) };
                    _spriteCache[id] = new DrawingImage(drawing);
                }
            }
        }

        public void AddInterestPoint(Vector2D point) => _interestPoints.Add(point);

        private ImageSource GetSprite(string spriteId) => _spriteCache.TryGetValue(spriteId, out var sprite) ? sprite : null;

        private void DrawStaticMap()
        {
            _canvas.GameArea.Children.Clear();
            for (int x = 0; x < _grid.Width; x++)
            {
                for (int y = 0; y < _grid.Height; y++)
                {
                    var cell = _grid[x, y];
                    var bgImage = new Image { Width = TileSize, Height = TileSize, Source = GetSprite(cell.BackgroundSpriteId) };
                    Canvas.SetLeft(bgImage, x * TileSize);
                    Canvas.SetTop(bgImage, y * TileSize);
                    _canvas.GameArea.Children.Add(bgImage);

                    if (!string.IsNullOrEmpty(cell.DecorSpriteId))
                    {
                        var decorImage = new Image { Width = TileSize, Height = TileSize, Source = GetSprite(cell.DecorSpriteId) };
                        Canvas.SetLeft(decorImage, x * TileSize);
                        Canvas.SetTop(decorImage, y * TileSize);
                        _canvas.GameArea.Children.Add(decorImage);
                    }
                }
            }
        }

        private void DrawItems()
        {
            foreach (var img in _itemVisuals.Values) _canvas.GameArea.Children.Remove(img);
            _itemVisuals.Clear();

            for (int x = 0; x < _grid.Width; x++)
            {
                for (int y = 0; y < _grid.Height; y++)
                {
                    var cell = _grid[x, y];
                    if (cell?.ItemOnGround?.Sprite != null)
                    {
                        var image = new Image { Width = 24, Height = 24, Source = cell.ItemOnGround.Sprite, Stretch = Stretch.Uniform };
                        Canvas.SetLeft(image, x * TileSize + (TileSize - 24) / 2.0);
                        Canvas.SetTop(image, y * TileSize + (TileSize - 24) / 2.0);
                        _canvas.GameArea.Children.Add(image);
                        _itemVisuals[(x, y)] = image;
                    }
                }
            }
        }

        public void SetTile(int x, int y, CellType type, string spriteId, string decorSpriteId = null)
        {
            if (!_grid.InBounds(x, y)) return;
            _grid.SetCell(x, y, type, spriteId, decorSpriteId);
        }

        public void PlaceItem(int x, int y, Item item)
        {
            if (_grid.InBounds(x, y)) _grid.PlaceItem(x, y, item);
        }

        public void AddCharacter(Character character)
        {
            if (character == null) return;
            _characters.Add(character);
            var visual = CreateCharacterVisual(character);
            _characterVisuals[character] = visual;
            _canvas.GameArea.Children.Add(visual);
        }

        private UIElement CreateCharacterVisual(Character ch)
        {
            string baseId;
            if (ch is Player)
                baseId = "MC";
            else if (ch is Dragon)
                baseId = "Boss";
            else
                baseId = "Orc";

            var image = new Image { Stretch = Stretch.None, RenderTransformOrigin = new Point(0.5, 0.5) };
            image.Tag = baseId;
            return image;
        }

        // Словарь для отслеживания времени анимации
        private readonly Dictionary<UIElement, double> _animationTime = new Dictionary<UIElement, double>();

        public void Update()
        {
            // 1. Обновляем глобальный FSM
            _gameFSM.Update();

            // 2. Обновляем логику персонажей (ИИ меняет Velocity, но не Position напрямую)
            foreach (var ch in _characters.ToArray())
            {
                if (ch.IsAlive) ch.Update();
            }


            _physics.UpdateCollisions(_characters);

            // 4. Проверка подбора предметов (после движения)
            foreach (var ch in _characters.ToArray())
            {
                if (!ch.IsAlive) continue;

                int cx = (int)Math.Floor(ch.Position.X);
                int cy = (int)Math.Floor(ch.Position.Y);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = cx + dx;
                        int y = cy + dy;
                        if (!_grid.InBounds(x, y)) continue;

                        var cell = _grid[x, y];
                        if (cell?.ItemOnGround != null)
                        {
                            var itemPos = new Vector2D(x + 0.5, y + 0.5);
                            if (Vector2D.Distance(ch.Position, itemPos) <= 0.8)
                            {
                                var item = cell.ItemOnGround;
                                if (ch.Inventory.TryAddItem(item.Id, 1))
                                {
                                    ch.PickupItem(item.Id);
                                    cell.ItemOnGround = null;
                                    if (_itemVisuals.TryGetValue((x, y), out Image img))
                                    {
                                        _canvas.GameArea.Children.Remove(img);
                                        _itemVisuals.Remove((x, y));
                                    }
                                    RemoveInterestPointAt(x, y);
                                }
                            }
                        }
                    }
                }
            }

            // 5. Рендеринг
            foreach (var kvp in _characterVisuals)
            {
                var character = kvp.Key;
                var visual = kvp.Value as Image;
                if (visual == null) continue;

                if (!character.IsAlive)
                {
                    visual.Visibility = Visibility.Collapsed;
                    continue;
                }

                visual.Visibility = Visibility.Visible;
                string baseId = (string)visual.Tag;
                string animKey = character.GetAnimationKey(character.Velocity);

                if (animKey == null) animKey = "_D_Walk";

                bool flipHorizontally = animKey == "_R_Walk";
                if (flipHorizontally) animKey = "_L_Walk";

                string fullAnimId = baseId + animKey;
                if (!_spriteCache.TryGetValue(fullAnimId, out ImageSource sheet)) continue;

                BitmapImage bitmap = sheet as BitmapImage;
                if (bitmap == null) continue;

                int frameCount = 6;
                int frameWidth = 48;
                int frameHeight = 48;
                double frameDuration = 0.08;

                if (character is Dragon)
                {
                    frameWidth = 72; frameHeight = 72; frameDuration = 0.12;
                    switch (animKey)
                    {
                        case "_idle": case "_death": frameCount = 4; break;
                        case "_attack": frameCount = 6; break;
                    }
                }

                var anim = new AnimatedSprite(bitmap, frameCount, frameWidth, frameHeight, frameDuration);
                bool isMoving = character.Velocity.Length() > 0 || character is Dragon;

                if (!_animationTime.ContainsKey(visual)) _animationTime[visual] = 0;
                _animationTime[visual] += 1.0 / 60.0;
                if (!isMoving) _animationTime[visual] = 0;

                int frame = isMoving ? (int)(_animationTime[visual] / frameDuration) % frameCount : 0;
                visual.Source = anim.GetFrame(frame);

                var pos = character.Position;
                double left = pos.X * TileSize + (TileSize - frameWidth) / 2.0;
                double top = pos.Y * TileSize + (TileSize - frameHeight) / 2.0;
                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);

                visual.RenderTransform = flipHorizontally ? new ScaleTransform(-1, 1) : null;
            }

            // 6. Логика победы (Союзник убивает дракона)
            if (_ally != null && _dragon != null && _ally.IsAlive && _dragon.IsAlive)
            {
                if (_ally.Inventory.HasItem("Sword"))
                {
                    if (Vector2D.Distance(_ally.Position, _dragon.Position) < 1.0)
                    {
                        _dragon.Die();
                        OnDragonKilled(byAlly: true);
                    }
                }
            }
        }

        public bool IsWalkable(int x, int y) => _grid.IsWalkable(x, y);

        public void RemoveInterestPointAt(int x, int y)
        {
            _interestPoints.RemoveAll(pt => (int)Math.Floor(pt.X) == x && (int)Math.Floor(pt.Y) == y);
        }
    }





    public interface ICollidable
    {
        Vector2D Position { get; set; }
        Vector2D Velocity { get; set; }
        double Size { get; } // Размер хитбокса (например, 0.6 клетки)
        bool IsAlive { get; }
    }

    /// <summary>
    /// Простой и надежный движок коллизий (AABB).
    /// </summary>
    public class PhysicsEngine
    {
        private readonly GameGrid _grid;
        private const double Epsilon = 0.0001; // Микро-зазор

        public PhysicsEngine(GameGrid grid)
        {
            _grid = grid;
        }

        public void UpdateCollisions(List<Character> characters)
        {
            foreach (var ch in characters)
            {
                if (!ch.IsAlive) continue;

                // Если скорость очень маленькая, обнуляем её
                if (Math.Abs(ch.Velocity.X) < Epsilon && Math.Abs(ch.Velocity.Y) < Epsilon)
                {
                    ch.Velocity = Vector2D.Zero;
                    continue;
                }

                MoveWithCollision(ch);
            }
        }

        private void MoveWithCollision(Character ch)
        {
            double halfSize = ch.Size / 2.0;

            // --- ОСЬ X ---
            double nextX = ch.Position.X + ch.Velocity.X;

            // Границы хитбокса по X
            double left = nextX - halfSize;
            double right = nextX + halfSize;

            // Границы по Y (текущие)
            double top = ch.Position.Y - halfSize;
            double bottom = ch.Position.Y + halfSize;

            // Определяем диапазон клеток для проверки
            // Вычитаем Epsilon у правой/нижней границы, чтобы не захватывать соседнюю клетку при идеальном касании
            int minCol = (int)Math.Floor(left);
            int maxCol = (int)Math.Floor(right - Epsilon);
            int minRow = (int)Math.Floor(top);
            int maxRow = (int)Math.Floor(bottom - Epsilon);

            bool collisionX = false;

            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    if (_grid.InBounds(c, r) && !_grid[c, r].IsWalkable())
                    {
                        collisionX = true;

                        // Разрешение коллизии
                        if (ch.Velocity.X > 0) // Движемся вправо -> уперлись в левую границу клетки c
                        {
                            nextX = c - halfSize - Epsilon;
                        }
                        else if (ch.Velocity.X < 0) // Движемся влево -> уперлись в правую границу клетки c
                        {
                            nextX = (c + 1) + halfSize + Epsilon;
                        }
                        break;
                    }
                }
                if (collisionX) break;
            }

            ch.Position.X = nextX;
            if (collisionX) ch.Velocity = new Vector2D(0, ch.Velocity.Y);

            // --- ОСЬ Y ---
            // Используем уже обновленный X
            double nextY = ch.Position.Y + ch.Velocity.Y;

            top = nextY - halfSize;
            bottom = nextY + halfSize;

            // Границы по X (уже обновленные)
            left = ch.Position.X - halfSize;
            right = ch.Position.X + halfSize;

            minRow = (int)Math.Floor(top);
            maxRow = (int)Math.Floor(bottom - Epsilon);
            int minColY = (int)Math.Floor(left);
            int maxColY = (int)Math.Floor(right - Epsilon);

            bool collisionY = false;

            for (int c = minColY; c <= maxColY; c++)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    if (_grid.InBounds(c, r) && !_grid[c, r].IsWalkable())
                    {
                        collisionY = true;

                        if (ch.Velocity.Y > 0) // Вниз -> уперлись в верхнюю границу клетки r
                        {
                            nextY = r - halfSize - Epsilon;
                        }
                        else if (ch.Velocity.Y < 0) // Вверх -> уперлись в нижнюю границу клетки r
                        {
                            nextY = (r + 1) + halfSize + Epsilon;
                        }
                        break;
                    }
                }
                if (collisionY) break;
            }

            ch.Position.Y = nextY;
            if (collisionY) ch.Velocity = new Vector2D(ch.Velocity.X, 0);
        }
    }
}