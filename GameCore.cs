using GameProj.src;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

namespace GameProj
{
    public class GameManager
    {
        public event Action OnGameOver;
        public event Action OnWin;

        public enum Event { Trigger, Heal, Sword, DragonAlive, DragonDead }
        public enum State_ { Tutorial, Start, End }

        // Константы
        private const int TileSize = 32;
        private const double Epsilon = 0.001;
        private const double PickupDistance = 40.0;
        private const double AnimationFrameTime = 0.1;
        private const int ItemIconSize = 24;
        private const double DefaultFrameDuration = 1.0 / 60.0;

        private static readonly Color PlaceholderBackground = Colors.Gray;
        private static readonly Color PlaceholderError = Colors.Red;

        private readonly GameGrid _grid;
        private readonly GameCanvas _canvas;
        private readonly PhysicsEngine _physics;

        // Оптимизированные кэши
        private static readonly Dictionary<string, ImageSource> _spriteCache = new Dictionary<string, ImageSource>();
        private static readonly Dictionary<string, CroppedBitmap> _frameCache = new Dictionary<string, CroppedBitmap>();
        private static readonly ScaleTransform _flipTransform = new ScaleTransform(-1, 1);
        private static readonly ScaleTransform _normalTransform = new ScaleTransform(1, 1);

        private readonly List<Character> _characters = new List<Character>();
        private readonly Dictionary<Character, UIElement> _characterVisuals = new Dictionary<Character, UIElement>();
        private readonly Dictionary<(int x, int y), Image> _itemVisuals = new Dictionary<(int x, int y), Image>();

        // Оптимизированный список предметов на земле
        private readonly List<GroundItem> _groundItems = new List<GroundItem>();

        private Ally _ally;
        private Player _player;

        internal readonly Random _rng = new Random();

        // FSM
        private FSM<State_, Event> _gameFSM;
        private bool _wPressed, _aPressed, _sPressed, _dPressed;

        private readonly Dictionary<UIElement, double> _animationTime = new Dictionary<UIElement, double>();

        private readonly string _tilesPath, _spritesPath, _itemsPath;

        public IReadOnlyList<Character> Characters => _characters;
        public State_ CurrentGameState => _gameFSM?.CurrentState?.Id ?? State_.Tutorial;
        public GameGrid Grid => _grid;

        public GameManager(GameCanvas canvas, int width, int height, Action<GameManager> mapInitializer = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _grid = new GameGrid(width, height, TileSize);

            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _tilesPath = Path.Combine(baseDir, "Tiles");
            _spritesPath = Path.Combine(baseDir, "Sprites");
            _itemsPath = Path.Combine(baseDir, "Items");

            _physics = new PhysicsEngine(_grid);

            InitializeBaseFSM();

            mapInitializer?.Invoke(this);

            DrawStaticMap();
            DrawItems();
        }

        private void InitializeBaseFSM()
        {
            var tutorial = new State<State_, Event>(State_.Tutorial);
            var start = new State<State_, Event>(State_.Start);
            var end = new State<State_, Event>(State_.End);

            tutorial.SetUpdate(m =>
            {
                if (_wPressed || _aPressed || _sPressed || _dPressed)
                    m.SetState(start);
            });

            _gameFSM = new FSM<State_, Event>(tutorial);
        }

        public void ShakeCamera() { _canvas.TriggerShake(); }

        public bool HasItemsOnGround() => _groundItems.Count > 0;

        public void SetAlly(Ally ally) => _ally = ally;
        public void SetPlayer(Player player) => _player = player;

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

        public void OnItemPickedUp(string itemId, bool byAlly = false) { }

        // --- ОПТИМИЗИРОВАННЫЕ МЕТОДЫ ЗАГРУЗКИ ---
        private ImageSource GetOrCreateSprite(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            if (_spriteCache.TryGetValue(filePath, out ImageSource cached))
                return cached;

            ImageSource result = File.Exists(filePath) ? LoadBitmap(filePath) : CreatePlaceholder(PlaceholderBackground);
            _spriteCache[filePath] = result;
            return result;
        }

        private CroppedBitmap GetOrCreateFrame(string spritePath, int frameIndex, int frameSize, BitmapSource sheetSource = null)
        {
            string cacheKey = $"{spritePath}:{frameIndex}";

            if (_frameCache.TryGetValue(cacheKey, out CroppedBitmap cached))
                return cached;

            if (sheetSource == null)
            {
                var source = GetOrCreateSprite(spritePath) as BitmapSource;
                if (source == null) return null;
                sheetSource = source;
            }

            try
            {
                int xPos = frameIndex * frameSize;
                var cropped = new CroppedBitmap(sheetSource, new Int32Rect(xPos, 0, frameSize, frameSize));
                cropped.Freeze();
                _frameCache[cacheKey] = cropped;
                return cropped;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка обрезки спрайта: {ex.Message}");
                return null;
            }
        }

        // --- ОТРИСОВКА ---
        private void DrawStaticMap()
        {
            _canvas.GameArea.Children.Clear();

            for (int x = 0; x < _grid.Width; x++)
            {
                for (int y = 0; y < _grid.Height; y++)
                {
                    var cell = _grid[x, y];

                    // Фон
                    if (!string.IsNullOrEmpty(cell.BackgroundSpriteId))
                    {
                        string bgPath = Path.Combine(_tilesPath, cell.BackgroundSpriteId + ".png");
                        var bgSource = GetOrCreateSprite(bgPath);
                        if (bgSource != null)
                        {
                            var bgImage = new Image
                            {
                                Width = TileSize,
                                Height = TileSize,
                                Source = bgSource
                            };
                            Canvas.SetLeft(bgImage, x * TileSize);
                            Canvas.SetTop(bgImage, y * TileSize);
                            Canvas.SetZIndex(bgImage, 0);
                            _canvas.GameArea.Children.Add(bgImage);
                        }
                    }

                    // Декор
                    if (!string.IsNullOrEmpty(cell.DecorSpriteId))
                    {
                        string decorPath = Path.Combine(_tilesPath, cell.DecorSpriteId + ".png");
                        var decorSource = GetOrCreateSprite(decorPath);
                        if (decorSource != null)
                        {
                            var decorImage = new Image
                            {
                                Width = TileSize,
                                Height = TileSize,
                                Source = decorSource
                            };
                            Canvas.SetLeft(decorImage, x * TileSize);
                            Canvas.SetTop(decorImage, y * TileSize);
                            Canvas.SetZIndex(decorImage, 1);
                            _canvas.GameArea.Children.Add(decorImage);
                        }
                    }
                }
            }
        }

        private void DrawItems()
        {
            foreach (var img in _itemVisuals.Values)
                _canvas.GameArea.Children.Remove(img);
            _itemVisuals.Clear();

            foreach (var groundItem in _groundItems)
            {
                var itemSource = GetOrCreateSprite(groundItem.Item.IconPath);
                if (itemSource != null)
                {
                    var image = new Image
                    {
                        Width = ItemIconSize,
                        Height = ItemIconSize,
                        Source = itemSource,
                        Stretch = Stretch.Uniform
                    };

                    double offset = (TileSize - ItemIconSize) / 2.0;
                    Canvas.SetLeft(image, groundItem.X * TileSize + offset);
                    Canvas.SetTop(image, groundItem.Y * TileSize + offset);
                    Canvas.SetZIndex(image, 2);
                    _canvas.GameArea.Children.Add(image);
                    _itemVisuals[(groundItem.X, groundItem.Y)] = image;
                }
            }
        }

        public void SetTile(int x, int y, TileType type, string spriteId, string decorSpriteId = null)
        {
            if (!_grid.InBounds(x, y)) return;
            _grid.UpdateCell(x, y, type, backgroundSpriteId: spriteId, decorSpriteId: decorSpriteId);
        }

        public void PlaceItem(int x, int y, Item item)
        {
            if (!_grid.InBounds(x, y)) return;
            _grid.PlaceItem(x, y, item);
            _groundItems.Add(new GroundItem(x, y, item));
            DrawItems();
        }

        public void AddCharacter(Character character)
        {
            if (character == null) return;
            _characters.Add(character);
            var visual = CreateCharacterVisual(character);
            _characterVisuals[character] = visual;
            Canvas.SetZIndex(visual, 10);
            _canvas.GameArea.Children.Add(visual);
        }

        private UIElement CreateCharacterVisual(Character ch)
        {
            var image = new Image
            {
                Stretch = Stretch.None,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            string baseName = "MC";
            if (ch is Ally) baseName = "Orc";
            if (!string.IsNullOrEmpty(ch.SpritePath))
                baseName = Path.GetFileNameWithoutExtension(ch.SpritePath);

            image.Tag = baseName;
            return image;
        }

        public void Update()
        {
            _gameFSM?.Update();

            foreach (var ch in _characters.ToArray())
            {
                if (ch.IsAlive)
                    ch.Update();
            }

            _physics.UpdateCollisions(_characters);

            // Оптимизированный подбор предметов
            UpdatePickups();

            // Рендеринг персонажей
            RenderCharacters();
        }

        private void UpdatePickups()
        {
            var itemsToRemove = new List<GroundItem>();

            foreach (var ch in _characters)
            {
                if (!ch.IsAlive) continue;

                foreach (var groundItem in _groundItems)
                {
                    Vector2D itemPos = new Vector2D(
                        groundItem.X * TileSize + TileSize / 2.0,
                        groundItem.Y * TileSize + TileSize / 2.0
                    );

                    if (Vector2D.Distance(ch.Position, itemPos) <= PickupDistance)
                    {
                        if (ch.Inventory.AddItem(groundItem.Item) >= 0)
                        {
                            ch.PickupItem(groundItem.Item.Key);
                            itemsToRemove.Add(groundItem);

                            if (_itemVisuals.TryGetValue((groundItem.X, groundItem.Y), out Image img))
                            {
                                _canvas.GameArea.Children.Remove(img);
                                _itemVisuals.Remove((groundItem.X, groundItem.Y));
                            }

                            if (ch is Player)
                                OnItemPickedUp(groundItem.Item.Key, false);

                            break; // Предмет подобран, выходим из цикла
                        }
                    }
                }
            }

            foreach (var item in itemsToRemove)
            {
                _groundItems.Remove(item);
                _grid[item.X, item.Y].ItemOnGround = null;
            }
        }

        private void RenderCharacters()
        {
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
                string animKeySuffix = character.GetAnimationKey(character.Velocity);

                if (string.IsNullOrEmpty(animKeySuffix))
                    animKeySuffix = "_D_Walk";

                bool needsFlip = animKeySuffix == "_R_Walk";
                string finalAnimSuffix = needsFlip ? "_L_Walk" : animKeySuffix;
                string animFileName = $"{baseId}{finalAnimSuffix}.png";
                string fullAnimPath = Path.Combine(_spritesPath, animFileName);

                var sheetSource = GetOrCreateSprite(fullAnimPath) as BitmapSource;

                if (sheetSource == null)
                {
                    string staticPath = Path.Combine(_spritesPath, $"{baseId}.png");
                    sheetSource = GetOrCreateSprite(staticPath) as BitmapSource;
                }

                ImageSource currentSource = null;

                if (sheetSource != null)
                {
                    int frameCount = sheetSource.PixelWidth / character.FrameSize;

                    if (frameCount <= 1 || sheetSource.PixelWidth == character.FrameSize)
                    {
                        currentSource = sheetSource;
                    }
                    else
                    {
                        bool isMoving = character.Velocity.Length() > Epsilon;

                        if (!_animationTime.ContainsKey(visual))
                            _animationTime[visual] = 0;

                        if (isMoving)
                            _animationTime[visual] += DefaultFrameDuration;
                        else
                            _animationTime[visual] = 0;

                        int frameIndex = isMoving
                            ? (int)(_animationTime[visual] / AnimationFrameTime) % frameCount
                            : 0;

                        if (frameIndex >= frameCount)
                            frameIndex = frameCount - 1;

                        currentSource = GetOrCreateFrame(fullAnimPath, frameIndex, character.FrameSize, sheetSource);
                    }
                }

                if (currentSource == null)
                {
                    currentSource = CreatePlaceholder(PlaceholderError);
                }

                double displaySize = character.FrameSize;
                visual.Source = currentSource;
                visual.Width = displaySize;
                visual.Height = displaySize;
                visual.Stretch = Stretch.Uniform;

                double left = character.Position.X - (displaySize / 2.0);
                double top = character.Position.Y - (displaySize / 2.0);
                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);

                // Оптимизированное применение трансформаций
                visual.RenderTransform = needsFlip ? _flipTransform : _normalTransform;
            }
        }

        public bool IsWalkable(int x, int y) => _grid.IsWalkable(x, y);

        private BitmapImage LoadBitmap(string path)
        {
            if (!File.Exists(path)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        public void RefreshItemsVisuals()
        {
            DrawItems();
        }

        private ImageSource CreatePlaceholder(Color color)
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing
            {
                Brush = new SolidColorBrush(color),
                Geometry = new RectangleGeometry(new Rect(0, 0, TileSize, TileSize))
            });
            return new DrawingImage(drawing);
        }

        // Вспомогательный класс для предметов на земле
        private class GroundItem
        {
            public int X { get; }
            public int Y { get; }
            public Item Item { get; }

            public GroundItem(int x, int y, Item item)
            {
                X = x;
                Y = y;
                Item = item;
            }
        }
    }
    






    public class PhysicsEngine
    {
        private const int TileSize = 32;
        private const double Epsilon = 0.001;

        private readonly GameGrid _grid;

        public PhysicsEngine(GameGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        public void UpdateCollisions(List<Character> characters)
        {
            foreach (var ch in characters)
            {
                if (!ch.IsAlive) continue;

                CorrectStuckPosition(ch);

                Vector2D oldPosition = ch.Position;
                Vector2D newPosition = new Vector2D(
                    ch.Position.X + ch.Velocity.X,
                    ch.Position.Y + ch.Velocity.Y
                );

                if (TryResolveCollision(ch, oldPosition, newPosition, out Vector2D resolvedPosition))
                {
                    ch.Position = resolvedPosition;

                    if (Math.Abs(ch.Position.X - oldPosition.X) < Epsilon)
                        ch.Velocity.X = 0;
                    if (Math.Abs(ch.Position.Y - oldPosition.Y) < Epsilon)
                        ch.Velocity.Y = 0;
                }
                else
                {
                    ch.Position = newPosition;
                }

                // Обнуляем очень маленькие скорости
                if (Math.Abs(ch.Velocity.X) < Epsilon) ch.Velocity.X = 0;
                if (Math.Abs(ch.Velocity.Y) < Epsilon) ch.Velocity.Y = 0;
            }
        }

        private bool TryResolveCollision(Character ch, Vector2D oldPos, Vector2D newPos, out Vector2D resolvedPos)
        {
            Vector2D direction = new Vector2D(newPos.X - oldPos.X, newPos.Y - oldPos.Y);
            double distance = direction.Length();

            if (distance < Epsilon)
            {
                resolvedPos = newPos;
                return false;
            }

            direction = new Vector2D(direction.X / distance, direction.Y / distance);
            double stepSize = Math.Min(TileSize / 4.0, distance / 10.0);

            Vector2D lastValidPos = oldPos;

            for (double t = stepSize; t <= distance; t += stepSize)
            {
                Vector2D checkPoint = new Vector2D(
                    oldPos.X + direction.X * t,
                    oldPos.Y + direction.Y * t
                );

                if (CheckPositionCollision(ch, checkPoint, out Vector2D collisionPoint))
                {
                    resolvedPos = lastValidPos;
                    return true;
                }

                lastValidPos = checkPoint;
            }

            resolvedPos = newPos;
            return false;
        }

        private bool CheckPositionCollision(Character ch, Vector2D position, out Vector2D collisionPoint)
        {
            double halfSize = ch.Size / 2.0;
            double left = position.X - halfSize;
            double right = position.X + halfSize;
            double top = position.Y - halfSize;
            double bottom = position.Y + halfSize;

            int minCol = (int)Math.Floor(left / TileSize);
            int maxCol = (int)Math.Floor((right - Epsilon) / TileSize);
            int minRow = (int)Math.Floor(top / TileSize);
            int maxRow = (int)Math.Floor((bottom - Epsilon) / TileSize);

            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    if (!_grid.InBounds(c, r)) continue;
                    if (_grid[c, r].IsWalkable()) continue;

                    Rect wallRect = new Rect(c * TileSize, r * TileSize, TileSize, TileSize);
                    Rect charRect = new Rect(left, top, ch.Size, ch.Size);

                    if (wallRect.IntersectsWith(charRect))
                    {
                        collisionPoint = CalculateCollisionPoint(wallRect, charRect, position, halfSize);
                        return true;
                    }
                }
            }

            collisionPoint = position;
            return false;
        }

        private Vector2D CalculateCollisionPoint(Rect wallRect, Rect charRect, Vector2D position, double halfSize)
        {
            double overlapLeft = charRect.Right - wallRect.Left;
            double overlapRight = wallRect.Right - charRect.Left;
            double overlapTop = charRect.Bottom - wallRect.Top;
            double overlapBottom = wallRect.Bottom - charRect.Top;

            double minOverlap = Math.Min(Math.Min(overlapLeft, overlapRight), Math.Min(overlapTop, overlapBottom));

            if (Math.Abs(minOverlap - overlapLeft) < Epsilon)
                return new Vector2D(wallRect.Left - halfSize - Epsilon, position.Y);
            else if (Math.Abs(minOverlap - overlapRight) < Epsilon)
                return new Vector2D(wallRect.Right + halfSize + Epsilon, position.Y);
            else if (Math.Abs(minOverlap - overlapTop) < Epsilon)
                return new Vector2D(position.X, wallRect.Top - halfSize - Epsilon);
            else
                return new Vector2D(position.X, wallRect.Bottom + halfSize + Epsilon);
        }

        private void CorrectStuckPosition(Character ch)
        {
            double halfSize = ch.Size / 2.0;
            double left = ch.Position.X - halfSize;
            double right = ch.Position.X + halfSize;
            double top = ch.Position.Y - halfSize;
            double bottom = ch.Position.Y + halfSize;

            int minCol = (int)Math.Floor(left / TileSize);
            int maxCol = (int)Math.Floor((right - Epsilon) / TileSize);
            int minRow = (int)Math.Floor(top / TileSize);
            int maxRow = (int)Math.Floor((bottom - Epsilon) / TileSize);

            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    if (!_grid.InBounds(c, r)) continue;
                    if (_grid[c, r].IsWalkable()) continue;

                    Rect wallRect = new Rect(c * TileSize, r * TileSize, TileSize, TileSize);
                    Rect charRect = new Rect(left, top, ch.Size, ch.Size);

                    if (wallRect.IntersectsWith(charRect))
                    {
                        double overlapLeft = charRect.Right - wallRect.Left;
                        double overlapRight = wallRect.Right - charRect.Left;
                        double overlapTop = charRect.Bottom - wallRect.Top;
                        double overlapBottom = wallRect.Bottom - charRect.Top;

                        double minOverlap = Math.Min(Math.Min(overlapLeft, overlapRight), Math.Min(overlapTop, overlapBottom));

                        if (Math.Abs(minOverlap - overlapLeft) < Epsilon)
                            ch.Position.X -= overlapLeft + Epsilon;
                        else if (Math.Abs(minOverlap - overlapRight) < Epsilon)
                            ch.Position.X += overlapRight + Epsilon;
                        else if (Math.Abs(minOverlap - overlapTop) < Epsilon)
                            ch.Position.Y -= overlapTop + Epsilon;
                        else
                            ch.Position.Y += overlapBottom + Epsilon;

                        return;
                    }
                }
            }
        }
    }
}

