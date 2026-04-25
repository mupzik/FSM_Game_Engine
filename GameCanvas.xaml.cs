using GameProj.src;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace GameProj
{
    public partial class GameCanvas : UserControl
    {
        // Событие для выхода в меню
        public static event Action OnExitToMenu;

        // Основные игровые объекты
        private GameManager _gameManager;
        private Player _player;
        private NPC _questAlly;
        private Ally _finn;

        // Состояние квеста
        private bool _questActive = false;
        private bool _questFinished = false;
        private const int SLIME_GOAL = 5;

        // Таймеры
        private DispatcherTimer _gameTimer;
        private DispatcherTimer _spawnTimer;

        // Атака и враги
        private double _attackCooldown = 0;
        private const double ATTACK_COOLDOWN_TIME = 0.4;
        private const int MAX_ENEMIES = 10;

        // Состояние игры
        private enum GameFlowState
        {
            Game,
            GameOver
        }
        private GameFlowState _currentFlowState;

        // Камера и зум
        private TranslateTransform _cameraTransform;
        private ScaleTransform _cameraScale;
        private TransformGroup _cameraTransformGroup;
        private double _currentZoom = 1.0;
        private double _shakeIntensity = 0;
        private Random _rng = new Random();

        private const double MIN_ZOOM = 1;
        private const double MAX_ZOOM = 2.0;
        private const double ZOOM_STEP = 0.1;

        // Диалоговое окно
        private Border _dialogueBox;
        private TextBlock _dialogueText;
        private bool _dialogueActive = false;
        private double _dialogueTimer = 0;

        // Пути к ресурсам
        private string _tilesPath, _spritesPath, _itemsPath, _mapsPath;

        // Размеры карты и скорость
        private const int MAP_WIDTH = 100;
        private const int MAP_HEIGHT = 100;
        private const double PLAYER_SPEED = 4.0;

        public GameCanvas()
        {
            InitializeComponent();
            Focusable = true;
            Loaded += OnLoaded;
        }

        // Инициализация путей к папкам с ресурсами
        private void InitializePaths()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _tilesPath = Path.Combine(baseDir, "Tiles");
            _spritesPath = Path.Combine(baseDir, "Sprites");
            _itemsPath = Path.Combine(baseDir, "Items");
            _mapsPath = Path.Combine(baseDir, "Maps");

            if (!Directory.Exists(_tilesPath)) Directory.CreateDirectory(_tilesPath);
            if (!Directory.Exists(_spritesPath)) Directory.CreateDirectory(_spritesPath);
            if (!Directory.Exists(_itemsPath)) Directory.CreateDirectory(_itemsPath);
            if (!Directory.Exists(_mapsPath)) Directory.CreateDirectory(_mapsPath);
        }

        // Загрузка канваса
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializePaths();

            // Настройка камеры
            _cameraTransform = new TranslateTransform();
            _cameraScale = new ScaleTransform(_currentZoom, _currentZoom);

            _cameraTransformGroup = new TransformGroup();
            _cameraTransformGroup.Children.Add(_cameraScale);
            _cameraTransformGroup.Children.Add(_cameraTransform);
            GameArea.RenderTransform = _cameraTransformGroup;

            this.SizeChanged += OnSizeChanged;
            this.PreviewMouseWheel += OnMouseWheel;

            // Запуск игры
            InitializeGame();
            CreateDialogueUI();

            // Таймер обновления
            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;
            _gameTimer.Start();
            Focus();
        }

        // Изменение зума колесиком мыши
        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_currentFlowState == GameFlowState.GameOver) return;

            double delta = e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP;
            double newZoom = _currentZoom + delta;

            if (newZoom >= MIN_ZOOM && newZoom <= MAX_ZOOM)
            {
                _currentZoom = newZoom;
                _cameraScale.ScaleX = _currentZoom;
                _cameraScale.ScaleY = _currentZoom;
                CenterCameraOnPlayer();
            }

            e.Handled = true;
        }

        // При изменении размера окна
        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_player != null && _player.IsAlive)
            {
                CenterCameraOnPlayer();
            }

            if (_dialogueBox != null)
            {
                Canvas.SetLeft(_dialogueBox, (ActualWidth - 600) / 2);
                Canvas.SetTop(_dialogueBox, ActualHeight - 150);
            }
        }

        // Центрирование камеры на игроке
        private void CenterCameraOnPlayer()
        {
            if (_player != null && _cameraTransform != null && _cameraScale != null)
            {
                double targetX = (ActualWidth / 2 - _player.Position.X * _currentZoom);
                double targetY = (ActualHeight / 2 - _player.Position.Y * _currentZoom);
                _cameraTransform.X = targetX;
                _cameraTransform.Y = targetY;
            }
        }

        // Эффект тряски камеры
        public void TriggerShake(double intensity = 5.0)
        {
            _shakeIntensity = intensity;
        }

        // Создание интерфейса диалогов
        private void CreateDialogueUI()
        {
            string dialogBoxPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UI", "Dialog_box.png");

            _dialogueBox = new Border
            {
                Padding = new Thickness(40, 15, 40, 15),
                Width = 600,
                Height = 120,
                CornerRadius = new CornerRadius(5)
            };

            if (File.Exists(dialogBoxPath))
            {
                _dialogueBox.Background = new ImageBrush(new BitmapImage(new Uri(dialogBoxPath)));
            }
            else
            {
                _dialogueBox.Background = new SolidColorBrush(Color.FromArgb(255, 245, 245, 220));
            }

            _dialogueText = new TextBlock
            {
                FontSize = 18,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeights.Normal,
                TextAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 530
            };

            _dialogueBox.Child = _dialogueText;
            _dialogueBox.Visibility = Visibility.Collapsed;

            Canvas.SetZIndex(_dialogueBox, 200);
            OverlayCanvas.Children.Add(_dialogueBox);
        }

        // Показать диалог
        public void ShowDialogue(string text, double duration = 3.0)
        {
            if (_dialogueText == null) CreateDialogueUI();

            _dialogueText.Text = text;
            _dialogueBox.Visibility = Visibility.Visible;
            _dialogueActive = true;
            _dialogueTimer = duration;
        }

        // Обновление диалога (таймер)
        private void UpdateDialogue()
        {
            if (_dialogueActive)
            {
                _dialogueTimer -= 0.016;
                if (_dialogueTimer <= 0)
                {
                    _dialogueBox.Visibility = Visibility.Collapsed;
                    _dialogueActive = false;
                }
            }
        }

        // Инициализация игры
        private void InitializeGame()
        {
            _gameManager = new GameManager(this, MAP_WIDTH, MAP_HEIGHT, InitializeMap);

            // Создание игрока
            string mcSpritePath = Path.Combine(_spritesPath, "MC.png");
            _player = new Player(
                new Vector2D(48 * 32, 48 * 32),
                "Player",
                health: 100,
                speed: PLAYER_SPEED,
                spritePath: mcSpritePath,
                visualScale: 1.0
            );
            _gameManager.AddCharacter(_player);

            // Создание NPC квестодателя
            string allySpritePath = Path.Combine(_spritesPath, "MC.png");
            _questAlly = new NPC(
                _gameManager.Grid,
                new Vector2D(46 * 32, 48 * 32),
                "QuestGiver",
                speed: 0,
                health: 100f,
                strength: 0f,
                frameSize: 48,
                spritePath: allySpritePath,
                visualScale: 1.0
            );
            _questAlly.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_questAlly);

            // Создание союзника Финна
            CreateFinn();

            // Запуск спавна врагов
            StartSpawnTimer();

            _questActive = false;
            _questFinished = false;
            _currentFlowState = GameFlowState.Game;
        }

        // Создание Финна (союзника)
        private void CreateFinn()
        {
            string finnSpritePath = Path.Combine(_spritesPath, "Orc");

            _finn = new Ally(
                _gameManager.Grid,
                new Vector2D(25 * 32, 25 * 32),
                "Finn",
                speed: 2.5,
                health: 60f,
                strength: 8f,
                frameSize: 48,
                visualScale: 1.0,
                spritePath: finnSpritePath
            );

            // Поведение в режиме ожидания
            _finn.ConfigureState(CharacterState.Idle,
                onEnter: () => _finn.Stop(),
                update: (machine) => {
                    // Случайное движение
                    if (_rng.NextDouble() < 0.01)
                    {
                        double angle = _rng.NextDouble() * Math.PI * 2;
                        Vector2D randomDir = new Vector2D(Math.Cos(angle), Math.Sin(angle));
                        _finn.Move(randomDir);
                    }

                    // Поиск врага рядом
                    Enemy nearbyEnemy = FindNearestEnemy(100);
                    if (nearbyEnemy != null)
                    {
                        _finn.SetState(CharacterState.Chase);
                    }
                });

            // Поведение преследования
            _finn.ConfigureState(CharacterState.Chase,
                update: (machine) => {
                    Enemy target = FindNearestEnemy(200);

                    if (target == null || !target.IsAlive)
                    {
                        _finn.SetState(CharacterState.Idle);
                        return;
                    }

                    double dist = Vector2D.Distance(_finn.Position, target.Position);

                    if (dist < 45)
                    {
                        _finn.SetState(CharacterState.Attack);
                    }
                    else
                    {
                        Vector2D dir = (target.Position - _finn.Position).Normalize();
                        _finn.Move(dir);
                    }
                });

            // Поведение атаки
            _finn.ConfigureState(CharacterState.Attack,
                update: (machine) => {
                    _finn.Stop();

                    Enemy target = FindNearestEnemy(60);

                    if (target != null && target.IsAlive)
                    {
                        target.TakeDamage(_finn.Strength);
                        ShowFloatingDamageNumber(target.Position, _finn.Strength, false);

                        // Выпадение предмета при смерти врага
                        if (!target.IsAlive)
                        {
                            DropItemAtPosition("slime_goo", "Слизь", "Липкая субстанция.", target.Position, Brushes.LimeGreen);
                        }

                        _finn.SetState(CharacterState.Chase);
                    }
                    else
                    {
                        _finn.SetState(CharacterState.Idle);
                    }
                });

            _finn.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_finn);
        }

        // Поиск ближайшего врага
        private Enemy FindNearestEnemy(double radius)
        {
            if (_finn == null || !_finn.IsAlive) return null;

            Enemy nearest = null;
            double minDist = radius;

            foreach (var character in _gameManager.Characters)
            {
                if (character is Enemy enemy && enemy.IsAlive)
                {
                    double dist = Vector2D.Distance(_finn.Position, enemy.Position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = enemy;
                    }
                }
            }

            return nearest;
        }

        // Выпадение предмета на землю
        private void DropItemAtPosition(string itemId, string itemName, string description, Vector2D position, Brush defaultColor = null)
        {
            var item = new Item(itemId, itemName, description, $"Items/{itemId}.png", true, 1);

            ImageSource source;
            string iconPath = Path.Combine(_itemsPath, $"{itemId}.png");

            if (File.Exists(iconPath))
            {
                var bmp = new BitmapImage(new Uri(iconPath));
                bmp.Freeze();
                source = bmp;
            }
            else
            {
                // Если картинки нет - рисуем круг
                var drawing = new DrawingGroup();
                drawing.Children.Add(new GeometryDrawing
                {
                    Brush = defaultColor ?? Brushes.LimeGreen,
                    Geometry = new EllipseGeometry(new Rect(0, 0, 20, 20))
                });
                source = new DrawingImage(drawing);
            }

            var image = new Image
            {
                Source = source,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                Tag = item
            };

            Canvas.SetLeft(image, position.X - 12);
            Canvas.SetTop(image, position.Y - 12);
            Canvas.SetZIndex(image, 4);
            GameArea.Children.Add(image);
        }

        // Запуск таймера спавна врагов
        private void StartSpawnTimer()
        {
            _spawnTimer?.Stop();
            _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _spawnTimer.Tick += (s, e) => SpawnRandomEnemy();
            _spawnTimer.Start();
            SpawnRandomEnemy();
        }

        // Создание случайного врага
        private void SpawnRandomEnemy()
        {
            if (_gameManager == null) return;

            int currentEnemies = _gameManager.Characters.Count(c => c is Enemy && c.IsAlive);
            if (currentEnemies >= MAX_ENEMIES) return;

            Vector2D spawnPos = GetRandomSpawnPosition();
            string slimeSpritePath = Path.Combine(_spritesPath, "Slime.png");

            Enemy slime = new Enemy(
                _gameManager.Grid,
                spawnPos,
                speed: 1.5,
                id: $"Slime_{DateTime.Now.Ticks}",
                spritePath: slimeSpritePath,
                frameSize: 48,
                visualScale: 1.0
            );
            slime.Strength = 4f;

            _gameManager.AddCharacter(slime);
            SetupSlimeBehavior(slime);
        }

        // Настройка поведения слизня
        private void SetupSlimeBehavior(Enemy slime)
        {
            // Режим ожидания
            slime.ConfigureState(CharacterState.Idle,
                update: (machine) =>
                {
                    slime.Stop();
                    if (_player != null && _player.IsAlive && Vector2D.Distance(slime.Position, _player.Position) < 150.0)
                        slime.SetState(CharacterState.Chase);
                });

            // Режим преследования
            slime.ConfigureState(CharacterState.Chase,
                update: (machine) =>
                {
                    if (_player == null || !_player.IsAlive)
                    {
                        slime.SetState(CharacterState.Idle);
                        return;
                    }
                    double dist = Vector2D.Distance(slime.Position, _player.Position);
                    if (dist < 40.0) slime.SetState(CharacterState.Attack);
                    else slime.Move((_player.Position - slime.Position).Normalize());
                });

            // Режим атаки
            slime.ConfigureState(CharacterState.Attack,
                update: (machine) =>
                {
                    if (_player == null || !_player.IsAlive)
                    {
                        slime.SetState(CharacterState.Idle);
                        return;
                    }
                    slime.Stop();
                    double dist = Vector2D.Distance(slime.Position, _player.Position);
                    if (dist > 50.0) slime.SetState(CharacterState.Chase);
                    else if (_rng.NextDouble() < 0.05)
                    {
                        slime.Attack(_player);
                        TriggerShake(3.0);
                        ShowFloatingDamageNumber(_player.Position, slime.Strength, false);
                    }
                });

            slime.SetState(CharacterState.Idle);
        }

        // Загрузка карты
        private void InitializeMap(GameManager gm)
        {
            var backgroundMappings = new Dictionary<char, (TileType, string)>
            {
                { 'g', (TileType.Floor, "Grass") },
                { 'G', (TileType.Wall, "Grass") },
                { 'r', (TileType.Floor, "Road") },
                { 'R', (TileType.Wall, "Road") },
                { 'd', (TileType.Floor, "Dirt") },
                { 'D', (TileType.Wall, "Dirt") }
            };

            var largeDecorMappings = new Dictionary<char, (TileType type, string spriteId, int width, int height)>
            {
                { 'T', (TileType.Wall, "Tree1", 2, 2) },
                { 'H', (TileType.Wall, "House", 4, 3) },
                { 'r', (TileType.Wall, "Rock5", 3, 3) },
                { 'R', (TileType.Wall, "Rock7", 2, 1) },
                { 't', (TileType.Wall, "Bush2", 2, 1) },
                { 'p', (TileType.Wall, "Bush1", 1, 1) }
            };

            string mapPath = Path.Combine(_mapsPath, "level2.txt");
            string largeDecorPath = Path.Combine(_mapsPath, "decor_large2.txt");

            if (File.Exists(mapPath))
            {
                gm.LoadMap(mapPath, largeDecorPath, backgroundMappings, largeDecorMappings);
            }
            else
            {
                CreateDefaultMap(gm);
            }
        }

        // Создание карты по умолчанию
        private void CreateDefaultMap(GameManager gm)
        {
            // Границы карты
            for (int x = 0; x < MAP_WIDTH; x++)
            {
                gm.SetTile(x, 0, TileType.Wall, "Road");
                gm.SetTile(x, MAP_HEIGHT - 1, TileType.Wall, "Road");
            }
            for (int y = 0; y < MAP_HEIGHT; y++)
            {
                gm.SetTile(0, y, TileType.Wall, "Road");
                gm.SetTile(MAP_WIDTH - 1, y, TileType.Wall, "Road");
            }

            // Пол
            for (int x = 1; x < MAP_WIDTH - 1; x++)
                for (int y = 1; y < MAP_HEIGHT - 1; y++)
                    gm.SetTile(x, y, TileType.Floor, "Grass");

            // Случайные деревья
            Random rand = new Random();
            for (int i = 0; i < 30; i++)
            {
                int x = rand.Next(2, MAP_WIDTH - 2);
                int y = rand.Next(2, MAP_HEIGHT - 2);
                gm.SetTile(x, y, TileType.Wall, "Tree");
            }
        }

        // Получение случайной позиции для спавна
        private Vector2D GetRandomSpawnPosition()
        {
            if (_gameManager == null) return Vector2D.Zero;
            var grid = _gameManager.Grid;
            for (int i = 0; i < 100; i++)
            {
                int x = _rng.Next(1, grid.Width - 1);
                int y = _rng.Next(1, grid.Height - 1);
                if (grid.IsWalkable(x, y))
                    return new Vector2D(x * 32 + 16, y * 32 + 16);
            }
            return new Vector2D(grid.Width * 16, grid.Height * 16);
        }

        // Показать всплывающее сообщение
        public void ShowFloatingMessage(string message, double durationSeconds)
        {
            var textBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Padding = new Thickness(15),
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(textBlock, (ActualWidth - 300));
            Canvas.SetTop(textBlock, ActualHeight - 80);
            OverlayCanvas.Children.Add(textBlock);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
            timer.Tick += (s, args) => { timer.Stop(); OverlayCanvas.Children.Remove(textBlock); };
            timer.Start();
        }

        // Показать урон над головой
        public void ShowFloatingDamageNumber(Vector2D worldPosition, float amount, bool isHeal)
        {
            if (_cameraTransform == null || _cameraScale == null) return;

            var text = new TextBlock
            {
                Text = isHeal ? $"+{amount}" : $"-{amount}",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = isHeal ? Brushes.LimeGreen : Brushes.Red,
                Opacity = 1,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1 }
            };

            double screenX = (worldPosition.X * _cameraScale.ScaleX) + _cameraTransform.X;
            double screenY = (worldPosition.Y * _cameraScale.ScaleY) + _cameraTransform.Y;

            Canvas.SetLeft(text, screenX - 20);
            Canvas.SetTop(text, screenY - 40);
            Canvas.SetZIndex(text, 200);
            OverlayCanvas.Children.Add(text);

            // Анимация подъема и исчезновения
            var translateAnimation = new DoubleAnimation
            {
                From = screenY - 40,
                To = screenY - 100,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var opacityAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromSeconds(0.8),
                BeginTime = TimeSpan.FromSeconds(0.2)
            };
            opacityAnimation.Completed += (s, e) => OverlayCanvas.Children.Remove(text);
            text.BeginAnimation(Canvas.TopProperty, translateAnimation);
            text.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        // Попытка атаки игрока
        private bool TryPlayerAttack()
        {
            if (_attackCooldown > 0) return false;
            if (_player == null || !_player.IsAlive) return false;

            double attackRange = 60.0;
            float playerDamage = 25f;
            bool hitSomething = false;

            foreach (var charac in _gameManager.Characters)
            {
                if (charac is Enemy enemy && enemy.IsAlive)
                {
                    double dist = Vector2D.Distance(_player.Position, enemy.Position);
                    if (dist <= attackRange)
                    {
                        enemy.TakeDamage(playerDamage);
                        hitSomething = true;
                        ShowFloatingDamageNumber(enemy.Position, playerDamage, false);
                        TriggerShake(2.0);

                        if (!enemy.IsAlive)
                        {
                            DropItemAtPosition("slime_goo", "Слизь", "Липкая субстанция.", enemy.Position, Brushes.LimeGreen);
                            ShowFloatingMessage("Слизень повержен!", 1.5);
                        }
                    }
                }
            }

            if (hitSomething)
            {
                _attackCooldown = ATTACK_COOLDOWN_TIME;
                return true;
            }

            _attackCooldown = 0.2;
            return false;
        }

        // Игровой тик (обновление каждый кадр)
        private void OnGameTick(object sender, EventArgs e)
        {
            if (_currentFlowState == GameFlowState.GameOver) return;

            if (_attackCooldown > 0) _attackCooldown -= 0.016;

            HandleInput();
            _gameManager.Update();
            UpdateDialogue();
            UpdateCameraShake();

            if (_player != null && _player.IsAlive && _cameraTransform != null && _shakeIntensity == 0)
                CenterCameraOnPlayer();
        }

        // Эффект тряски камеры
        private void UpdateCameraShake()
        {
            if (_shakeIntensity > 0 && _cameraTransform != null && _player != null && _cameraScale != null)
            {
                double originalX = ActualWidth / 2 - _player.Position.X * _currentZoom;
                double originalY = ActualHeight / 2 - _player.Position.Y * _currentZoom;
                double dx = (_rng.NextDouble() - 0.5) * _shakeIntensity;
                double dy = (_rng.NextDouble() - 0.5) * _shakeIntensity;
                _cameraTransform.X = originalX + dx;
                _cameraTransform.Y = originalY + dy;
                _shakeIntensity *= 0.85;
                if (_shakeIntensity < 0.3) _shakeIntensity = 0;
            }
        }

        // Обработка нажатий клавиш
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Рестарт по R
            if (e.Key == Key.R && _currentFlowState == GameFlowState.GameOver)
            {
                Restart();
                return;
            }

            // Действие по E (атака, подбор, диалог)
            if (e.Key == Key.E && _currentFlowState == GameFlowState.Game)
            {
                bool attacked = TryPlayerAttack();

                if (!attacked)
                {
                    bool pickedUp = TryPickupItem();
                    if (!pickedUp)
                    {
                        bool interacted = TryInteractWithNPC();
                        if (!interacted)
                        {
                            TryInteractWithFinn();
                        }
                    }
                }
            }

            // Выход в меню
            if (e.Key == Key.Escape) OnExitToMenu?.Invoke();
            e.Handled = true;
        }

        // Диалог с квестодателем
        private bool TryInteractWithNPC()
        {
            if (_player == null || !_player.IsAlive) return false;
            if (_questAlly == null || !_questAlly.IsAlive) return false;

            if (Vector2D.Distance(_player.Position, _questAlly.Position) <= 60)
            {
                HandleQuestDialogue(_player);
                return true;
            }
            return false;
        }

        // Логика диалогов квеста
        private void HandleQuestDialogue(Player player)
        {
            string message = "";
            if (!_questActive && !_questFinished)
            {
                message = "Привет! Мне нужно зелье. Принеси мне 5 бутылочек со слизью (Slime Goo).";
                _questActive = true;
            }
            else if (_questActive)
            {
                int currentSlimes = player.Inventory.GetTotalQuantity("slime_goo");
                if (currentSlimes >= SLIME_GOAL)
                {
                    message = "Ого! Ты принес 5 слизей? Отличная работа! Держи награду.";
                    for (int i = 0; i < SLIME_GOAL; i++) player.Inventory.RemoveItem("slime_goo");
                    player.Heal(100);
                    ShowFloatingMessage("Здоровье восстановлено!", 2.0);
                    _questActive = false;
                    _questFinished = true;
                }
                else
                {
                    message = $"У тебя пока только {currentSlimes} слизей. Нужно еще {SLIME_GOAL - currentSlimes}.";
                }
            }
            else if (_questFinished)
            {
                message = "Спасибо за помощь! Теперь я могу отдохнуть.";
            }
            ShowDialogue(message, 5.0);
        }

        // Подбор предмета с земли
        private bool TryPickupItem()
        {
            if (_player == null || !_player.IsAlive) return false;

            Image closestItemImage = null;
            double minDist = double.MaxValue;
            Item itemToPickup = null;

            foreach (var child in GameArea.Children)
            {
                if (child is Image img && img.Tag is Item item)
                {
                    double itemX = Canvas.GetLeft(img) + 12;
                    double itemY = Canvas.GetTop(img) + 12;
                    double dist = Vector2D.Distance(_player.Position, new Vector2D(itemX, itemY));
                    if (dist <= 50 && dist < minDist)
                    {
                        minDist = dist;
                        closestItemImage = img;
                        itemToPickup = item;
                    }
                }
            }

            if (closestItemImage != null && itemToPickup != null)
            {
                if (_player.Inventory.AddItem(itemToPickup) != -1)
                {
                    GameArea.Children.Remove(closestItemImage);
                    ShowFloatingMessage($"Подобрано: {itemToPickup.Name}", 1.5);
                    return true;
                }
                else
                {
                    ShowFloatingMessage("Инвентарь полон!", 1.0);
                }
            }
            return false;
        }

        // Диалог с Финном
        private void TryInteractWithFinn()
        {
            if (_finn == null || !_finn.IsAlive) return;
            if (_player == null || !_player.IsAlive) return;

            double dist = Vector2D.Distance(_player.Position, _finn.Position);
            if (dist > 60) return;

            ShowDialogue("Финн: Привет! Город странный какой-то и пустой, тебе не кажется?", 2.5);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            e.Handled = true;
        }

        // Обработка управления WASD
        private void HandleInput()
        {
            if (_player?.IsAlive != true) return;
            var dir = Vector2D.Zero;
            if (Keyboard.IsKeyDown(Key.W)) dir += new Vector2D(0, -1);
            if (Keyboard.IsKeyDown(Key.S)) dir += new Vector2D(0, 1);
            if (Keyboard.IsKeyDown(Key.A)) dir += new Vector2D(-1, 0);
            if (Keyboard.IsKeyDown(Key.D)) dir += new Vector2D(1, 0);
            if (dir.Length() > 0) _player.Move(dir);
            else _player.Stop();
        }

        // Конец игры
        public void GameOver(bool isWin)
        {
            _currentFlowState = GameFlowState.GameOver;
            var gameOverText = new TextBlock
            {
                Text = isWin ? "ПОБЕДА!\n\nНажмите R для рестарта" : "ВЫ УМЕРЛИ!\n\nНажмите R для рестарта",
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                Foreground = isWin ? Brushes.Gold : Brushes.Red,
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                Padding = new Thickness(30),
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetLeft(gameOverText, (ActualWidth - 400) / 2);
            Canvas.SetTop(gameOverText, (ActualHeight - 200) / 2);
            Canvas.SetZIndex(gameOverText, 1000);
            OverlayCanvas.Children.Add(gameOverText);
        }

        // Перезапуск игры
        public void Restart()
        {
            _gameTimer?.Stop();
            _spawnTimer?.Stop();
            GameArea.Children.Clear();
            OverlayCanvas.Children.Clear();
            _currentZoom = 1.0;
            if (_cameraScale != null)
            {
                _cameraScale.ScaleX = _currentZoom;
                _cameraScale.ScaleY = _currentZoom;
            }
            InitializeGame();
            CreateDialogueUI();
            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;
            _gameTimer.Start();
            Focus();
        }
    }

    // Генератор карт
    public class MapGenerator
    {
        public static void GenerateMapFiles(string outputPath)
        {
            int width = 100, height = 100;
            char[,] bgMap = new char[height, width];
            char[,] decorMap = new char[height, width];

            // Заполняем травой
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                { bgMap[y, x] = 'g'; decorMap[y, x] = '.'; }

            Random rnd = new Random(42);

            // Границы
            for (int x = 0; x < width; x++)
            { bgMap[0, x] = 'G'; bgMap[height - 1, x] = 'G'; decorMap[0, x] = 'T'; decorMap[height - 1, x] = 'T'; }
            for (int y = 0; y < height; y++)
            { bgMap[y, 0] = 'G'; bgMap[y, width - 1] = 'G'; decorMap[y, 0] = 'T'; decorMap[y, width - 1] = 'T'; }

            // Дорога в центре
            int roadCenterX = width / 2, roadCenterY = height / 2;
            for (int x = 5; x < width - 5; x++)
                for (int dy = -2; dy <= 2; dy++)
                { int y = roadCenterY + dy; if (y > 0 && y < height - 1) bgMap[y, x] = 'r'; }
            for (int y = 5; y < height - 5; y++)
                for (int dx = -2; dx <= 2; dx++)
                { int x = roadCenterX + dx; if (x > 0 && x < width - 1 && bgMap[y, x] != 'r') bgMap[y, x] = 'r'; }

            // Леса и декорации
            for (int y = 2; y < height - 2; y++)
            {
                for (int x = 2; x < width - 2; x++)
                {
                    if (bgMap[y, x] == 'r') continue;
                    if (x < 35 && y < 35) { if (rnd.NextDouble() < 0.01) decorMap[y, x] = 'p'; continue; }
                    double noise = rnd.NextDouble();
                    bool isDenseForest = (x > 60 && y > 60);
                    if (isDenseForest)
                    {
                        if (noise < 0.15) { bgMap[y, x] = 'g'; decorMap[y, x] = 'T'; }
                        else if (noise < 0.25) decorMap[y, x] = 't';
                    }
                    else
                    {
                        if (noise < 0.04) { bgMap[y, x] = 'g'; decorMap[y, x] = 'T'; }
                        else if (noise < 0.07) decorMap[y, x] = 'p';
                        else if (noise < 0.08) decorMap[y, x] = 'r';
                    }
                }
            }

            // Очистка области спавна игрока
            int spawnX = 48, spawnY = 48;
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                { int cx = spawnX + dx, cy = spawnY + dy; if (cx > 0 && cx < width && cy > 0 && cy < height) { bgMap[cy, cx] = 'g'; decorMap[cy, cx] = '.'; } }

            // Сохранение файлов
            try
            {
                Directory.CreateDirectory(outputPath);
                StringBuilder sbBg = new StringBuilder(), sbDecor = new StringBuilder();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++) { sbBg.Append(bgMap[y, x]); sbDecor.Append(decorMap[y, x]); }
                    sbBg.AppendLine(); sbDecor.AppendLine();
                }
                File.WriteAllText(Path.Combine(outputPath, "level2.txt"), sbBg.ToString());
                File.WriteAllText(Path.Combine(outputPath, "decor_large2.txt"), sbDecor.ToString());
            }
            catch (Exception ex) { System.Windows.MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка"); }
        }
    }
}