using GameProj.src;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;

namespace GameProj
{
    // Крупный декоративный объект (занимает несколько клеток)
    public class LargeDecor
    {
        public int X { get; set; }          // Позиция по X в клетках
        public int Y { get; set; }          // Позиция по Y в клетках
        public string SpriteId { get; set; } // ID спрайта
        public int Width { get; set; }       // Ширина в клетках
        public int Height { get; set; }      // Высота в клетках
        public TileType Type { get; set; }   // Тип тайла (стена/пол)
    }

    // Главный управляющий класс игры
    public class GameManager
    {
        // События окончания игры и победы
        public event Action OnGameOver;
        public event Action OnWin;

        // Список крупных декоративных объектов
        private readonly List<LargeDecor> _largeDecors = new List<LargeDecor>();

        // Общие события для конечного автомата
        public enum CommonEvent { Exit, Restart }

        // Константы размеров и физики
        private const int TileSize = 32;           // Размер клетки в пикселях
        private const double Epsilon = 0.001;       // Маленькое число для сравнения
        private const double AnimationFrameTime = 0.1; // Время кадра анимации
        private const int ItemIconSize = 24;        // Размер иконки предмета
        private const double DefaultFrameDuration = 1.0 / 60.0; // 60 FPS

        private static readonly Color PlaceholderBackground = Colors.Gray; // Цвет заглушки

        // Основные компоненты
        private readonly GameGrid _grid;           // Сетка мира
        private readonly GameCanvas _canvas;       // Холст для отрисовки
        private readonly PhysicsEngine _physics;    // Движок физики

        // Персонажи и их визуальные представления
        private readonly List<Character> _characters = new List<Character>();
        private readonly Dictionary<Character, UIElement> _characterVisuals = new Dictionary<Character, UIElement>();

        // Визуальные представления предметов на земле
        private readonly Dictionary<(int x, int y), Image> _itemVisuals = new Dictionary<(int x, int y), Image>();

        // Предметы на земле
        private readonly List<GroundItem> _groundItems = new List<GroundItem>();

        // Список врагов
        private readonly List<Enemy> _enemies = new List<Enemy>();

        // Генератор случайных чисел
        internal readonly Random _rng = new Random();

        // Конечный автомат 
        private FSM<object, object> _gameFSM;

        // Состояния игры
        private CompositeState<object, object> _tutorialState; // Обучение
        private CompositeState<object, object> _gameState;     // Основная игра
        private CompositeState<object, object> _endState;      // Конец игры

        // Время анимации для персонажей
        private readonly Dictionary<UIElement, double> _animationTime = new Dictionary<UIElement, double>();

        // Пути к ресурсам
        private readonly string _tilesPath, _spritesPath, _itemsPath;

        // Публичные свойства
        public IReadOnlyList<Character> Characters => _characters;
        public GameGrid Grid => _grid;
        public FSM<object, object> FSM => _gameFSM;

        // Конструктор
        public GameManager(GameCanvas canvas, int width, int height, Action<GameManager> mapInitializer = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _grid = new GameGrid(width, height, TileSize);

            // Определяем пути к папкам с ресурсами
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _tilesPath = Path.Combine(baseDir, "Tiles");
            _spritesPath = Path.Combine(baseDir, "Sprites");
            _itemsPath = Path.Combine(baseDir, "Items");

            _physics = new PhysicsEngine(_grid);

            // Инициализация карты, если передана
            mapInitializer?.Invoke(this);

            // Рисуем статическую карту, декор и предметы
            DrawStaticMap();
            DrawLargeDecors();
            DrawItems();
        }

        /// <summary>
        /// Инициализирует конечный автомат с тремя состояниями
        /// </summary>
        public void InitializeFSM(
            object tutorialId,
            object gameId,
            object endId,
            Action<CompositeState<object, object>> tutorialInitializer = null,
            Action<CompositeState<object, object>> gameInitializer = null,
            Action<CompositeState<object, object>> endInitializer = null)
        {
            // Создаем состояния
            _tutorialState = new CompositeState<object, object>(tutorialId, tutorialId);
            _gameState = new CompositeState<object, object>(gameId, gameId);
            _endState = new CompositeState<object, object>(endId, endId);

            // Инициализируем состояния, если переданы инициализаторы
            tutorialInitializer?.Invoke(_tutorialState);
            gameInitializer?.Invoke(_gameState);
            endInitializer?.Invoke(_endState);

            // Настраиваем обработчики событий для каждого состояния
            _tutorialState.SetEventHandler((machine, ev) =>
            {
                if (ev is CommonEvent commonEv)
                {
                    if (commonEv == CommonEvent.Exit)
                        Application.Current?.Shutdown();
                }
            });

            _gameState.SetEventHandler((machine, ev) =>
            {
                if (ev is CommonEvent commonEv)
                {
                    if (commonEv == CommonEvent.Exit)
                        Application.Current?.Shutdown();
                }
            });

            _endState.SetEventHandler((machine, ev) =>
            {
                if (ev is CommonEvent commonEv)
                {
                    if (commonEv == CommonEvent.Restart)
                        machine.SetState(_tutorialState); // Рестарт - в обучение
                    else if (commonEv == CommonEvent.Exit)
                        Application.Current?.Shutdown();
                }
            });

            // Запускаем FSM с состояния обучения
            _gameFSM = new FSM<object, object>(_tutorialState);
        }

        /// <summary>
        /// Получает композитное состояние по его ID
        /// </summary>
        public CompositeState<object, object> GetCompositeState(object stateId)
        {
            if (_tutorialState != null && _tutorialState.Id.Equals(stateId))
                return _tutorialState;
            if (_gameState != null && _gameState.Id.Equals(stateId))
                return _gameState;
            if (_endState != null && _endState.Id.Equals(stateId))
                return _endState;
            return null;
        }

        /// <summary>
        /// Переключает FSM на указанное состояние
        /// </summary>
        public void SwitchToState(object stateId)
        {
            var targetState = GetCompositeState(stateId);
            if (targetState != null)
            {
                _gameFSM.SetState(targetState);
            }
        }

        /// <summary>
        /// Отправляет событие в конечный автомат
        /// </summary>
        public void SendEvent(object ev)
        {
            _gameFSM?.HandleEvent(ev);
        }

        // Вспомогательные методы
        public void ShakeCamera() { _canvas.TriggerShake(); } // Тряска камеры
        public bool HasItemsOnGround() => _groundItems.Count > 0; // Есть ли предметы на земле
        public void OnItemPickedUp(string itemId, bool byAlly = false) { } // Обработка поднятия предмета

        /// <summary>
        /// Загружает изображение из файла без кэширования
        /// </summary>
        private BitmapImage LoadBitmap(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad; // Важно: загружаем в память сразу, чтобы разблокировать файл
                bmp.EndInit();
                bmp.Freeze(); // Делаем доступным для всех потоков
                return bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки изображения {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Создает изображение-заглушку указанного цвета
        /// </summary>
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

        /// <summary>
        /// Рисует статическую карту (фон и малый декор)
        /// </summary>
        private void DrawStaticMap()
        {
            // Удаляем старые изображения фона и декора
            var toRemove = _canvas.GameArea.Children
                .OfType<Image>()
                .Where(img => Canvas.GetZIndex(img) == 0 || Canvas.GetZIndex(img) == 1)
                .ToList();

            foreach (var img in toRemove)
            {
                _canvas.GameArea.Children.Remove(img);
            }

            // Проходим по всем клеткам
            for (int x = 0; x < _grid.Width; x++)
            {
                for (int y = 0; y < _grid.Height; y++)
                {
                    var cell = _grid[x, y];

                    // Рисуем фон
                    if (!string.IsNullOrEmpty(cell.BackgroundSpriteId))
                    {
                        string bgPath = Path.Combine(_tilesPath, cell.BackgroundSpriteId + ".png");
                        var bgSource = LoadBitmap(bgPath);
                        if (bgSource != null)
                        {
                            var bgImage = new Image
                            {
                                Width = TileSize + 1,
                                Height = TileSize + 1,
                                Source = bgSource,
                                Stretch = Stretch.UniformToFill
                            };
                            Canvas.SetLeft(bgImage, x * TileSize - 0.5);
                            Canvas.SetTop(bgImage, y * TileSize - 0.5);
                            Canvas.SetZIndex(bgImage, 0); // Нижний слой
                            _canvas.GameArea.Children.Add(bgImage);
                        }
                    }

                    // Рисуем малый декор
                    if (!string.IsNullOrEmpty(cell.DecorSpriteId))
                    {
                        string decorPath = Path.Combine(_tilesPath, cell.DecorSpriteId + ".png");
                        var decorSource = LoadBitmap(decorPath);
                        if (decorSource != null)
                        {
                            var decorImage = new Image
                            {
                                Width = TileSize + 1,
                                Height = TileSize + 1,
                                Source = decorSource,
                                Stretch = Stretch.UniformToFill
                            };
                            Canvas.SetLeft(decorImage, x * TileSize - 0.5);
                            Canvas.SetTop(decorImage, y * TileSize - 0.5);
                            Canvas.SetZIndex(decorImage, 1); // Средний слой
                            _canvas.GameArea.Children.Add(decorImage);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Рисует крупные декоративные объекты
        /// </summary>
        private void DrawLargeDecors()
        {
            // Удаляем старые изображения крупного декора
            var oldLargeDecors = _canvas.GameArea.Children
                .OfType<Image>()
                .Where(img => Canvas.GetZIndex(img) == 5)
                .ToList();

            foreach (var img in oldLargeDecors)
            {
                _canvas.GameArea.Children.Remove(img);
            }

            // Рисуем каждый декоративный объект
            foreach (var decor in _largeDecors)
            {
                string decorPath = Path.Combine(_tilesPath, decor.SpriteId + ".png");
                var decorSource = LoadBitmap(decorPath);

                if (decorSource != null)
                {
                    var image = new Image
                    {
                        Width = decor.Width * TileSize,
                        Height = decor.Height * TileSize,
                        Source = decorSource,
                        Stretch = Stretch.Fill
                    };

                    Canvas.SetLeft(image, decor.X * TileSize);
                    Canvas.SetTop(image, decor.Y * TileSize);
                    Canvas.SetZIndex(image, 5); // Высокий слой
                    _canvas.GameArea.Children.Add(image);
                }
            }
        }

        /// <summary>
        /// Рисует предметы на земле
        /// </summary>
        private void DrawItems()
        {
            // Удаляем старые изображения предметов
            foreach (var img in _itemVisuals.Values)
            {
                _canvas.GameArea.Children.Remove(img);
            }
            _itemVisuals.Clear();

            // Рисуем каждый предмет
            foreach (var groundItem in _groundItems)
            {
                var itemSource = LoadBitmap(groundItem.Item.IconPath);
                if (itemSource != null)
                {
                    var image = new Image
                    {
                        Width = ItemIconSize,
                        Height = ItemIconSize,
                        Source = itemSource,
                        Stretch = Stretch.Uniform
                    };

                    // Центрируем иконку в клетке
                    double offset = (TileSize - ItemIconSize) / 2.0;
                    Canvas.SetLeft(image, groundItem.X * TileSize + offset);
                    Canvas.SetTop(image, groundItem.Y * TileSize + offset);
                    Canvas.SetZIndex(image, 2);
                    _canvas.GameArea.Children.Add(image);
                    _itemVisuals[(groundItem.X, groundItem.Y)] = image;
                }
            }
        }

        /// <summary>
        /// Устанавливает тип и спрайт клетки
        /// </summary>
        public void SetTile(int x, int y, TileType type, string spriteId, string decorSpriteId = null)
        {
            if (!_grid.InBounds(x, y)) return;
            _grid.UpdateCell(x, y, type, backgroundSpriteId: spriteId, decorSpriteId: decorSpriteId);
        }

        /// <summary>
        /// Размещает предмет на земле
        /// </summary>
        public void PlaceItem(int x, int y, Item item)
        {
            if (!_grid.InBounds(x, y)) return;
            _grid.PlaceItem(x, y, item);
            _groundItems.Add(new GroundItem(x, y, item));
            DrawItems(); // Обновляем отображение
        }

        /// <summary>
        /// Добавляет персонажа в игру
        /// </summary>
        public void AddCharacter(Character character)
        {
            if (character == null) return;
            _characters.Add(character);
            var visual = CreateCharacterVisual(character);
            _characterVisuals[character] = visual;
            Canvas.SetZIndex(visual, 10); // Самый высокий слой
            _canvas.GameArea.Children.Add(visual);
        }

        /// <summary>
        /// Создает визуальное представление для персонажа
        /// </summary>
        private UIElement CreateCharacterVisual(Character ch)
        {
            var image = new Image
            {
                Stretch = Stretch.None,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            string baseName;

            // Определяем базовое имя для спрайтов
            if (!string.IsNullOrEmpty(ch.SpritePath))
            {
                baseName = Path.GetFileNameWithoutExtension(ch.SpritePath);
            }
            else if (!string.IsNullOrEmpty(ch.Id))
            {
                baseName = ch.Id;
            }
            else
            {
                baseName = "Unknown";
            }

            image.Tag = baseName; // Сохраняем для использования в рендере
            return image;
        }

        /// <summary>
        /// Обновляет состояние игры (вызывается каждый кадр)
        /// </summary>
        public void Update()
        {
            _gameFSM?.Update(); // Обновляем FSM

            // Обновляем всех живых персонажей
            foreach (var ch in _characters.ToArray())
            {
                if (ch.IsAlive)
                    ch.Update();
            }

            // Обрабатываем столкновения
            _physics.UpdateCollisions(_characters);

            // Перерисовываем персонажей
            RenderCharacters();
        }

        /// <summary>
        /// Удаляет предмет с указанной клетки
        /// </summary>
        public void RemoveItemFromGround(int x, int y)
        {
            if (!_grid.InBounds(x, y)) return;

            _grid[x, y].ItemOnGround = null;

            // Удаляем из списка предметов на земле
            var groundItem = _groundItems.FirstOrDefault(gi => gi.X == x && gi.Y == y);
            if (groundItem != null)
            {
                _groundItems.Remove(groundItem);
            }

            // Удаляем визуальное представление
            if (_itemVisuals.TryGetValue((x, y), out Image img))
            {
                _canvas.GameArea.Children.Remove(img);
                _itemVisuals.Remove((x, y));
            }
        }

        /// <summary>
        /// Отрисовывает всех персонажей с анимацией и учетом визуального масштаба
        /// </summary>
        private void RenderCharacters()
        {
            foreach (var kvp in _characterVisuals)
            {
                var character = kvp.Key;
                if (!(kvp.Value is Image visual)) continue;

                // Скрываем мертвых персонажей
                if (!character.IsAlive)
                {
                    visual.Visibility = Visibility.Collapsed;
                    continue;
                }

                visual.Visibility = Visibility.Visible;

                string baseId = (string)visual.Tag;
                string animKeySuffix = character.GetAnimationKey(character.Velocity);

                if (string.IsNullOrEmpty(animKeySuffix))
                    animKeySuffix = "_D_Walk"; // Анимация по умолчанию

                // Обработка отражения для движения влево
                bool needsFlip = false;
                string finalAnimSuffix = animKeySuffix;

                if (animKeySuffix == "_L_Walk")
                {
                    finalAnimSuffix = "_R_Walk"; // Используем правую анимацию
                    needsFlip = true;            // И отражаем её
                }

                string animFileName = $"{baseId}{finalAnimSuffix}.png";
                string fullAnimPath = Path.Combine(_spritesPath, animFileName);

                BitmapSource sheetSource = null;

                // Пробуем загрузить анимацию
                if (File.Exists(fullAnimPath))
                {
                    sheetSource = LoadBitmap(fullAnimPath);
                }

                // Если анимации нет, пробуем статичный спрайт
                if (sheetSource == null)
                {
                    string staticPath = Path.Combine(_spritesPath, $"{baseId}.png");
                    if (File.Exists(staticPath))
                    {
                        sheetSource = LoadBitmap(staticPath);
                    }
                }

                // Если ничего нет - создаем заглушку
                if (sheetSource == null)
                {
                    int size = character.FrameSize;
                    var drawingVisual = new DrawingVisual();
                    using (var context = drawingVisual.RenderOpen())
                    {
                        context.DrawRectangle(new SolidColorBrush(Colors.Magenta), null, new Rect(0, 0, size, size));
                    }
                    var renderTarget = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                    renderTarget.Render(drawingVisual);
                    renderTarget.Freeze();
                    sheetSource = renderTarget;
                }

                ImageSource currentSource = null;

                if (sheetSource != null)
                {
                    // Если размеры некорректны - берем весь спрайт
                    if (character.FrameSize <= 0 || sheetSource.PixelWidth < character.FrameSize)
                    {
                        currentSource = sheetSource;
                    }
                    else
                    {
                        int frameCount = sheetSource.PixelWidth / character.FrameSize;

                        // Одиночный спрайт без анимации
                        if (frameCount <= 1 || sheetSource.PixelWidth == character.FrameSize)
                        {
                            currentSource = sheetSource;
                        }
                        else
                        {
                            bool isMoving = character.Velocity.Length() > Epsilon;

                            // Управление временем анимации
                            if (!_animationTime.ContainsKey(visual))
                                _animationTime[visual] = 0;

                            if (isMoving)
                                _animationTime[visual] += DefaultFrameDuration;
                            else
                                _animationTime[visual] = 0;

                            // Вычисляем текущий кадр
                            int frameIndex = isMoving
                                ? (int)(_animationTime[visual] / AnimationFrameTime) % frameCount
                                : 0;

                            if (frameIndex >= frameCount)
                                frameIndex = frameCount - 1;

                            try
                            {
                                // Вырезаем кадр напрямую без кэша
                                int xPos = frameIndex * character.FrameSize;
                                currentSource = new CroppedBitmap(sheetSource, new Int32Rect(xPos, 0, character.FrameSize, character.FrameSize));
                                ((CroppedBitmap)currentSource).Freeze();
                            }
                            catch
                            {
                                currentSource = sheetSource;
                            }
                        }
                    }
                }

                if (currentSource == null)
                {
                    currentSource = CreatePlaceholder(Colors.Red);
                }

                // --- ИЗМЕНЕНИЯ ЗДЕСЬ ---

                // Базовый размер кадра
                double baseDisplaySize = character.FrameSize;

                // Применяем визуальный масштаб
                double scaledWidth = baseDisplaySize * character.VisualScale;
                double scaledHeight = baseDisplaySize * character.VisualScale;

                visual.Source = currentSource;
                visual.Width = scaledWidth;
                visual.Height = scaledHeight;
                visual.Stretch = Stretch.Uniform;

                // Позиционируем так, чтобы центр персонажа совпадал с character.Position
                double left = character.Position.X - (scaledWidth / 2.0);
                double top = character.Position.Y - (scaledHeight / 2.0);

                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);

                // Применяем трансформации: Отражение (если нужно) и Масштаб
                // ScaleTransform(X, Y): 
                // X = -1 * VisualScale если нужно отразить, иначе 1 * VisualScale
                // Y = VisualScale
                double scaleX = needsFlip ? -character.VisualScale : character.VisualScale;
                double scaleY = character.VisualScale;

                visual.RenderTransform = new ScaleTransform(scaleX, scaleY);
            }
        }

        /// <summary>
        /// Проверяет, можно ли пройти через клетку
        /// </summary>
        public bool IsWalkable(int x, int y) => _grid.IsWalkable(x, y);

        /// <summary>
        /// Загружает карту из текстовых файлов
        /// </summary>
        public void LoadMap(string backgroundPath,
                            string largeDecorPath,
                            Dictionary<char, (TileType type, string spriteId)> backgroundMappings,
                            Dictionary<char, (TileType type, string spriteId, int width, int height)> largeDecorMappings,
                            TileType defaultTileType = TileType.Floor)
        {
            // Проверяем наличие файла фона
            if (!File.Exists(backgroundPath))
                throw new FileNotFoundException($"Файл фоновой карты не найден: {backgroundPath}");

            // Читаем карту фона
            var bgLines = File.ReadAllLines(backgroundPath);
            int mapHeight = bgLines.Length;
            int mapWidth = bgLines[0].Length;

            // Проверяем, что все строки одинаковой длины
            for (int i = 0; i < bgLines.Length; i++)
            {
                if (bgLines[i].Length != mapWidth)
                    throw new InvalidOperationException($"Строка {i + 1} фоновой карты имеет длину {bgLines[i].Length}, ожидается {mapWidth}");
            }

            // Читаем карту крупного декора, если указана
            string[] largeDecorLines = null;
            if (!string.IsNullOrEmpty(largeDecorPath) && File.Exists(largeDecorPath))
            {
                largeDecorLines = File.ReadAllLines(largeDecorPath);
                if (largeDecorLines.Length != mapHeight)
                    throw new InvalidOperationException($"Высота крупного декора ({largeDecorLines.Length}) не совпадает с фоном ({mapHeight})");
                for (int i = 0; i < largeDecorLines.Length; i++)
                {
                    if (largeDecorLines[i].Length != mapWidth)
                        throw new InvalidOperationException($"Строка {i + 1} крупного декора имеет длину {largeDecorLines[i].Length}, ожидается {mapWidth}");
                }
            }

            // Очищаем текущий список крупного декора
            _largeDecors.Clear();

            // Загружаем фон
            for (int y = 0; y < Math.Min(mapHeight, _grid.Height); y++)
            {
                string bgLine = bgLines[y];
                for (int x = 0; x < Math.Min(mapWidth, _grid.Width); x++)
                {
                    char bgSymbol = bgLine[x];
                    if (char.IsWhiteSpace(bgSymbol)) continue;

                    if (backgroundMappings.TryGetValue(bgSymbol, out var bgMapping))
                    {
                        _grid.UpdateCell(x, y, bgMapping.type, bgMapping.spriteId);
                    }
                    else
                    {
                        _grid.UpdateCell(x, y, defaultTileType, null);
                    }
                }
            }

            // Загружаем крупный декор
            if (largeDecorLines != null && largeDecorMappings != null)
            {
                for (int y = 0; y < Math.Min(mapHeight, _grid.Height); y++)
                {
                    string line = largeDecorLines[y];
                    for (int x = 0; x < Math.Min(mapWidth, _grid.Width); x++)
                    {
                        if (x >= line.Length) continue;
                        char symbol = line[x];
                        if (char.IsWhiteSpace(symbol)) continue;

                        if (largeDecorMappings.TryGetValue(symbol, out var decor))
                        {
                            // Добавляем крупный декор
                            _largeDecors.Add(new LargeDecor
                            {
                                X = x,
                                Y = y,
                                SpriteId = decor.spriteId,
                                Width = decor.width,
                                Height = decor.height,
                                Type = decor.type
                            });

                            // Делаем все клетки декора непроходимыми
                            for (int dy = 0; dy < decor.height; dy++)
                            {
                                for (int dx = 0; dx < decor.width; dx++)
                                {
                                    int newX = x + dx;
                                    int newY = y + dy;
                                    if (_grid.InBounds(newX, newY))
                                    {
                                        _grid.UpdateCell(newX, newY, decor.type, null);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Перерисовываем всё
            DrawStaticMap();
            DrawLargeDecors();
            DrawItems();
        }

        // Обновление визуальных эффектов предметов
        public void RefreshItemsVisuals()
        {
            DrawItems();
        }

        // Полное обновление карты
        public void RefreshMap()
        {
            DrawStaticMap();
            DrawLargeDecors();
            DrawItems();
        }

        // Внутренний класс для хранения предметов на земле
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

    /// <summary>
    /// Движок физики для обработки столкновений
    /// </summary>
    public class PhysicsEngine
    {
        private const int TileSize = 32;
        private const double Epsilon = 0.001;

        private readonly GameGrid _grid;

        public PhysicsEngine(GameGrid grid)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        /// <summary>
        /// Обновляет столкновения для всех персонажей
        /// </summary>
        public void UpdateCollisions(List<Character> characters)
        {
            foreach (var ch in characters.Where(c => c.IsAlive))
            {
                // Исправляем застревание в стенах
                CorrectStuckPosition(ch);

                Vector2D oldPos = ch.Position;
                Vector2D newPos = oldPos + ch.Velocity;

                // Пытаемся разрешить коллизию
                if (TryResolveCollision(ch, oldPos, newPos, out Vector2D resolvedPos))
                {
                    ch.Position = resolvedPos;

                    // Если движение заблокировано, обнуляем скорость по соответствующей оси
                    if (Math.Abs(ch.Position.X - oldPos.X) < Epsilon) ch.Velocity.X = 0;
                    if (Math.Abs(ch.Position.Y - oldPos.Y) < Epsilon) ch.Velocity.Y = 0;
                }
                else
                {
                    ch.Position = newPos;
                }

                // Очищаем очень маленькие скорости
                if (Math.Abs(ch.Velocity.X) < Epsilon) ch.Velocity.X = 0;
                if (Math.Abs(ch.Velocity.Y) < Epsilon) ch.Velocity.Y = 0;
            }
        }

        /// <summary>
        /// Пытается разрешить коллизию, возвращает true если коллизия была
        /// </summary>
        private bool TryResolveCollision(Character ch, Vector2D oldPos, Vector2D newPos, out Vector2D resolvedPos)
        {
            // Если движения нет или новая позиция не вызывает коллизию
            if ((newPos - oldPos).Length() < Epsilon || !CheckCollisionAtPosition(ch, newPos, out _))
            {
                resolvedPos = newPos;
                return false;
            }

            // Ищем позицию перед столкновением бинарным поиском
            resolvedPos = BinarySearchCollision(ch, oldPos, newPos);
            return true;
        }

        /// <summary>
        /// Бинарный поиск позиции перед столкновением
        /// </summary>
        private Vector2D BinarySearchCollision(Character ch, Vector2D start, Vector2D end, int depth = 8)
        {
            if (depth <= 0) return start;

            Vector2D mid = (start + end) / 2;

            bool midCollides = CheckCollisionAtPosition(ch, mid, out _);
            bool endCollides = CheckCollisionAtPosition(ch, end, out _);

            if (midCollides)
                return BinarySearchCollision(ch, start, mid, depth - 1);
            else if (endCollides)
                return BinarySearchCollision(ch, mid, end, depth - 1);
            else
                return end;
        }

        /// <summary>
        /// Проверяет, есть ли коллизия в указанной позиции
        /// </summary>
        private bool CheckCollisionAtPosition(Character ch, Vector2D position, out Vector2D pushVector)
        {
            double halfSize = ch.Size / 2.0;
            Rect charRect = new Rect(
                position.X - halfSize,
                position.Y - halfSize,
                ch.Size,
                ch.Size
            );

            // Получаем все стены, пересекающиеся с персонажем
            var intersectingWalls = GetIntersectingWalls(charRect);

            if (!intersectingWalls.Any())
            {
                pushVector = Vector2D.Zero;
                return false;
            }

            // Вычисляем вектор выталкивания
            pushVector = CalculatePushVector(charRect, intersectingWalls);
            return true;
        }

        /// <summary>
        /// Получает все стены, пересекающиеся с прямоугольником
        /// </summary>
        private List<Rect> GetIntersectingWalls(Rect charRect)
        {
            // Определяем диапазон клеток, которые пересекаются с прямоугольником
            int minCol = (int)Math.Floor(charRect.Left / TileSize);
            int maxCol = (int)Math.Floor((charRect.Right - Epsilon) / TileSize);
            int minRow = (int)Math.Floor(charRect.Top / TileSize);
            int maxRow = (int)Math.Floor((charRect.Bottom - Epsilon) / TileSize);

            var walls = new List<Rect>();

            for (int c = minCol; c <= maxCol; c++)
            {
                for (int r = minRow; r <= maxRow; r++)
                {
                    if (_grid.InBounds(c, r) && !_grid[c, r].IsWalkable())
                    {
                        walls.Add(new Rect(c * TileSize, r * TileSize, TileSize, TileSize));
                    }
                }
            }

            return walls;
        }

        /// <summary>
        /// Вычисляет вектор выталкивания для разрешения коллизии
        /// </summary>
        private Vector2D CalculatePushVector(Rect charRect, List<Rect> walls)
        {
            Vector2D push = Vector2D.Zero;

            foreach (var wall in walls)
            {
                // Вычисляем перекрытие с каждой стороной стены
                double overlapLeft = charRect.Right - wall.Left;
                double overlapRight = wall.Right - charRect.Left;
                double overlapTop = charRect.Bottom - wall.Top;
                double overlapBottom = wall.Bottom - charRect.Top;

                // Находим минимальное перекрытие
                double minOverlap = Math.Min(Math.Min(overlapLeft, overlapRight),
                                           Math.Min(overlapTop, overlapBottom));

                // Выталкиваем в направлении минимального перекрытия
                if (Math.Abs(minOverlap - overlapLeft) < Epsilon)
                    push.X -= overlapLeft + Epsilon;
                else if (Math.Abs(minOverlap - overlapRight) < Epsilon)
                    push.X += overlapRight + Epsilon;
                else if (Math.Abs(minOverlap - overlapTop) < Epsilon)
                    push.Y -= overlapTop + Epsilon;
                else
                    push.Y += overlapBottom + Epsilon;
            }

            return push;
        }

        /// <summary>
        /// Исправляет застревание персонажа в стенах
        /// </summary>
        private void CorrectStuckPosition(Character ch)
        {
            double halfSize = ch.Size / 2.0;
            Rect charRect = new Rect(
                ch.Position.X - halfSize,
                ch.Position.Y - halfSize,
                ch.Size,
                ch.Size
            );

            var walls = GetIntersectingWalls(charRect);

            if (walls.Any())
            {
                Vector2D push = CalculatePushVector(charRect, walls);
                ch.Position += push;
            }
        }
    }
}