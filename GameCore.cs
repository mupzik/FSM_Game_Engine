using GameProj.src;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private readonly GameGrid _grid;
        private readonly GameCanvas _canvas;
        private readonly PhysicsEngine _physics;

        // Кэши
        private static readonly Dictionary<string, ImageSource> _staticSpriteCache = new Dictionary<string, ImageSource>();
        private static readonly Dictionary<string, CroppedBitmap> _staticFrameCache = new Dictionary<string, CroppedBitmap>();

        private readonly List<Character> _characters = new List<Character>();
        private readonly Dictionary<Character, UIElement> _characterVisuals = new Dictionary<Character, UIElement>();
        private readonly Dictionary<(int x, int y), Image> _itemVisuals = new Dictionary<(int x, int y), Image>();

        private Ally _ally;
        private Player _player;

        internal readonly Random _rng = new Random();
        private const int TileSize = 32;

        // FSM
        private FSM<State_, Event> _gameFSM;
        private bool _wPressed, _aPressed, _sPressed, _dPressed;

        private readonly Dictionary<UIElement, double> _animationTime = new Dictionary<UIElement, double>();

        private string _tilesPath, _spritesPath, _itemsPath;

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

        public bool HasItemsOnGround()
        {
            for (int x = 0; x < _grid.Width; x++)
                for (int y = 0; y < _grid.Height; y++)
                    if (_grid[x, y]?.ItemOnGround != null) return true;
            return false;
        }

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

        // --- УНИВЕРСАЛЬНЫЕ МЕТОДЫ ЗАГРУЗКИ (LAZY LOADING) ---

        private ImageSource GetOrCreateSprite(string filePath, string debugLabel = "")
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            if (_staticSpriteCache.TryGetValue(filePath, out ImageSource cached))
            {
                return cached;
            }

            ImageSource result = null;
            if (File.Exists(filePath))
            {
                result = LoadBitmap(filePath);
            }
            else
            {
                result = CreatePlaceholder(Colors.Gray, debugLabel);
            }

            _staticSpriteCache[filePath] = result;
            return result;
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

                    string bgPath = !string.IsNullOrEmpty(cell.BackgroundSpriteId)
                        ? Path.Combine(_tilesPath, cell.BackgroundSpriteId + ".png")
                        : null;

                    if (bgPath == null || !File.Exists(bgPath))
                    {
                        if (!string.IsNullOrEmpty(cell.BackgroundSpriteId))
                            bgPath = Path.Combine(_tilesPath, cell.BackgroundSpriteId + ".png");
                    }

                    ImageSource bgSource = GetOrCreateSprite(bgPath, "BG");

                    if (bgSource != null)
                    {
                        var bgImage = new Image { Width = TileSize, Height = TileSize, Source = bgSource };
                        Canvas.SetLeft(bgImage, x * TileSize);
                        Canvas.SetTop(bgImage, y * TileSize);
                        Canvas.SetZIndex(bgImage, 0);
                        _canvas.GameArea.Children.Add(bgImage);
                    }

                    string decorPath = !string.IsNullOrEmpty(cell.DecorSpriteId)
                        ? Path.Combine(_tilesPath, cell.DecorSpriteId + ".png")
                        : null;

                    ImageSource decorSource = GetOrCreateSprite(decorPath, "Decor");

                    if (decorSource != null)
                    {
                        var decorImage = new Image { Width = TileSize, Height = TileSize, Source = decorSource };
                        Canvas.SetLeft(decorImage, x * TileSize);
                        Canvas.SetTop(decorImage, y * TileSize);
                        Canvas.SetZIndex(decorImage, 1);
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
                    if (cell?.ItemOnGround != null)
                    {
                        var itemSource = GetOrCreateSprite(cell.ItemOnGround.IconPath, cell.ItemOnGround.Key);

                        if (itemSource != null)
                        {
                            var image = new Image { Width = 24, Height = 24, Source = itemSource, Stretch = Stretch.Uniform };
                            Canvas.SetLeft(image, x * TileSize + (TileSize - 24) / 2.0);
                            Canvas.SetTop(image, y * TileSize + (TileSize - 24) / 2.0);
                            Canvas.SetZIndex(image, 2);
                            _canvas.GameArea.Children.Add(image);
                            _itemVisuals[(x, y)] = image;
                        }
                    }
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
            if (_grid.InBounds(x, y)) _grid.PlaceItem(x, y, item);
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
            var image = new Image { Stretch = Stretch.None, RenderTransformOrigin = new Point(0.5, 0.5) };
            // Сохраняем базовое имя спрайта в Tag, чтобы использовать при отрисовке
            // Например: "MC", "Orc", "Boss"
            string baseName = "MC";
            if (ch is Ally) baseName = "Orc";
            // Если у персонажа задан SpritePath, используем его имя файла без расширения
            if (!string.IsNullOrEmpty(ch.SpritePath))
            {
                baseName = Path.GetFileNameWithoutExtension(ch.SpritePath);
            }

            image.Tag = baseName;
            return image;
        }


        public void Update()
        {
            _gameFSM?.Update();
            foreach (var ch in _characters.ToArray()) { if (ch.IsAlive) ch.Update(); }
            _physics.UpdateCollisions(_characters);

            // Подбор предметов
            foreach (var ch in _characters.ToArray())
            {
                if (!ch.IsAlive) continue;
                int cx = (int)Math.Floor(ch.Position.X / TileSize);
                int cy = (int)Math.Floor(ch.Position.Y / TileSize);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (!_grid.InBounds(x, y)) continue;
                        var cell = _grid[x, y];
                        if (cell?.ItemOnGround != null)
                        {
                            var itemPosPx = new Vector2D(x * TileSize + TileSize / 2.0, y * TileSize + TileSize / 2.0);
                            if (Vector2D.Distance(ch.Position, itemPosPx) <= 40.0)
                            {
                                var item = cell.ItemOnGround;
                                if (ch.Inventory.AddItem(item) >= 0)
                                {
                                    ch.PickupItem(item.Key);
                                    cell.ItemOnGround = null;
                                    if (_itemVisuals.TryGetValue((x, y), out Image img))
                                    {
                                        _canvas.GameArea.Children.Remove(img);
                                        _itemVisuals.Remove((x, y));
                                    }
                                    if (ch is Player) OnItemPickedUp(item.Key, false);
                                }
                            }
                        }
                    }
                }
            }

            // 5. Рендеринг персонажей
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

                // 1. Определяем базовое имя и анимацию
                string baseId = (string)visual.Tag; // Например, "MC" или "Orc"

                // Получаем суффикс направления из персонажа
                string animKeySuffix = character.GetAnimationKey(character.Velocity);

                // Если стоим, сохраняем последнее направление или дефолт
                if (string.IsNullOrEmpty(animKeySuffix))
                    animKeySuffix = "_D_Walk";

                // 2. ЛОГИКА ОТЗЕРКАЛИВАНИЯ
                // Предположение: У нас есть файлы _L_Walk (смотрит влево).
                // Если персонаж идет вправо (_R_Walk), мы берем файл _L_Walk и отзеркаливаем его.

                bool needsFlip = false;
                string finalAnimSuffix = animKeySuffix;

                if (animKeySuffix == "_R_Walk")
                {
                    finalAnimSuffix = "_L_Walk"; // Подменяем на левую анимацию
                    needsFlip = true;           // Включаем флаг отражения
                }

                // Формируем имя файла: например, "MC_L_Walk.png"
                string animFileName = $"{baseId}{finalAnimSuffix}.png";
                string fullAnimPath = Path.Combine(_spritesPath, animFileName);

                // 3. Загрузка спрайт-листа (Sheet)
                ImageSource sheetSource = GetOrCreateSprite(fullAnimPath);

                // Fallback: если файла анимации нет, пробуем статичный спрайт (например, "MC.png")
                if (sheetSource is DrawingImage || !File.Exists(fullAnimPath))
                {
                    string staticPath = Path.Combine(_spritesPath, $"{baseId}.png");
                    sheetSource = GetOrCreateSprite(staticPath);
                }

                ImageSource currentSource = null;

                if (sheetSource is BitmapSource bitmapSource)
                {
                    // Автоматический расчет кадров
                    int frameCount = bitmapSource.PixelWidth / character.FrameSize;

                    // Если это статичная картинка (1 кадр) или ширина совпадает с размером кадра
                    if (frameCount <= 1 || bitmapSource.PixelWidth == character.FrameSize)
                    {
                        currentSource = bitmapSource;
                    }
                    else
                    {
                        // Анимация
                        bool isMoving = character.Velocity.Length() > 0.001;

                        if (!_animationTime.ContainsKey(visual)) _animationTime[visual] = 0;

                        if (isMoving)
                            _animationTime[visual] += 1.0 / 60.0;
                        else
                            _animationTime[visual] = 0;

                        double durationPerFrame = 0.1;
                        int frameIndex = isMoving
                            ? (int)(_animationTime[visual] / durationPerFrame) % frameCount
                            : 0;

                        // Защита от выхода за границы
                        if (frameIndex >= frameCount) frameIndex = frameCount - 1;

                        // Ключ кэша для конкретного кадра
                        string cacheKey = $"{fullAnimPath}:{frameIndex}";

                        if (!_staticFrameCache.TryGetValue(cacheKey, out CroppedBitmap cropped))
                        {
                            try
                            {
                                int xPos = frameIndex * character.FrameSize;
                                // Создаем вырезанный кадр
                                cropped = new CroppedBitmap(bitmapSource, new Int32Rect(xPos, 0, character.FrameSize, character.FrameSize));
                                cropped.Freeze(); // Замораживаем для производительности
                                _staticFrameCache[cacheKey] = cropped;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Ошибка обрезки спрайта: {ex.Message}");
                            }
                        }
                        currentSource = cropped;
                    }
                }

                // Fallback, если вообще ничего не загрузилось
                if (currentSource == null)
                {
                    currentSource = CreatePlaceholder(Colors.Red, character.Id);
                }

                // --- ПРИМЕНЕНИЕ ИСТОЧНИКА И РАЗМЕРОВ ---
                double displaySize = character.FrameSize;
                visual.Source = currentSource;
                visual.Width = displaySize;
                visual.Height = displaySize;
                visual.Stretch = Stretch.Uniform;

                // Позиционирование
                var pos = character.Position;
                double left = pos.X - (displaySize / 2.0);
                double top = pos.Y - (displaySize / 2.0);
                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);

                // --- ОТЗЕРКАЛИВАНИЕ (ИСПРАВЛЕННОЕ) ---
                visual.RenderTransformOrigin = new Point(0.5, 0.5);

                if (needsFlip)
                {
                    // Если нужно отзеркалить (движение вправо), применяем ScaleTransform(-1, 1)
                    visual.RenderTransform = new ScaleTransform(-1, 1);
                }
                else
                {
                    // Иначе сбрасываем трансформацию в единичную (не null!)
                    visual.RenderTransform = new ScaleTransform(1, 1);
                }
            }
        }


        public bool IsWalkable(int x, int y) => _grid.IsWalkable(x, y);

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---

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

        private ImageSource CreatePlaceholder(Color color, string label = "")
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing
            {
                Brush = new SolidColorBrush(color),
                Geometry = new RectangleGeometry(new Rect(0, 0, 32, 32))
            });
            return new DrawingImage(drawing);
        }
    }





    public class PhysicsEngine
    {
        private readonly GameGrid _grid;
        private const double Epsilon = 0.001; // Небольшой отступ, чтобы не застревать точно на границе
        private const int TileSize = 32;      // Размер клетки в пикселях

        public PhysicsEngine(GameGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>
        /// Основной метод обновления физики. Разделяет движение по осям для корректного скольжения вдоль стен.
        /// </summary>
        public void UpdateCollisions(List<Character> characters)
        {
            foreach (var ch in characters)
            {
                if (!ch.IsAlive) continue;

                // 1. Движение и коллизия по оси X
                HandleAxisCollision(ch, isXAxis: true);

                // 2. Движение и коллизия по оси Y
                HandleAxisCollision(ch, isXAxis: false);

                // Если скорость очень мала, обнуляем её полностью, чтобы избежать микро-дрожания
                if (Math.Abs(ch.Velocity.X) < Epsilon) ch.Velocity.X = 0;
                if (Math.Abs(ch.Velocity.Y) < Epsilon) ch.Velocity.Y = 0;
            }
        }

        private void HandleAxisCollision(Character ch, bool isXAxis)
        {
            // Получаем текущую скорость по нужной оси
            double velocity = isXAxis ? ch.Velocity.X : ch.Velocity.Y;

            // Если движения нет, пропускаем расчеты
            if (Math.Abs(velocity) < Epsilon) return;

            // Предсказываем новую позицию
            double newPosVal = (isXAxis ? ch.Position.X : ch.Position.Y) + velocity;

            // Размеры хитбокса персонажа (предполагаем квадратный хитбокс для простоты, как в вашем коде Size)
            double size = ch.Size;
            double halfSize = size / 2.0;

            // Определяем границы персонажа (AABB) после движения по одной оси
            // Важно: по одной оси позиция меняется, по другой остается старой
            double left, right, top, bottom;

            if (isXAxis)
            {
                left = newPosVal - halfSize;
                right = newPosVal + halfSize;
                top = ch.Position.Y - halfSize;
                bottom = ch.Position.Y + halfSize;
            }
            else
            {
                left = ch.Position.X - halfSize;
                right = ch.Position.X + halfSize;
                top = newPosVal - halfSize;
                bottom = newPosVal + halfSize;
            }

            // Находим диапазон клеток сетки, которые пересекает этот прямоугольник
            int minCol = (int)Math.Floor(left / TileSize);
            int maxCol = (int)Math.Floor((right - Epsilon) / TileSize); // -Epsilon чтобы не захватить соседнюю клетку при точном совпадении
            int minRow = (int)Math.Floor(top / TileSize);
            int maxRow = (int)Math.Floor((bottom - Epsilon) / TileSize);

            bool collisionOccurred = false;
            double resolvedPos = newPosVal;

            // Перебираем все потенциально затронутые клетки
            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    // Пропускаем клетки вне карты или проходимые клетки
                    if (!_grid.InBounds(c, r) || _grid[c, r].IsWalkable())
                        continue;

                    // Получаем AABB стены (клетки) в мировых координатах
                    Rect wallRect = new Rect(c * TileSize, r * TileSize, TileSize, TileSize);

                    // Получаем AABB персонажа (используем рассчитанные выше координаты)
                    Rect charRect = new Rect(left, top, right - left, bottom - top);

                    // Проверяем пересечение (IntersectsWith)
                    if (wallRect.IntersectsWith(charRect))
                    {
                        collisionOccurred = true;

                        // РАЗРЕШЕНИЕ КОЛЛИЗИИ (Resolution)
                        if (isXAxis)
                        {
                            if (velocity > 0) // Движемся вправо -> упираемся в левую грань стены
                            {
                                resolvedPos = wallRect.Left - halfSize - Epsilon;
                            }
                            else // Движемся влево -> упираемся в правую грань стены
                            {
                                resolvedPos = wallRect.Right + halfSize + Epsilon;
                            }
                        }
                        else // Ось Y
                        {
                            if (velocity > 0) // Движемся вниз -> упираемся в верхнюю грань стены
                            {
                                resolvedPos = wallRect.Top - halfSize - Epsilon;
                            }
                            else // Движемся вверх -> упираемся в нижнюю грань стены
                            {
                                resolvedPos = wallRect.Bottom + halfSize + Epsilon;
                            }
                        }

                    }
                }
            }

            // Применяем результаты
            if (collisionOccurred)
            {
                if (isXAxis)
                {
                    ch.Position.X = resolvedPos;
                    ch.Velocity.X = 0; // Останавливаем движение по X
                }
                else
                {
                    ch.Position.Y = resolvedPos;
                    ch.Velocity.Y = 0; // Останавливаем движение по Y
                }
            }
            else
            {
                // Если коллизий нет, применяем новую позицию
                if (isXAxis)
                    ch.Position.X = newPosVal;
                else
                    ch.Position.Y = newPosVal;
            }
        }
    }
}