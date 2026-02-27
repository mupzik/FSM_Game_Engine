using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static GameProj.GameManager;

namespace GameProj
{
    public partial class GameCanvas : UserControl
    {
        public static event Action OnExitToMenu;

        private const int TILESIZE = 32;
        private GameManager _gameManager;
        private Player _player;
        private Ally _ally;
        private DispatcherTimer _gameTimer;
        private State_ _lastGameState = State_.Tutorial;

        public GameCanvas()
        {
            InitializeComponent();
            Focusable = true;
            Loaded += OnLoaded;
            InitializeGame();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;
            _gameTimer.Start();
            Focus();
        }

        private void InitializeGame()
        {
            ImageSource LoadItemSprite(string name)
            {
                string itemsPath = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Items");
                string filePath = System.IO.Path.Combine(itemsPath, name + ".png");

                if (System.IO.File.Exists(filePath))
                    return new BitmapImage(new Uri(filePath));

                var drawing = new GeometryDrawing { Brush = Brushes.Gray, Geometry = new EllipseGeometry(new Point(8, 8), 8, 8) };
                return new DrawingImage(drawing);
            }

            var swordSprite = LoadItemSprite("Sword");
            var potionSprite = LoadItemSprite("Potion");

            var sword = new Item("Sword", "Меч", "Обычный меч", price: 50, isStackable: false, sprite: swordSprite);
            var potion = new Item("Potion", "Зелье", "Восстанавливает здоровье", price: 20, isStackable: true,
                useAction: (ch) => ch.Heal(10), sprite: potionSprite);

            var interestPoints = new List<Vector2D>();

            _gameManager = new GameManager(this, 30, 30, gm =>
            {
                // Дорожки
                for (int i = 8; i < 13; i++)
                {
                    gm.SetTile(i, 8, CellType.Floor, "Grass", "Road");
                    gm.SetTile(i, 12, CellType.Floor, "Grass", "Road");
                    gm.SetTile(8, i, CellType.Floor, "Grass", "Road");
                    gm.SetTile(12, i, CellType.Floor, "Grass", "Road");
                }

                // Декор
                for (int i = 1; i < 26; i += 5)
                {
                    gm.SetTile(i, i + 2, CellType.Floor, "Grass", "2");
                    gm.SetTile(i, i + 6, CellType.Floor, "Grass", "2");
                    gm.SetTile(i, i + 16, CellType.Floor, "Grass", "3");
                    gm.SetTile(i, i + 24, CellType.Floor, "Grass", "2");
                    gm.SetTile(i, i + 20, CellType.Floor, "Grass", "3");
                    gm.SetTile(i + 2, i, CellType.Floor, "Grass", "2");
                    gm.SetTile(i + 13, i, CellType.Floor, "Grass", "2");
                    gm.SetTile(i + 6, i, CellType.Floor, "Grass", "3");
                    gm.SetTile(i + 9, i, CellType.Floor, "Grass", "3");
                    gm.SetTile(i + 22, i, CellType.Floor, "Grass", "2");
                }

                // Границы
                for (int i = 0; i < 30; i++)
                {
                    gm.SetTile(i, 0, CellType.Wall, "Grass", "Fence_H");
                    gm.SetTile(i, 29, CellType.Wall, "Grass", "Fence_H");
                    gm.SetTile(0, i, CellType.Wall, "Grass", "Fence_U");
                    gm.SetTile(29, i, CellType.Wall, "Grass", "Fence_U");
                }
                gm.SetTile(0, 0, CellType.Wall, "Grass", "Fence_Corner1");
                gm.SetTile(29, 29, CellType.Wall, "Grass", "Fence_Corner1");
                gm.SetTile(29, 0, CellType.Wall, "Grass", "Fence_Corner2");
                gm.SetTile(0, 29, CellType.Wall, "Grass", "Fence_Corner2");

                // Предметы
                gm.SetTile(10, 10, CellType.Floor, "Grass");
                gm.PlaceItem(10, 10, sword);
                interestPoints.Add(new Vector2D(10.5, 10.5));

                gm.SetTile(15, 12, CellType.Floor, "Grass");
                gm.PlaceItem(15, 12, potion);
                interestPoints.Add(new Vector2D(15.5, 12.5));
            });

            _player = new Player(new Vector2D(5.5, 5.5), speed: 0.1, id: "Player");
            _gameManager.AddCharacter(_player);

            var patrolPoints = new List<Vector2D>
            {
                new Vector2D(8, 8), new Vector2D(12, 8),
                new Vector2D(12, 12), new Vector2D(8, 12),
                new Vector2D(8, 8), new Vector2D(12, 8),
                new Vector2D(12, 12), new Vector2D(8, 12)
            };

            Ally ally = null;
            ally = new Ally(
                grid: _gameManager.Grid,
                startPosition: new Vector2D(8, 8),
                speed: 0.1,
                id: "Ally",
                transitionProvider: () =>
                {
                    var transitions = new List<(CharacterState state, double probability)>();
                    transitions.Add((CharacterState.Patrol, 15.0));
                    if (_gameManager.HasItemsOnGround())
                        transitions.Add((CharacterState.GoToItem, 2.0));

                    bool hasSword = ally.Inventory.HasItem("Sword");
                    bool dragonAlive = _gameManager._dragon?.IsAlive == true;
                    if (hasSword && dragonAlive)
                        transitions.Add((CharacterState.Attack, 3.0));
                    return transitions;
                });

            ally.PatrolPoints.AddRange(patrolPoints);

            ally.ConfigureState(CharacterState.Patrol, update: machine =>
            {
                if (!ally.IsAlive || ally.PatrolPoints.Count == 0) return;
                var target = ally.PatrolPoints[0];
                var dir = target - ally.Position; // Используем Position напрямую

                if (dir.Length() < 0.3)
                {
                    var pt = ally.PatrolPoints[0];
                    ally.PatrolPoints.RemoveAt(0);
                    ally.PatrolPoints.Add(pt);
                    ally.SetState(CharacterState.Decision);
                }
                else
                {
                    ally.Move(dir.Normalize());
                }
            });

            ally.ConfigureState(CharacterState.GoToItem,
                onEnter: () =>
                {
                    Vector2D closest = null;
                    double minDist = double.MaxValue;
                    for (int x = 0; x < _gameManager.Grid.Width; x++)
                    {
                        for (int y = 0; y < _gameManager.Grid.Height; y++)
                        {
                            var cell = _gameManager.Grid[x, y];
                            if (cell?.ItemOnGround != null)
                            {
                                var itemPos = new Vector2D(x + 0.5, y + 0.5);
                                double dist = Vector2D.Distance(ally.Position, itemPos);
                                if (dist < minDist)
                                {
                                    minDist = dist;
                                    closest = itemPos;
                                }
                            }
                        }
                    }
                    ally.CurrentTarget = closest;
                },
                update: machine =>
                {
                    if (!ally.IsAlive || ally.CurrentTarget == null)
                    {
                        ally.Stop();
                        ally.SetState(CharacterState.Decision);
                        return;
                    }
                    var dir = ally.CurrentTarget - ally.Position;
                    if (dir.Length() < 0.3)
                    {
                        ally.CurrentTarget = null;
                        ally.Stop();
                        ally.SetState(CharacterState.Decision);
                    }
                    else
                    {
                        ally.Move(dir.Normalize());
                    }
                });

            ally.ConfigureState(CharacterState.Attack,
                onEnter: () =>
                {
                    if (_gameManager._dragon?.IsAlive == true)
                        ally.CurrentTarget = _gameManager._dragon.Position;
                    else
                    {
                        ally.CurrentTarget = null;
                        ally.SetState(CharacterState.Decision);
                    }
                },
                update: machine =>
                {
                    if (!ally.IsAlive || ally.CurrentTarget == null)
                    {
                        ally.Stop();
                        ally.SetState(CharacterState.Decision);
                        return;
                    }
                    var dir = ally.CurrentTarget - ally.Position;
                    if (dir.Length() < 0.3)
                    {
                        if (_gameManager._dragon?.IsAlive == true)
                        {
                            _gameManager._dragon.Die();
                            _gameManager.OnDragonKilled(byAlly: true);
                        }
                        ally.CurrentTarget = null;
                        ally.Stop();
                        ally.SetState(CharacterState.Decision);
                    }
                    else
                    {
                        ally.Move(dir.Normalize());
                    }
                });

            _gameManager.AddCharacter(ally);
            _gameManager.SetAlly(ally);
            _ally = ally;

            _player.OnItemPickedUp += (character, itemId) =>
            {
                _gameManager.OnItemPickedUp(itemId, byAlly: false);
                int x = (int)Math.Floor(character.Position.X);
                int y = (int)Math.Floor(character.Position.Y);
                _gameManager.RemoveInterestPointAt(x, y);
            };

            _ally.OnItemPickedUp += (character, itemId) =>
            {
                _gameManager.OnItemPickedUp(itemId, byAlly: true);
                int x = (int)Math.Floor(character.Position.X);
                int y = (int)Math.Floor(character.Position.Y);
                _gameManager.RemoveInterestPointAt(x, y);
            };

            var dragon = new Dragon(_gameManager.Grid, new Vector2D(20, 20), "Dragon");
            _gameManager.AddCharacter(dragon);
            _gameManager.SetDragon(dragon);
        }

        private void OnGameTick(object sender, EventArgs e)
        {
            HandleInput();
            _gameManager.Update(); // Теперь включает физику
            UpdateUI();
        }

        private void UpdateUI()
        {
            var currentState = _gameManager.CurrentGameState;
            if (currentState != _lastGameState)
            {
                string stateText;

                // Классический оператор switch, совместимый с C# 7.3
                switch (currentState)
                {
                    case State_.Tutorial:
                        stateText = "Обучение";
                        break;
                    case State_.NothingFound:
                        stateText = "Игра: Ничего не найдено";
                        break;
                    case State_.SwordFound:
                        stateText = "Игра: Есть меч";
                        break;
                    case State_.HealFound:
                        stateText = "Игра: Есть зелье";
                        break;
                    case State_.AllFound:
                        stateText = "Игра: Есть всё";
                        break;
                    case State_.NothingFoundEnd:
                        stateText = "Конец: Погиб у дракона";
                        break;
                    case State_.SwordFoundEnd:
                        stateText = "Конец: Убил дракона, но погиб";
                        break;
                    case State_.HealFoundEnd:
                        stateText = "Конец: Выжил, но не убил дракона";
                        break;
                    case State_.AllFoundEnd:
                        stateText = "Конец: Победа!";
                        break;
                    case State_.AllyKillsDragon:
                        stateText = "Конец: Ally победил дракона!";
                        break;
                    default:
                        stateText = currentState.ToString();
                        break;
                }

                GameStateDisplay.Text = $"Состояние: {stateText}";
                _lastGameState = currentState;
            }

            if (currentState != State_.Tutorial)
                TutorialHint.Visibility = Visibility.Collapsed;

            bool hasSword = _player.Inventory.HasItem("Sword");
            bool hasPotion = _player.Inventory.HasItem("Potion");
            PlayerInventory.Text = $"Игрок: Меч: {(hasSword ? "✅" : "❌")}, Лекарство: {(hasPotion ? "✅" : "❌")}";

            bool allyHasSword = _ally?.Inventory.HasItem("Sword") == true;
            bool allyHasPotion = _ally?.Inventory.HasItem("Potion") == true;
            AllyInventory.Text = $"Ally: Меч: {(allyHasSword ? "✅" : "❌")}, Лекарство: {(allyHasPotion ? "✅" : "❌")}";
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            _gameManager.OnTutorialKeyPress(e.Key);
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            e.Handled = true;
        }

        private void HandleInput()
        {
            if (_player?.IsAlive != true) return;

            var dir = Vector2D.Zero;
            if (Keyboard.IsKeyDown(Key.W)) dir += new Vector2D(0, -1);
            if (Keyboard.IsKeyDown(Key.S)) dir += new Vector2D(0, 1);
            if (Keyboard.IsKeyDown(Key.A)) dir += new Vector2D(-1, 0);
            if (Keyboard.IsKeyDown(Key.D)) dir += new Vector2D(1, 0);

            // Просто задаем направление. PhysicsEngine сам остановит у стены.
            if (dir.Length() > 0)
            {
                _player.Move(dir);
            }
            else
            {
                _player.Stop();
            }

            if (Keyboard.IsKeyDown(Key.E))
            {
                TryInteract();
            }

            if (Keyboard.IsKeyDown(Key.Escape))
            {
                OnExitToMenu?.Invoke();
            }
        }

        private void TryInteract()
        {
            foreach (var ch in _gameManager.Characters)
            {
                if (ch is Dragon dragon && _player.CanInteractWith(dragon))
                {
                    dragon.Die();
                    _gameManager.OnDragonKilled(byAlly: false);
                    DialogueBox.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            foreach (var ch in _gameManager.Characters)
            {
                if (ch != _player && !(ch is Dragon) && _player.CanInteractWith(ch))
                {
                    _player.Interact(ch);
                    DialogueText.Text = ch.DialogueProvider?.GetDialogueFor(_player) ?? "Здравствуй!";
                    DialogueBox.Visibility = Visibility.Visible;
                    return;
                }
            }
            DialogueBox.Visibility = Visibility.Collapsed;
        }

        public void Restart()
        {
            _gameTimer?.Stop();
            GameArea.Children.Clear();
            _gameManager = null;
            _player = null;
            _ally = null;
            InitializeGame();
            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;
            _gameTimer.Start();
            Focus();
        }
    }
}