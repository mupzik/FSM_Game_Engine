using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GameProj.src;
using Path = System.IO.Path;

namespace GameProj
{
    public partial class GameCanvas : UserControl
    {
        public static event Action OnExitToMenu;

        private GameManager _gameManager;
        private Player _player;
        private Ally _ally;

        // Таймер игры
        private DispatcherTimer _gameTimer;
        private DispatcherTimer _potionSpawnerTimer; // Таймер для спавна зелий

        // FSM для потока игры
        private FSM<string, string> _gameFlowFSM;
        private State<string, string> _stateTutorial;
        private State<string, string> _stateGame;
        private State<string, string> _stateGameOver;

        private string _lastStatus = "";
        private int _score = 0; // СЧЕТЧИК ОЧКОВ

        // Для тряски экрана
        private TranslateTransform _cameraTransform;
        private double _shakeIntensity = 0;
        private Random _rng = new Random();

        private string _tilesPath, _spritesPath, _itemsPath;

        // Константы
        private const int MAP_WIDTH = 30;
        private const int MAP_HEIGHT = 30;
        private const int POTION_SPAWN_INTERVAL_MS = 2000; // Зелье каждые 2 секунды

        public GameCanvas()
        {
            InitializeComponent();
            Focusable = true;
            Loaded += OnLoaded;
            InitializeGame();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cameraTransform = new TranslateTransform();
            GameArea.RenderTransform = _cameraTransform;

            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;

            // Таймер спавна предметов
            _potionSpawnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(POTION_SPAWN_INTERVAL_MS) };
            _potionSpawnerTimer.Tick += OnPotionSpawnTick;

            _gameTimer.Start();
            _potionSpawnerTimer.Start();
            Focus();
        }

        public void TriggerShake(double intensity = 5.0)
        {
            _shakeIntensity = intensity;
        }

        private void InitializeGame()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _tilesPath = Path.Combine(baseDir, "Tiles");
            _spritesPath = Path.Combine(baseDir, "Sprites");
            _itemsPath = Path.Combine(baseDir, "Items");

            _score = 0; // Сброс очков

            // --- 1. НАСТРОЙКА FSM ---
            _stateTutorial = new State<string, string>("Tutorial");
            _stateGame = new State<string, string>("Game");
            _stateGameOver = new State<string, string>("GameOver");

            _stateTutorial.SetUpdate(m =>
            {
                if (Keyboard.IsKeyDown(Key.W) || Keyboard.IsKeyDown(Key.A) ||
                    Keyboard.IsKeyDown(Key.S) || Keyboard.IsKeyDown(Key.D))
                {
                    m.SetState(_stateGame);
                }
            });

            _stateGameOver.SetEnter(() =>
            {
                _lastStatus = $"ИГРА ОКОНЧЕНА! Ваш счет: {_score}. Нажмите R.";
                StopCharacters();
                _potionSpawnerTimer.Stop();
            });

            _gameFlowFSM = new FSM<string, string>(_stateTutorial);

            // --- 2. СОЗДАНИЕ GAMEMANAGER И ГЕНЕРАЦИЯ ЛАБИРИНТА ---
            _gameManager = new GameManager(this, MAP_WIDTH, MAP_HEIGHT, gm =>
            {
                // 1. Заполняем ВСЮ карту СТЕНАМИ (Трава - это стена)
                for (int x = 0; x < MAP_WIDTH; x++)
                    for (int y = 0; y < MAP_HEIGHT; y++)
                        gm.SetTile(x, y, TileType.Wall, "Road");

                // 2. Генерируем дороги (лабиринт) внутри стен
                GenerateRandomMaze(gm);
            });

            var grid = _gameManager.Grid;

            // --- 3. ИГРОК ---
            // Ищем безопасное место на дороге
            Vector2D playerStart = FindSafeSpawnPosition(grid);

            _player = new Player(
                playerStart,
                id: "Player",
                speed: 3.5,
                spritePath: "MC");

            _gameManager.AddCharacter(_player);
            _gameManager.SetPlayer(_player);

            _player.OnHealthChanged += (ch, dmg) =>
            {
                if (dmg < 0) TriggerShake(8.0);
                if (!_player.IsAlive) _gameFlowFSM.SetState(_stateGameOver);
            };

            // Подбор предметов добавляет очки
            _player.OnItemPickedUp += (ch, itemId) =>
            {
                if (itemId == "Potion")
                {
                    _score += 10;
                    _lastStatus = "+10 Очков!";
                    _player.Heal(5);
                }
            };

            // --- 4. ALLY (Враг) ---
            Vector2D allyStart = FindSafeSpawnPosition(grid);
            while (Vector2D.Distance(allyStart, playerStart) < 150)
            {
                allyStart = FindSafeSpawnPosition(grid);
            }

            _ally = new Ally(
                grid: grid,
                startPosition: allyStart,
                id: "Hunter",
                speed: 2.8, // Чуть быстрее обычного
                spritePath: "Orc"
            );

            // Настраиваем переходы
            _ally.AddTransition(CharacterState.Idle, CharacterState.Patrol, 1.0);
            _ally.AddTransition(CharacterState.Patrol, CharacterState.Idle, 2.0);
            _ally.AddTransition(CharacterState.Patrol, CharacterState.Attack, 1.0);
            _ally.AddTransition(CharacterState.Idle, CharacterState.Attack, 1.0);
            _ally.AddTransition(CharacterState.Attack, CharacterState.Patrol, 1.0);

            // Логика PATROL (Блуждание + Охота за зельями)
            _ally.ConfigureState(CharacterState.Patrol, update: machine =>
            {
                if (!_ally.IsAlive) return;

                // 1. ПРИОРИТЕТ: Поиск зелий
                // Используем вспомогательный метод, чтобы найти ближайшее зелье
                Item nearestPotion = FindNearestItem(_ally.Position, "Potion", 250);

                if (nearestPotion != null)
                {
                    // Вычисляем позицию предмета в мире на основе его координат в сетке
                    // Нам нужно найти, где лежит этот предмет
                    Vector2D itemPos = GetItemWorldPosition(nearestPotion);

                    // Двигаемся к зелью
                    var dirToPotion = itemPos - _ally.Position;

                    // Если подошли очень близко, останавливаемся (GameManager сам подберет)
                    if (dirToPotion.Length() < 5)
                    {
                        _ally.Stop();
                    }
                    else
                    {
                        _ally.Move(dirToPotion.Normalize());
                    }
                    return; // Прерываем обычное блуждание, пока есть цель (зелье)
                }

                // 2. ОБЫЧНОЕ БЛУЖДАНИЕ (если зелий нет рядом)
                if (_rng.NextDouble() < 0.02)
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    _ally.Move(new Vector2D(Math.Cos(angle), Math.Sin(angle)));
                }

                // 3. СЛУЧАЙНАЯ АГРЕССИЯ (Ваш запрос)
                double distToPlayer = Vector2D.Distance(_ally.Position, _player.Position);

                // Если игрок ближе 180 пикселей И выпадает шанс (1.5% каждый кадр ~ раз в секунду)
                if (distToPlayer < 180 && _rng.NextDouble() < 0.015)
                {
                    _ally.SetState(CharacterState.Attack);
                }
            });

            // Логика ATTACK
            _ally.ConfigureState(CharacterState.Attack, update: machine =>
            {
                if (!_ally.IsAlive || !_player.IsAlive)
                {
                    _ally.SetState(CharacterState.Patrol);
                    return;
                }

                var dirToPlayer = _player.Position - _ally.Position;
                double dist = dirToPlayer.Length();

                // Если игрок убежал далеко, теряем интерес
                if (dist > 300)
                {
                    _ally.SetState(CharacterState.Patrol);
                    return;
                }

                if (dist > 5) // Радиус атаки
                {
                    _ally.Move(dirToPlayer.Normalize());
                }
                else
                {
                    _ally.Stop();
                    _ally.Attack(_player);
                }
            });

            // Подписка на события Ally
            _ally.OnItemPickedUp += (ch, itemId) =>
            {
                if (itemId == "Potion")
                {
                    _ally.Heal(5); // Ally лечится
                    _lastStatus = "Орк подобрал зелье!";
                }
            };

            _gameManager.AddCharacter(_ally);
            _gameManager.SetAlly(_ally);
        }

        /// <summary>
        /// Генерирует лабиринт: Стены = 1 клетка (Grass/Wall), Проходы = 2 клетки (Road/Floor).
        /// Использует алгоритм DFS (Recursive Backtracker).
        /// </summary>
        private void GenerateRandomMaze(GameManager gm)
        {
            int startX = 1;
            int startY = 1;

            // Стек для алгоритма DFS
            Stack<(int x, int y)> stack = new Stack<(int x, int y)>();

            // Помечаем стартовую точку как посещенную (делаем дорогой)
            MakeRoad(gm, startX, startY);
            stack.Push((startX, startY));

            // Направления: Вверх, Вниз, Влево, Вправо
            // Шаг = 2, чтобы перепрыгивать через стену
            int[] dx = { 0, 0, -2, 2 };
            int[] dy = { -2, 2, 0, 0 };

            while (stack.Count > 0)
            {
                var current = stack.Peek();
                int cx = current.x;
                int cy = current.y;

                // Ищем соседей, куда еще не ходили (на расстоянии 2 клеток)
                List<int> neighbors = new List<int>();
                for (int i = 0; i < 4; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];

                    // Проверка границ (оставляем рамку 1 клетка)
                    if (nx > 0 && nx < MAP_WIDTH - 1 && ny > 0 && ny < MAP_HEIGHT - 1)
                    {
                        // Если клетка еще является стеной (не посещена)
                        if (gm.Grid[nx, ny].Type == TileType.Wall)
                        {
                            neighbors.Add(i);
                        }
                    }
                }

                if (neighbors.Count > 0)
                {
                    // Выбираем случайного соседа
                    int dirIndex = neighbors[_rng.Next(neighbors.Count)];
                    int nx = cx + dx[dirIndex];
                    int ny = cy + dy[dirIndex];

                    // Пробиваем стену МЕЖДУ текущей и следующей клеткой
                    // Так как шаг 2, стена находится посередине: (cx+nx)/2, (cy+ny)/2
                    int wallX = (cx + nx) / 2;
                    int wallY = (cy + ny) / 2;

                    // Делаем дорогу в следующей клетке
                    MakeRoad(gm, nx, ny);

                    // Делаем дорогу в стене между ними (чтобы соединить)
                    MakeRoad(gm, wallX, wallY);

                    // ДЛЯ ШИРИНЫ КОРИДОРА 2 КЛЕТКИ:
                    if (dirIndex == 0 || dirIndex == 1) // Движение по вертикали
                    {
                        if (nx + 1 < MAP_WIDTH - 1)
                        {
                            MakeRoad(gm, nx + 1, ny);
                            MakeRoad(gm, wallX + 1, wallY);
                        }
                    }
                    else // Движение по горизонтали
                    {
                        if (ny + 1 < MAP_HEIGHT - 1)
                        {
                            MakeRoad(gm, nx, ny + 1);
                            MakeRoad(gm, wallX, wallY + 1);
                        }
                    }

                    stack.Push((nx, ny));
                }
                else
                {
                    // Тупик, возвращаемся назад
                    stack.Pop();
                }
            }

            // Опционально: Добавить немного случайных проломов в стенах для цикличности
            for (int i = 0; i < 50; i++)
            {
                int rx = _rng.Next(1, MAP_WIDTH - 1);
                int ry = _rng.Next(1, MAP_HEIGHT - 1);
                if (gm.Grid[rx, ry].Type == TileType.Wall)
                {
                    if (HasNeighborRoad(gm, rx, ry))
                    {
                        MakeRoad(gm, rx, ry);
                    }
                }
            }
        }

        private void MakeRoad(GameManager gm, int x, int y)
        {
            if (gm.Grid.InBounds(x, y))
            {
                gm.SetTile(x, y, TileType.Floor, "Grass");
            }
        }

        private bool HasNeighborRoad(GameManager gm, int x, int y)
        {
            int[] dx = { 0, 0, -1, 1 };
            int[] dy = { -1, 1, 0, 0 };
            for (int i = 0; i < 4; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                if (gm.Grid.InBounds(nx, ny) && gm.Grid[nx, ny].Type == TileType.Floor)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Ищет случайную клетку типа Floor (Road)
        /// </summary>
        private Vector2D FindSafeSpawnPosition(GameGrid grid)
        {
            for (int i = 0; i < 500; i++)
            {
                int x = _rng.Next(1, MAP_WIDTH - 1);
                int y = _rng.Next(1, MAP_HEIGHT - 1);

                // Проверяем, что это пол (дорога), а не стена (трава)
                if (grid[x, y].Type == TileType.Floor)
                {
                    return grid.GridToPixelCenter(x, y);
                }
            }

            // Если вдруг дорог не нашли (ошибка генерации), возвращаем центр
            return grid.GridToPixelCenter(MAP_WIDTH / 2, MAP_HEIGHT / 2);
        }

        /// <summary>
        /// Спавнит зелье в случайном свободном месте
        /// </summary>
        private void SpawnRandomPotion()
        {
            if (_gameFlowFSM.CurrentState.Id != "Game") return;

            // Попытка найти место
            for (int i = 0; i < 100; i++)
            {
                int x = _rng.Next(1, MAP_WIDTH - 1);
                int y = _rng.Next(1, MAP_HEIGHT - 1);

                var cell = _gameManager.Grid[x, y];

                // Проверяем: это пол? нет ли там уже предмета?
                if (cell.Type == TileType.Floor && cell.ItemOnGround == null)
                {
                    // Создаем предмет
                    string itemPath = Path.Combine(_itemsPath, "Potion.png");

                    if (!System.IO.File.Exists(itemPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"ОШИБКА: Файл не найден: {itemPath}");
                    }

                    var potion = new Item("Potion", "Energy Drink", "+10 Очков", itemPath, isStackable: false);

                    // Размещаем в сетке
                    _gameManager.PlaceItem(x, y, potion);

                    // Обновляем визуализацию предметов на экране
                    _gameManager.RefreshItemsVisuals();

                    System.Diagnostics.Debug.WriteLine($"Зелье заспавнено на: {x}, {y}");
                    return;
                }
            }
            System.Diagnostics.Debug.WriteLine("Не удалось найти место для зелья за 100 попыток.");
        }

        private void OnPotionSpawnTick(object sender, EventArgs e)
        {
            SpawnRandomPotion();
        }

        private void StopCharacters()
        {
            _player?.Stop();
            _ally?.Stop();
        }

        private void OnGameTick(object sender, EventArgs e)
        {
            _gameFlowFSM.Update();

            if (_gameFlowFSM.CurrentState.Id != "GameOver")
            {
                HandleInput();
                _gameManager.Update();
            }

            UpdateUI();
            UpdateCameraShake();
        }

        private void UpdateUI()
        {
            if (_gameFlowFSM.CurrentState.Id == "Tutorial")
            {
                GameStateDisplay.Text = "Обучение: WASD - движение. Собирай зелья!";
                TutorialHint.Visibility = Visibility.Visible;
            }
            else
            {
                TutorialHint.Visibility = Visibility.Collapsed;

                string status = _lastStatus;
                if (_gameFlowFSM.CurrentState.Id == "GameOver")
                {
                    // Текст уже установлен в SetEnter
                }
                else
                {
                    // Можно показывать последние действия или подсказки
                    if (string.IsNullOrEmpty(status) || status.StartsWith("+"))
                    {
                        status = "Выживай и собирай зелья!";
                    }
                }

                // Обновляем заголовок статуса, если он не критичен
                if (_gameFlowFSM.CurrentState.Id == "Game")
                    GameStateDisplay.Text = status;

                PlayerInventory.Text = $"ОЧКИ: {_score} | HP: {_player.Health}";

                // Ally Info
                if (_ally != null)
                {
                    AllyInventory.Text = $"Hunter HP: {_ally.Health}";
                    AllyInventory.Foreground = _ally.CurrentState == CharacterState.Attack ? Brushes.Red : Brushes.White;
                }
            }
        }

        /// <summary>
        /// Ищет ближайший предмет определенного типа в радиусе range
        /// </summary>
        private Item FindNearestItem(Vector2D position, string itemType, double range)
        {
            Item nearest = null;
            double minDistSq = range * range;

            // Перебираем сетку.
            for (int x = 0; x < MAP_WIDTH; x++)
            {
                for (int y = 0; y < MAP_HEIGHT; y++)
                {
                    var cell = _gameManager.Grid[x, y];
                    // ИСПРАВЛЕНО: используем Key вместо Id
                    if (cell.ItemOnGround != null && cell.ItemOnGround.Key == itemType)
                    {
                        // Получаем мировые координаты центра клетки
                        Vector2D itemPos = _gameManager.Grid.GridToPixelCenter(x, y);

                        double distSq = Vector2D.DistanceSquared(position, itemPos);

                        if (distSq < minDistSq)
                        {
                            minDistSq = distSq;
                            nearest = cell.ItemOnGround;
                        }
                    }
                }
            }
            return nearest;
        }

        /// <summary>
        /// Helper to find world position of an item currently on the grid
        /// </summary>
        private Vector2D GetItemWorldPosition(Item item)
        {
            for (int x = 0; x < MAP_WIDTH; x++)
            {
                for (int y = 0; y < MAP_HEIGHT; y++)
                {
                    if (_gameManager.Grid[x, y].ItemOnGround == item)
                    {
                        return _gameManager.Grid.GridToPixelCenter(x, y);
                    }
                }
            }
            return Vector2D.Zero;
        }

        private void UpdateCameraShake()
        {
            if (_shakeIntensity > 0)
            {
                double dx = (_rng.NextDouble() - 0.5) * _shakeIntensity;
                double dy = (_rng.NextDouble() - 0.5) * _shakeIntensity;
                _cameraTransform.X = dx;
                _cameraTransform.Y = dy;
                _shakeIntensity *= 0.9;
                if (_shakeIntensity < 0.5)
                {
                    _shakeIntensity = 0;
                    _cameraTransform.X = 0;
                    _cameraTransform.Y = 0;
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.R && _gameFlowFSM.CurrentState.Id == "GameOver")
            {
                Restart();
                return;
            }

            _gameManager.OnTutorialKeyPress(e.Key);

            e.Handled = true;
        }
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); e.Handled = true; }

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

            if (Keyboard.IsKeyDown(Key.Escape)) OnExitToMenu?.Invoke();
        }

        public void Restart()
        {
            _gameTimer?.Stop();
            _potionSpawnerTimer?.Stop();
            _gameTimer.Tick -= OnGameTick;
            _potionSpawnerTimer.Tick -= OnPotionSpawnTick;

            GameArea.Children.Clear();
            _gameManager = null;
            _player = null;
            _ally = null;
            _lastStatus = "";
            _score = 0;

            InitializeGame();

            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;
            _gameTimer.Start();

            _potionSpawnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(POTION_SPAWN_INTERVAL_MS) };
            _potionSpawnerTimer.Tick += OnPotionSpawnTick;
            _potionSpawnerTimer.Start();

            Focus();
        }
    }
}