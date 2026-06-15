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
using MediaPlayer = System.Windows.Media.MediaPlayer;
using Path = System.IO.Path;

namespace GameProj
{
    public class LargeDecor
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string SpriteId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public TileType Type { get; set; }
    }

    public class GameManager
    {
        public event Action OnGameOver;
        public event Action OnWin;

        private readonly List<LargeDecor> _largeDecors = new List<LargeDecor>();

        public enum CommonEvent { Exit, Restart }

        private const int TileSize = 32;
        private const double Epsilon = 0.001;
        private const double AnimationFrameTime = 0.1;
        private const int ItemIconSize = 24;
        private const double DefaultFrameDuration = 1.0 / 120.0;

        private readonly GameGrid _grid;
        private readonly GameCanvas _canvas;
        private readonly PhysicsEngine _physics;

        private readonly List<Character> _characters = new List<Character>();
        private readonly Dictionary<Character, UIElement> _characterVisuals = new Dictionary<Character, UIElement>();
        private readonly Dictionary<(int x, int y), Image> _itemVisuals = new Dictionary<(int x, int y), Image>();
        private readonly List<GroundItem> _groundItems = new List<GroundItem>();

        internal readonly Random _rng = new Random();

        // Настройки звука
        private double _musicVolume = 0.5;
        private double _sfxVolume = 0.7;
        private MediaPlayer _musicPlayer;
        private readonly List<MediaPlayer> _activeSounds = new List<MediaPlayer>();

        private FSM<State_, Event_> _gameFSM;
        public CompositeState<State_, Event_> _tutorialState;
        public CompositeState<State_, Event_> _gameState;
        public CompositeState<State_, Event_> _endState;

        private readonly Dictionary<UIElement, double> _animationTime = new Dictionary<UIElement, double>();

        private readonly string _tilesPath, _spritesPath, _itemsPath, _soundsPath, _musicPath;

        public IReadOnlyList<Character> Characters => _characters;
        public GameGrid Grid => _grid;
        public FSM<State_, Event_> FSM => _gameFSM;

        // Свойства для доступа к настройкам звука
        public double MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = Math.Max(0, Math.Min(1, value));
                if (_musicPlayer != null)
                    _musicPlayer.Volume = _musicVolume;
            }
        }

        public double SFXVolume
        {
            get => _sfxVolume;
            set => _sfxVolume = Math.Max(0, Math.Min(1, value));
        }

        public GameManager(
            GameCanvas canvas,
            int width,
            int height,
            Action<GameManager> mapInitializer = null,
            bool enableSliding = true)
        {
            // Проверка параметров
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

            // Инициализация игровой сетки
            _grid = new GameGrid(width, height, TileSize);

            // Определение путей к ресурсам
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppDomain.CurrentDomain.BaseDirectory;

            _tilesPath = Path.Combine(baseDir, "Tiles");
            _spritesPath = Path.Combine(baseDir, "Sprites");
            _itemsPath = Path.Combine(baseDir, "Items");
            _soundsPath = Path.Combine(baseDir, "Sounds");
            _musicPath = Path.Combine(baseDir, "Music");

            // Создание директорий для ресурсов
            try
            {
                Directory.CreateDirectory(_soundsPath);
                Directory.CreateDirectory(_musicPath);
                Directory.CreateDirectory(_tilesPath);
                Directory.CreateDirectory(_spritesPath);
                Directory.CreateDirectory(_itemsPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка создания директорий: {ex.Message}");
            }

            // Инициализация физического движка с настройкой скольжения
            _physics = new PhysicsEngine(_grid, enableSliding);

            // Инициализация генератора случайных чисел
            _rng = new Random();

            // Инициализация музыкального плеера
            _musicPlayer = null;

            // Вызов пользовательской инициализации карты
            mapInitializer?.Invoke(this);

            // Отрисовка начального состояния
            DrawStaticMap();
            DrawLargeDecors();
            DrawItems();
        }

        public void PlayMusic(string fileName)
        {
            string fullPath = Path.Combine(_musicPath, fileName);
            if (!File.Exists(fullPath)) return;

            if (_musicPlayer != null)
            {
                _musicPlayer.Stop();
                _musicPlayer.Close();
            }

            _musicPlayer = new MediaPlayer();
            _musicPlayer.Open(new Uri(fullPath, UriKind.Absolute));
            _musicPlayer.MediaEnded += (s, e) => _musicPlayer.Position = TimeSpan.Zero;
            _musicPlayer.Volume = _musicVolume;
            _musicPlayer.Play();
        }

        public void StopMusic()
        {
            if (_musicPlayer != null)
            {
                _musicPlayer.Stop();
                _musicPlayer.Close();
                _musicPlayer = null;
            }
        }

        public void PlaySound(string fileName, float volumeScale = 1.0f)
        {
            string fullPath = Path.Combine(_soundsPath, fileName);
            if (!File.Exists(fullPath)) return;

            var player = new MediaPlayer();

            // Сохраняем ссылку, чтобы player не уничтожился до окончания воспроизведения
            _activeSounds.Add(player);

            player.MediaOpened += (s, e) =>
            {
                player.Volume = Math.Max(0, Math.Min(1, _sfxVolume * volumeScale));
                player.Play();
            };

            player.MediaEnded += (s, e) =>
            {
                player.Stop();
                player.Close();
                _activeSounds.Remove(player);
            };

            player.MediaFailed += (s, e) =>
            {
                _activeSounds.Remove(player);
            };

            player.Open(new Uri(fullPath, UriKind.Absolute));
        }

        /// <summary>
        /// Установить громкость музыки (0.0 - 1.0)
        /// </summary>
        public void SetMusicVolume(double volume)
        {
            MusicVolume = volume;
        }

        /// <summary>
        /// Установить громкость звуковых эффектов (0.0 - 1.0)
        /// </summary>
        public void SetSFXVolume(double volume)
        {
            SFXVolume = volume;
        }

        /// <summary>
        /// Получить текущую громкость музыки
        /// </summary>
        public double GetMusicVolume()
        {
            return _musicVolume;
        }

        /// <summary>
        /// Получить текущую громкость звуковых эффектов
        /// </summary>
        public double GetSFXVolume()
        {
            return _sfxVolume;
        }

        /// <summary>
        /// Включить/выключить звук музыки
        /// </summary>
        public void ToggleMusicMute()
        {
            if (_musicPlayer != null)
            {
                if (_musicPlayer.Volume > 0)
                    _musicPlayer.Volume = 0;
                else
                    _musicPlayer.Volume = _musicVolume;
            }
        }

        /// <summary>
        /// Переворачивает спрайт персонажа горизонтально
        /// </summary>
        /// <param name="character">Персонаж для переворота</param>
        /// <param name="flipLeft">true - перевернуть влево, false - вернуть в нормальное положение (вправо)</param>
        public void FlipHorizontally(Character character, bool flipLeft)
        {
            if (character == null) return;

            if (_characterVisuals.TryGetValue(character, out UIElement visual) && visual is Image image)
            {
                double scaleX = flipLeft ? -character.VisualScale : character.VisualScale;
                image.RenderTransform = new ScaleTransform(scaleX, character.VisualScale);
            }
        }


        /// <summary>
        /// Получить композитное состояние по ID
        /// </summary>
        public CompositeState<State_, Event_> GetCompositeState(State_ stateId)
        {
            if (_tutorialState != null && _tutorialState.Id.Equals(stateId))
                return _tutorialState;
            if (_gameState != null && _gameState.Id.Equals(stateId))
                return _gameState;
            if (_endState != null && _endState.Id.Equals(stateId))
                return _endState;
            return null;
        }


        public void RemoveCharacter(Character character)
        {
            if (character == null) return;

            // Удаляем из списка персонажей
            _characters.Remove(character);

            // Удаляем визуальное представление
            if (_characterVisuals.TryGetValue(character, out UIElement visual))
            {
                _canvas.GameArea.Children.Remove(visual);
                _characterVisuals.Remove(character);
            }

            // Очищаем анимационные таймеры
            if (visual != null && _animationTime.ContainsKey(visual))
                _animationTime.Remove(visual);
        }


        /// <summary>
        /// Переключить глобальное состояние игры
        /// </summary>
        public void SetState(State_ stateId)
        {
            var targetState = GetCompositeState(stateId);
            if (targetState != null)
                _gameFSM.SetState(targetState);
        }

        // В GameManager.cs добавьте метод инициализации
        public void InitializeFSM(CompositeState<State_, Event_> initialState)
        {
            _gameFSM = new FSM<State_, Event_>(initialState);
        }

        /// <summary>
        /// Переключить подсостояние внутри текущего композитного состояния
        /// </summary>
        public void SetSubState(State_ subStateId)
        {
            if (_gameFSM.CurrentState is CompositeState<State_, Event_> currentComposite)
            {
                currentComposite.SwitchToSubState(subStateId);
            }
        }

        /// <summary>
        /// Отправить событие в FSM
        /// </summary>
        public void SendEvent(Event_ ev)
        {
            _gameFSM?.HandleEvent(ev);
        }

        public void ShakeCamera() { _canvas.TriggerShake(); }
        public bool HasItemsOnGround() => _groundItems.Count > 0;

        private BitmapImage LoadBitmap(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
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

        private void DrawStaticMap()
        {
            var toRemove = _canvas.GameArea.Children
                .OfType<Image>()
                .Where(img => Canvas.GetZIndex(img) == 0 || Canvas.GetZIndex(img) == 1)
                .ToList();

            foreach (var img in toRemove)
                _canvas.GameArea.Children.Remove(img);

            for (int x = 0; x < _grid.Width; x++)
            {
                for (int y = 0; y < _grid.Height; y++)
                {
                    var cell = _grid[x, y];

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
                            Canvas.SetZIndex(bgImage, 0);
                            _canvas.GameArea.Children.Add(bgImage);
                        }
                    }

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
                            Canvas.SetZIndex(decorImage, 1);
                            _canvas.GameArea.Children.Add(decorImage);
                        }
                    }
                }
            }
        }

        private void DrawLargeDecors()
        {
            var oldLargeDecors = _canvas.GameArea.Children
                .OfType<Image>()
                .Where(img => Canvas.GetZIndex(img) == 5)
                .ToList();

            foreach (var img in oldLargeDecors)
                _canvas.GameArea.Children.Remove(img);

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
                    double leftX = decor.X * TileSize;
                    double topY = (decor.Y - decor.Height + 1) * TileSize;
                    Canvas.SetLeft(image, leftX);
                    Canvas.SetTop(image, topY);
                    Canvas.SetZIndex(image, 5);
                    _canvas.GameArea.Children.Add(image);
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

            // Используем BaseName из SpriteData, если он есть
            string baseName;
            if (!string.IsNullOrEmpty(ch.SpriteData.BaseName))
                baseName = ch.SpriteData.BaseName;
            else if (!string.IsNullOrEmpty(ch.SpritePath))
                baseName = Path.GetFileNameWithoutExtension(ch.SpritePath);
            else
                baseName = ch.Id;

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
            RenderCharacters();
        }

        public void RemoveItemFromGround(int x, int y)
        {
            if (!_grid.InBounds(x, y)) return;
            _grid[x, y].ItemOnGround = null;
            var groundItem = _groundItems.FirstOrDefault(gi => gi.X == x && gi.Y == y);
            if (groundItem != null)
                _groundItems.Remove(groundItem);
            if (_itemVisuals.TryGetValue((x, y), out Image img))
            {
                _canvas.GameArea.Children.Remove(img);
                _itemVisuals.Remove((x, y));
            }
        }

        private void RenderCharacters()
        {
            foreach (var kvp in _characterVisuals)
            {
                var character = kvp.Key;
                if (!(kvp.Value is Image visual)) continue;

                if (!character.IsAlive)
                {
                    visual.Visibility = Visibility.Collapsed;
                    continue;
                }

                visual.Visibility = Visibility.Visible;
                string baseId = (string)visual.Tag;

                // Получаем ключ анимации с учетом состояния
                string animKeySuffix;
                if (character is NPC npc)
                    animKeySuffix = npc.GetAnimationKeyWithState();
                else
                    animKeySuffix = character.GetAnimationKey(character.Velocity);

                if (string.IsNullOrEmpty(animKeySuffix))
                    animKeySuffix = "_Idle";

                bool needsFlip = false;
                string finalAnimSuffix = animKeySuffix;

                // Для анимаций влево используем зеркальное отражение спрайта вправо
                if (animKeySuffix == "_L_Walk" || animKeySuffix == "_L_Idle" || animKeySuffix == "_L_Attack")
                {
                    finalAnimSuffix = animKeySuffix.Replace("_L_", "_R_");
                    needsFlip = true;
                }

                string animFileName = $"{baseId}{finalAnimSuffix}.png";
                string fullAnimPath = Path.Combine(_spritesPath, animFileName);
                BitmapSource sheetSource = null;

                if (File.Exists(fullAnimPath))
                {
                    sheetSource = LoadBitmap(fullAnimPath);
                }
                else
                {
                    // Логирование отсутствующего файла
                    string missingFilePath = fullAnimPath.Replace(_spritesPath, "").TrimStart('\\');
                    System.Diagnostics.Debug.WriteLine($"[WARNING] Missing animation file: {missingFilePath}");

                    // Fallback на Idle анимацию, если текущая не найдена
                    if (finalAnimSuffix != "_Idle")
                    {
                        string idlePath = Path.Combine(_spritesPath, $"{baseId}_Idle.png");
                        if (File.Exists(idlePath))
                        {
                            sheetSource = LoadBitmap(idlePath);
                            System.Diagnostics.Debug.WriteLine($"         Using fallback: {baseId}_Idle.png");
                        }
                    }
                }

                if (sheetSource == null)
                {
                    // Если нет ни одного файла анимации, создаем простую заглушку
                    int width = character.SpriteData.Width;
                    int height = character.SpriteData.Height;
                    var drawingVisual = new DrawingVisual();
                    using (var context = drawingVisual.RenderOpen())
                    {
                        context.DrawRectangle(new SolidColorBrush(Colors.Magenta), null, new Rect(0, 0, width, height));
                    }
                    var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    renderTarget.Render(drawingVisual);
                    renderTarget.Freeze();
                    sheetSource = renderTarget;
                }

                ImageSource currentSource = null;
                int frameWidth = character.SpriteData.Width;
                int frameHeight = character.SpriteData.Height;

                if (sheetSource != null)
                {
                    if (frameWidth <= 0 || sheetSource.PixelWidth < frameWidth)
                    {
                        currentSource = sheetSource;
                    }
                    else
                    {
                        int frameCount = sheetSource.PixelWidth / frameWidth;

                        // ВСЕГДА проигрываем анимацию, даже для idle
                        // Разница только в скорости проигрывания
                        bool isMoving = character.Velocity.Length() > 0.001;
                        bool isAttacking = (character is NPC npc2 && npc2.CurrentState == CharacterState.Attack);

                        if (!_animationTime.ContainsKey(visual))
                            _animationTime[visual] = 0;

                        // Для движения и атаки - быструю (0.1 секунды)
                        double frameDuration;
                        if (isMoving || isAttacking)
                            frameDuration = AnimationFrameTime; // 0.1 секунды
                        else
                            frameDuration = AnimationFrameTime;

                        _animationTime[visual] += DefaultFrameDuration;

                        // Всегда обновляем анимацию, даже если стоим
                        int frameIndex = (int)(_animationTime[visual] / frameDuration) % frameCount;

                        // Сбрасываем таймер, чтобы избежать переполнения
                        if (_animationTime[visual] > frameDuration * frameCount)
                            _animationTime[visual] -= frameDuration * frameCount;

                        try
                        {
                            int xPos = frameIndex * frameWidth;
                            currentSource = new CroppedBitmap(sheetSource, new Int32Rect(xPos, 0, frameWidth, frameHeight));
                            ((CroppedBitmap)currentSource).Freeze();
                        }
                        catch
                        {
                            currentSource = sheetSource;
                        }
                    }
                }

                if (currentSource == null)
                    currentSource = CreatePlaceholder(Colors.Red);

                double scaledWidth = frameWidth * character.VisualScale;
                double scaledHeight = frameHeight * character.VisualScale;
                visual.Source = currentSource;
                visual.Width = scaledWidth;
                visual.Height = scaledHeight;
                visual.Stretch = Stretch.Uniform;

                double left = character.Position.X - (scaledWidth / 2.0);
                double top = character.Position.Y - (scaledHeight / 2.0);
                Canvas.SetLeft(visual, left);
                Canvas.SetTop(visual, top);

                double scaleX = needsFlip ? -character.VisualScale : character.VisualScale;
                visual.RenderTransform = new ScaleTransform(scaleX, character.VisualScale);
            }
        }


        public bool IsWalkable(int x, int y) => _grid.IsWalkable(x, y);

        public void LoadMap(string backgroundPath,
                            string largeDecorPath,
                            Dictionary<char, (TileType type, string spriteId)> backgroundMappings,
                            Dictionary<char, (TileType type, string spriteId, int width, int height)> largeDecorMappings,
                            TileType defaultTileType = TileType.Floor)
        {
            if (!File.Exists(backgroundPath))
                throw new FileNotFoundException($"Файл фоновой карты не найден: {backgroundPath}");

            var bgLines = File.ReadAllLines(backgroundPath);
            int mapHeight = bgLines.Length;
            int mapWidth = bgLines[0].Length;

            for (int i = 0; i < bgLines.Length; i++)
            {
                if (bgLines[i].Length != mapWidth)
                    throw new InvalidOperationException($"Строка {i + 1} фоновой карты имеет длину {bgLines[i].Length}, ожидается {mapWidth}");
            }

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

            _largeDecors.Clear();

            for (int y = 0; y < Math.Min(mapHeight, _grid.Height); y++)
            {
                string bgLine = bgLines[y];
                for (int x = 0; x < Math.Min(mapWidth, _grid.Width); x++)
                {
                    char bgSymbol = bgLine[x];
                    if (char.IsWhiteSpace(bgSymbol)) continue;
                    if (backgroundMappings.TryGetValue(bgSymbol, out var bgMapping))
                        _grid.UpdateCell(x, y, bgMapping.type, bgMapping.spriteId);
                    else
                        _grid.UpdateCell(x, y, defaultTileType, null);
                }
            }

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
                            _largeDecors.Add(new LargeDecor
                            {
                                X = x,
                                Y = y,
                                SpriteId = decor.spriteId,
                                Width = decor.width,
                                Height = decor.height,
                                Type = decor.type
                            });

                            for (int dy = 0; dy < decor.height; dy++)
                            {
                                for (int dx = 0; dx < decor.width; dx++)
                                {
                                    int newX = x + dx;
                                    int newY = y - dy;
                                    if (_grid.InBounds(newX, newY))
                                        _grid.UpdateCell(newX, newY, decor.type, null);
                                }
                            }
                        }
                    }
                }
            }

            DrawStaticMap();
            DrawLargeDecors();
            DrawItems();
        }

        public void RefreshItemsVisuals() => DrawItems();

        public void RefreshMap()
        {
            DrawStaticMap();
            DrawLargeDecors();
            DrawItems();
        }

        private class GroundItem
        {
            public int X { get; }
            public int Y { get; }
            public Item Item { get; }
            public GroundItem(int x, int y, Item item) { X = x; Y = y; Item = item; }
        }
    }

    public class PhysicsEngine
    {
        private const int TileSize = 32;
        private const double Epsilon = 0.001;
        private readonly GameGrid _grid;
        private readonly bool _enableSliding; // Включено ли скольжение вдоль стен

        /// <summary>
        /// Конструктор физического движка
        /// </summary>
        /// <param name="grid">Игровая сетка с тайлами</param>
        /// <param name="enableSliding">Включить скольжение вдоль стен (по умолчанию true)</param>
        public PhysicsEngine(GameGrid grid, bool enableSliding = true)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _enableSliding = enableSliding;
        }

        /// <summary>
        /// Обновление коллизий для всех персонажей
        /// </summary>
        public void UpdateCollisions(List<Character> characters)
        {
            foreach (var ch in characters.Where(c => c.IsAlive))
            {
                // Исправляем застрявшие позиции
                CorrectStuckPosition(ch);

                Vector2D oldPos = ch.Position;
                Vector2D newPos = oldPos + ch.Velocity;

                if (_enableSliding)
                {
                    // Логика со скольжением вдоль стен
                    Vector2D resolvedPos = ResolveCollisionWithSliding(ch, oldPos, newPos);
                    ch.Position = resolvedPos;

                    // Сбрасываем скорость, если движения нет
                    if (Math.Abs(ch.Position.X - oldPos.X) < Epsilon)
                        ch.Velocity.X = 0;
                    if (Math.Abs(ch.Position.Y - oldPos.Y) < Epsilon)
                        ch.Velocity.Y = 0;
                }
                else
                {
                    // Старая логика - полная остановка при столкновении
                    if (TryResolveCollision(ch, oldPos, newPos, out Vector2D resolvedPos))
                    {
                        ch.Position = resolvedPos;
                        if (Math.Abs(ch.Position.X - oldPos.X) < Epsilon) ch.Velocity.X = 0;
                        if (Math.Abs(ch.Position.Y - oldPos.Y) < Epsilon) ch.Velocity.Y = 0;
                    }
                    else
                    {
                        ch.Position = newPos;
                    }
                }

                // Очищаем очень маленькие скорости
                if (Math.Abs(ch.Velocity.X) < Epsilon) ch.Velocity.X = 0;
                if (Math.Abs(ch.Velocity.Y) < Epsilon) ch.Velocity.Y = 0;
            }
        }

        /// <summary>
        /// Разрешение коллизии со скольжением вдоль стен
        /// Двигаемся сначала по X, потом по Y, чтобы скользить вдоль препятствий
        /// </summary>
        private Vector2D ResolveCollisionWithSliding(Character ch, Vector2D oldPos, Vector2D newPos)
        {
            // Пробуем движение только по горизонтали
            Vector2D xMovement = new Vector2D(newPos.X, oldPos.Y);
            bool xCollision = CheckCollisionAtPosition(ch, xMovement, out _);

            // Пробуем движение только по вертикали
            Vector2D yMovement = new Vector2D(oldPos.X, newPos.Y);
            bool yCollision = CheckCollisionAtPosition(ch, yMovement, out _);

            // Нет коллизий - можно двигаться свободно
            if (!xCollision && !yCollision)
                return newPos;

            // Коллизия только по горизонтали - двигаемся только по вертикали
            if (xCollision && !yCollision)
                return yMovement;

            // Коллизия только по вертикали - двигаемся только по горизонтали
            if (!xCollision && yCollision)
                return xMovement;

            // Коллизии по обеим осям - ищем наилучшую позицию
            return FindBestSlidingPosition(ch, oldPos, newPos, xMovement, yMovement);
        }

        /// <summary>
        /// Поиск лучшей позиции при скольжении (когда оба направления заблокированы)
        /// Пытаемся найти проход между препятствиями
        /// </summary>
        private Vector2D FindBestSlidingPosition(Character ch, Vector2D oldPos, Vector2D newPos,
                                                    Vector2D xMovement, Vector2D yMovement)
        {
            // Пробуем двигаться по диагонали с уменьшенным шагом
            Vector2D direction = (newPos - oldPos).Normalize();

            // Делаем несколько шагов для поиска прохода
            for (int step = 1; step <= 8; step++)
            {
                double stepSize = (newPos - oldPos).Length() / 8.0;
                Vector2D candidate = oldPos + direction * (stepSize * step);

                if (!CheckCollisionAtPosition(ch, candidate, out _))
                    return candidate;
            }

            // Проверяем, можно ли хотя бы частично подвинуться по горизонтали
            if (!CheckCollisionAtPosition(ch, xMovement, out _))
                return xMovement;

            // Проверяем, можно ли хотя бы частично подвинуться по вертикали
            if (!CheckCollisionAtPosition(ch, yMovement, out _))
                return yMovement;

            // Если ничего не подошло - остаёмся на месте
            return oldPos;
        }

        /// <summary>
        /// Старый метод разрешения коллизий (без скольжения)
        /// Возвращает true, если коллизия была разрешена
        /// </summary>
        private bool TryResolveCollision(Character ch, Vector2D oldPos, Vector2D newPos, out Vector2D resolvedPos)
        {
            // Если почти не двигаемся или нет коллизии в новой позиции
            if ((newPos - oldPos).Length() < Epsilon || !CheckCollisionAtPosition(ch, newPos, out _))
            {
                resolvedPos = newPos;
                return false;
            }

            // Ищем границу столкновения бинарным поиском
            resolvedPos = BinarySearchCollision(ch, oldPos, newPos);
            return true;
        }

        /// <summary>
        /// Бинарный поиск точной позиции перед столкновением
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
        /// Проверка коллизии персонажа с препятствиями в заданной позиции
        /// </summary>
        private bool CheckCollisionAtPosition(Character ch, Vector2D position, out Vector2D pushVector)
        {
            double halfSize = ch.Size / 2.0;
            Rect charRect = new Rect(position.X - halfSize, position.Y - halfSize, ch.Size, ch.Size);

            // Получаем все стены, пересекающиеся с персонажем
            var intersectingWalls = GetIntersectingWalls(charRect);

            if (!intersectingWalls.Any())
            {
                pushVector = Vector2D.Zero;
                return false;
            }

            // Рассчитываем вектор выталкивания
            pushVector = CalculatePushVector(charRect, intersectingWalls);
            return true;
        }

        /// <summary>
        /// Получение всех стен (непроходимых тайлов) в области персонажа
        /// </summary>
        private List<Rect> GetIntersectingWalls(Rect charRect)
        {
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
        /// Расчет вектора выталкивания из стен
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

                // Выталкиваем в сторону минимального перекрытия
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
        /// Исправление застрявшей позиции персонажа (выталкивание из стен)
        /// </summary>
        private void CorrectStuckPosition(Character ch)
        {
            double halfSize = ch.Size / 2.0;
            Rect charRect = new Rect(ch.Position.X - halfSize, ch.Position.Y - halfSize, ch.Size, ch.Size);

            var walls = GetIntersectingWalls(charRect);

            if (walls.Any())
            {
                Vector2D push = CalculatePushVector(charRect, walls);
                ch.Position += push;
            }
        }
    }
}