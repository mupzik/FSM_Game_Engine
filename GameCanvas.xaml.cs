using GameProj.src;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public enum QuestStatus
    {
        NotStarted,
        Active,
        Completed
    }

    public class Quest
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string StartDialogue { get; private set; }
        public string CompletionDialogue { get; private set; }
        public string AlreadyCompletedDialogue { get; private set; }

        public Dictionary<string, int> RequiredItems { get; private set; }
        public Dictionary<string, int> RewardsItems { get; private set; }
        public float RewardStrength { get; private set; }

        public string RequiredEnemyType { get; private set; }
        public bool RequiresEnemyKill { get; private set; }
        public bool EnemyDefeated { get; set; } = false;

        public QuestStatus Status { get; set; }

        public event Action<Quest> OnQuestStarted;
        public event Action<Quest> OnQuestCompleted;

        public Quest(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
            RequiredItems = new Dictionary<string, int>();
            RewardsItems = new Dictionary<string, int>();
            Status = QuestStatus.NotStarted;
            RequiresEnemyKill = false;
        }

        public Quest SetDialogues(string start, string completion, string alreadyCompleted = null)
        {
            StartDialogue = start;
            CompletionDialogue = completion;
            AlreadyCompletedDialogue = alreadyCompleted ?? "Это задание уже выполнено.";
            return this;
        }

        public Quest AddRequiredItem(string itemId, int amount)
        {
            RequiredItems[itemId] = amount;
            return this;
        }

        public Quest AddRewardItem(string itemId, int amount)
        {
            RewardsItems[itemId] = amount;
            return this;
        }

        public Quest SetRewardStrength(float strength)
        {
            RewardStrength = strength;
            return this;
        }

        public Quest SetRequiredEnemyKill(string enemyType)
        {
            RequiredEnemyType = enemyType;
            RequiresEnemyKill = true;
            return this;
        }

        public void Start()
        {
            if (Status == QuestStatus.NotStarted)
            {
                Status = QuestStatus.Active;
                OnQuestStarted?.Invoke(this);
            }
        }

        public void Complete()
        {
            if (Status == QuestStatus.Active)
            {
                Status = QuestStatus.Completed;
                OnQuestCompleted?.Invoke(this);
            }
        }

        public bool IsComplete(Inventory inventory)
        {
            if (RequiresEnemyKill) return false;
            foreach (var required in RequiredItems)
                if (inventory.GetTotalQuantity(required.Key) < required.Value) return false;
            return true;
        }

        public string GetDialogue(Inventory inventory)
        {
            switch (Status)
            {
                case QuestStatus.NotStarted: return StartDialogue;
                case QuestStatus.Active:
                    if (RequiresEnemyKill && EnemyDefeated) return "Ты победил Горгону? Невероятно! Вот твоя награда!";
                    if (RequiresEnemyKill) return "Горгона всё ещё там... Будь осторожен!";
                    if (IsComplete(inventory)) return CompletionDialogue;
                    else return "Ты еще не собрал все предметы. Приходи, когда соберешь.";
                case QuestStatus.Completed: return AlreadyCompletedDialogue;
                default: return Description;
            }
        }

        public void Reset()
        {
            Status = QuestStatus.NotStarted;
            EnemyDefeated = false;
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public enum State_
    {
        Tutorial, Game, End, DeadEnding, GoodEnding, BadEnding, SecretEnding
    }

    public enum Event_
    {
        TutorialCompleted, PlayerDead, GoodEndingCompleted, BadEndingCompleted, SecretEndingCompleted, GorgonDefeated
    }

    public partial class GameCanvas : UserControl
    {
        public static event Action OnExitToMenu;

        private const int SLIME_GOAL = 5;
        private const double ATTACK_COOLDOWN_TIME = 0.4;
        private const int MAX_ENEMIES = 30;
        private int _currentBeeCount = 0;
        private const int MAX_BEES = 10;
        private const double MIN_ZOOM = 1;
        private const double MAX_ZOOM = 2.0;
        private const double ZOOM_STEP = 0.1;
        private const int MAP_WIDTH = 100;
        private const int MAP_HEIGHT = 100;
        private const double PLAYER_SPEED = 4.0;
        private Queue<string> _dialogueQueue = new Queue<string>();

        private GameManager _gameManager;
        private Player _player;
        private NPC _questAlly, _girl, _schoolGirl, _woman;
        private Enemy _gorgon;
        private NSM_NPC _finn;

        private Dictionary<string, Quest> _availableQuests;
        private Quest _gorgonKillQuest;

        private List<Item> _inventoryItems = new List<Item>();
        private int _selectedInventoryIndex = 0;

        private bool _isQuestLogOpen = false;
        private List<Quest> _activeQuests = new List<Quest>();
        private int _selectedQuestIndex = 0;

        private double _attackCooldown = 0.4;
        private Random _rng = new Random();

        private int _gorgonInteractionCount = 0;
        private double _gorgonLastDirection = 1;
        private bool _gorgonDefeated = false;

        private TranslateTransform _cameraTransform;
        private ScaleTransform _cameraScale;
        private TransformGroup _cameraTransformGroup;
        private double _currentZoom = 1.5;
        private double _shakeIntensity = 0;

        private string _tilesPath, _spritesPath, _itemsPath, _mapsPath, _soundsPath, _musicPath, _uiPath;
        private Dictionary<string, Item> _itemPrefabs;
        private Dictionary<string, ImageSource> _iconCache = new Dictionary<string, ImageSource>();

        private DispatcherTimer _gameTimer;
        private DispatcherTimer _spawnTimer;
        private bool _tutorialCompleted = false;

        private StreamWriter _logWriter;
        private string _logFilePath;

        public bool IsUIOpen => InventoryPanel.Visibility == Visibility.Visible || DialogueBox.Visibility == Visibility.Visible;

        public GameCanvas()
        {
            InitializeComponent();
            Focusable = true;
            Loaded += OnLoaded;
            InitializeLogging();
        }

        private void InitializeLogging()
        {
            string logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(logsDir)) Directory.CreateDirectory(logsDir);

            _logFilePath = Path.Combine(logsDir, $"game_log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt");
            try
            {
                _logWriter = new StreamWriter(_logFilePath, true);
                _logWriter.AutoFlush = true;
                WriteLog("ИГРА ЗАПУЩЕНА");
                WriteLog($"Время: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize log: {ex.Message}");
            }
        }

        private void WriteLog(string message)
        {
            try
            {
                _logWriter?.WriteLine($"{message}");
            }
            catch { }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializePaths();
            InitializeItemPrefabs();
            InitializeCamera();
            SizeChanged += OnSizeChanged;
            PreviewMouseWheel += OnMouseWheel;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                InitializeGame();
                Focus();
            }), DispatcherPriority.Background);
        }

        private void InitializePaths()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _tilesPath = Path.Combine(baseDir, "Tiles");
            _spritesPath = Path.Combine(baseDir, "Sprites");
            _itemsPath = Path.Combine(baseDir, "Items");
            _mapsPath = Path.Combine(baseDir, "Maps");
            _soundsPath = Path.Combine(baseDir, "Sounds");
            _musicPath = Path.Combine(baseDir, "Music");
            _uiPath = Path.Combine(baseDir, "UI");

            foreach (var path in new[] { _tilesPath, _spritesPath, _itemsPath, _mapsPath, _soundsPath, _musicPath, _uiPath })
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        private void InitializeItemPrefabs()
        {
            _itemPrefabs = new Dictionary<string, Item>
            {
                { "slime_goo", new Item("slime_goo", "Слизь", "Холодная и липкая.", "Items/slime_goo.png", true, 1) },
                { "sword", new Item("sword", "Меч Силы", "Больше похож на кухонный нож, но все равно бьет больно.", "Items/sword.png", false, 1) },
                { "health_potion", new Item("health_potion", "Зелье здоровья", "Восстанавливает 50 HP.", "Items/health_potion.png", true, 1) },
                { "belt", new Item("belt", "Пояс", "Издалека похож на знак \"Стоп\".", "Items/belt.png", false, 1) },
                { "black_bottle", new Item("black_bottle", "Черная жижа", "Фу, гадость.", "Items/black_bottle.png", true, 1) },
                { "necklace", new Item("necklace", "Ожерелье", "Симпатичное.", "Items/necklace.png", false, 1) },
                { "note", new Item("note", "Записка", "Я читать не умею вообще-то.", "Items/necklace.png", false, 1) },
                { "cheese", new Item("cheese", "Сыр", "Немного дор-блю.", "Items/cheese.png", false, 1) },
                { "rock", new Item("rock", "Камень", "Камень.", "Items/rock.png", false, 1) },
                { "silver_thing", new Item("silver_thing", "Серебряная сфера", "Блестит.", "Items/silver_thing.png", false, 1) },
                { "yellow_thing", new Item("yellow_thing", "Золотистая сфера", "Ого как блестит.", "Items/yellow_thing.png", false, 1) },
                { "sword_part", new Item("sword_part", "Ручка от меча", "Ну и куда её?", "Items/sword_part.png", false, 1) },
                { "tail", new Item("tail", "Чей-то хвост", "Бедная корова?", "Items/tail.png", false, 1) },
                { "web", new Item("web", "Паутина", "Здесь же пауков нет...", "Items/web.png", true, 1) },
                { "honey", new Item("honey", "Мёд", "Сладкий и липкий.", "Items/honey.png", true, 1) },
            };
        }

        private void InitializeCamera()
        {
            _cameraTransform = new TranslateTransform();
            _cameraScale = new ScaleTransform(_currentZoom, _currentZoom);
            _cameraTransformGroup = new TransformGroup();
            _cameraTransformGroup.Children.Add(_cameraScale);
            _cameraTransformGroup.Children.Add(_cameraTransform);
            GameArea.RenderTransform = _cameraTransformGroup;
        }

        private void InitializeGame()
        {
            _availableQuests = new Dictionary<string, Quest>();
            _gorgonInteractionCount = 0;
            _gorgonDefeated = false;
            _inventoryItems.Clear();
            _selectedInventoryIndex = 0;
            _currentBeeCount = 0;
            _dialogueQueue.Clear();
            _activeQuests.Clear();
            _selectedQuestIndex = 0;
            _isQuestLogOpen = false;

            _gameManager = new GameManager(this, MAP_WIDTH, MAP_HEIGHT, InitializeMap);
            InitializeGameStates();
            InitializePlayer();
            InitializeQuestAlly();
            InitializeGirl();
            InitializeSchoolGirl();
            InitializeWoman();
            InitializeGorgon();
            InitializeFinn();

            SubscribeInventoryEvents();
            StartSpawnTimer();

            WriteLog("начальное состояние Tutorial");
            _gameManager.SetState(State_.Tutorial);
            StartGameLoop();
        }

        private void InitializeGameStates()
        {
            var tutorialState = new State<State_, Event_>(State_.Tutorial);
            var gameState = new State<State_, Event_>(State_.Game);
            var deadEndingState = new State<State_, Event_>(State_.DeadEnding);
            var goodEndingState = new State<State_, Event_>(State_.GoodEnding);
            var badEndingState = new State<State_, Event_>(State_.BadEnding);
            var secretEndingState = new State<State_, Event_>(State_.SecretEnding);

            tutorialState.SetEventHandler((machine, ev) =>
            {
                if (ev == Event_.TutorialCompleted)
                {
                    WriteLog($"отправлено событие {ev}");
                    WriteLog($"переход Tutorial - Game");
                    machine.SetState(gameState);
                }
            });

            gameState.SetEventHandler((machine, ev) =>
            {
                WriteLog($"отправлено событие {ev}");
                switch (ev)
                {
                    case Event_.PlayerDead:
                        WriteLog($"переход Game - DeadEnding");
                        machine.SetState(deadEndingState);
                        break;
                    case Event_.GorgonDefeated:
                        WriteLog($"переход Game - GoodEnding");
                        machine.SetState(goodEndingState);
                        break;
                    case Event_.GoodEndingCompleted:
                        WriteLog($"переход Game - GoodEnding");
                        machine.SetState(goodEndingState);
                        break;
                    case Event_.BadEndingCompleted:
                        WriteLog($"переход Game - BadEnding");
                        machine.SetState(badEndingState);
                        break;
                    case Event_.SecretEndingCompleted:
                        WriteLog($"переход Game - SecretEnding");
                        machine.SetState(secretEndingState);
                        break;
                }
            });

            deadEndingState.SetEnter(() => Dispatcher.Invoke(() =>
            {
                WriteLog($"переход в DeadEnding (игрок умер)");
                GameOver(false, "ВЫ УМЕРЛИ!\n\nНажмите R для рестарта");
            }));

            goodEndingState.SetEnter(() => Dispatcher.Invoke(() =>
            {
                WriteLog($"переход в GoodEnding (победа)");
                GameOver(true, "ПОБЕДА!\n\nНажмите R для рестарта");
            }));

            badEndingState.SetEnter(() => Dispatcher.Invoke(() =>
            {
                WriteLog($"переход в BadEnding (плохая концовка)");
                GameOver(false, "ПЛОХАЯ КОНЦОВКА\n\nНажмите R для рестарта");
            }));

            secretEndingState.SetEnter(() => Dispatcher.Invoke(() =>
            {
                WriteLog($"переход в SecretEnding (секретная концовка)");
                GameOver(true, "СЕКРЕТНАЯ КОНЦОВКА\n\nНажмите R для рестарта");
            }));

            _gameManager._tutorialState = new CompositeState<State_, Event_>(State_.Tutorial, State_.Tutorial);
            _gameManager._gameState = new CompositeState<State_, Event_>(State_.Game, State_.Game);

            var endComposite = new CompositeState<State_, Event_>(State_.End, State_.DeadEnding);
            endComposite.AddSubState(deadEndingState);
            endComposite.AddSubState(goodEndingState);
            endComposite.AddSubState(badEndingState);
            endComposite.AddSubState(secretEndingState);
            _gameManager._endState = endComposite;

            _gameManager._tutorialState.AddSubState(tutorialState);
            _gameManager._gameState.AddSubState(gameState);
            _gameManager.InitializeFSM(_gameManager._tutorialState);
        }

        public void GameOver(bool isWin, string message)
        {
            _gameTimer?.Stop();
            _spawnTimer?.Stop();
            GameOverText.Text = message;
            GameOverText.Foreground = isWin ? Brushes.Gold : Brushes.White;
            GameOverBox.Visibility = Visibility.Visible;
            _gameManager?.StopMusic();
        }

        private void StartGameLoop()
        {
            _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _gameTimer.Tick += OnGameTick;
            _gameTimer.Start();
        }

        private void InitializePlayer()
        {
            string mcSpritePath = Path.Combine(_spritesPath, "MC.png");
            _player = new Player(new Vector2D(48 * 32, 48 * 32), "Player", 100, PLAYER_SPEED,
                spritePath: mcSpritePath, visualScale: 1.0, spriteInfo: new SpriteInfo("MC", 48, 48));
            _player.Strength = 15f;
            _gameManager.AddCharacter(_player);
        }

        private void InitializeQuestAlly()
        {
            string allySpritePath = Path.Combine(_spritesPath, "MC.png");
            _questAlly = new NPC(_gameManager.Grid, new Vector2D(46 * 32, 48 * 32), "MC",
                0, 100f, 0f, spritePath: allySpritePath, visualScale: 1.0, spriteInfo: new SpriteInfo("MC", 48, 48));
            _questAlly.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_questAlly);

            var slimeQuest = new Quest("slime_quest", "Сбор слизи", "Принеси 5 бутылочек со слизью")
                .SetDialogues("Привет! Мне нужна помощь! Надо победить пять слизней, чтобы слизь с них получить, а я не могу!",
                    "Ухты, разобрался, как сражаться? А у меня не получилось :( Держи мой меч, тебе нужнее будет",
                    "На северо-запад пойдешь - деревню найдешь!")
                .AddRequiredItem("slime_goo", SLIME_GOAL).AddRewardItem("sword", 1).SetRewardStrength(15);

            slimeQuest.OnQuestCompleted += (q) =>
            {
                _player.MaxHealth += 20;
                _player.Heal(20);
            };

            _availableQuests.Add("slime_quest", slimeQuest);
        }

        private void InitializeGirl()
        {
            string girlSpritePath = Path.Combine(_spritesPath, "Girl.png");
            _girl = new NPC(_gameManager.Grid, new Vector2D(18 * 32, 4 * 32), "Girl",
                0, 100f, 0f, spritePath: girlSpritePath, visualScale: 1.0, spriteInfo: new SpriteInfo("Girl", 48, 48));
            _girl.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_girl);

            var firstGirlQuest = new Quest("girl_quest_first", "Странная просьба", "Принеси черную жижу и 10 слизей")
                .SetDialogues(
                    "Пожалуйста, помоги мне! Мне нужна черная жижа и 10 слизей. Я знаю, это странно, но это очень важно!",
                    "Спасибо! Ты принес черную жижу и 10 слизей. Вот, держи ожерелье, его кто-то, наверное, потерял. Но есть еще одна просьба..."
                )
                .AddRequiredItem("black_bottle", 1)
                .AddRequiredItem("slime_goo", 10)
                .AddRewardItem("necklace", 1)
                .SetRewardStrength(50);

            var secondGirlQuest = new Quest("girl_quest_second", "Магическая желтая сфера", "Принеси магическую сферу")
                .SetDialogues(
                    "Спасибо еще раз! Принеси магическую сферу, если найдешь. Говорят, они где-то в этом лесу...",
                    "Ура! Это именно то, что мне нужно! Спасибо! О нет... Кажется, я разбудила древнее зло! Берегись!",
                    "Спасибо, что помог мне с магической сферой!"
                )
                .AddRequiredItem("yellow_thing", 1)
                .AddRewardItem("cheese", 3)
                .SetRewardStrength(15);

            secondGirlQuest.OnQuestStarted += (q) =>
            {
                q.RequiredItems.Clear();
            };

            firstGirlQuest.OnQuestCompleted += (q) =>
            {
                if (!_availableQuests.ContainsKey("girl_quest_second"))
                {
                    _availableQuests.Add("girl_quest_second", secondGirlQuest);
                }
                if (_isQuestLogOpen) RefreshQuestLog();
            };

            secondGirlQuest.OnQuestCompleted += (q) =>
            {
                foreach (var required in q.RequiredItems)
                {
                    if (required.Key == "yellow_thing" && _player.Inventory.GetTotalQuantity("yellow_thing") >= required.Value)
                        SpawnGiantDarkSlime(true);
                    if (required.Key == "silver_thing" && _player.Inventory.GetTotalQuantity("silver_thing") >= required.Value)
                        SpawnGiantDarkSlime(false);
                }
            };

            _availableQuests.Add("girl_quest_first", firstGirlQuest);
        }

        private void SpawnGiantDarkSlime(bool hasYellowSphere)
        {
            Vector2D spawnPos = new Vector2D(20 * 32 + 16, 10 * 32 + 16);

            string giantSlimeSpritePath = Path.Combine(_spritesPath, "DarkSlime.png");
            if (!File.Exists(giantSlimeSpritePath))
            {
                giantSlimeSpritePath = Path.Combine(_spritesPath, "Slime.png");
            }

            float slimeHealth = 500f;
            float slimeStrength = 150f;

            if (hasYellowSphere)
            {
                slimeHealth = 700f;
                slimeStrength = 200f;
            }
            else
            {
                slimeHealth = 300f;
                slimeStrength = 80f;
            }

            Enemy giantDarkSlime = new Enemy(_gameManager.Grid, spawnPos, 1.2,
                "GiantDarkSlime", slimeHealth, slimeStrength, giantSlimeSpritePath, 5.0,
                new SpriteInfo("DarkSlime", 48, 48), "GiantDarkSlime",
                hitboxRadius: 100.0,
                collisionRadius: 30.0);

            SetupGiantDarkSlimeBehavior(giantDarkSlime);
            _gameManager.AddCharacter(giantDarkSlime);

            _gameManager.PlaySound("attack.mp3", 1.0f);
            TriggerShake(10.0);
        }

        private void SetupGiantDarkSlimeBehavior(Enemy giantSlime)
        {
            if (giantSlime.Type != "GiantDarkSlime") return;

            double lastAttackTime = 0;
            const double ATTACK_COOLDOWN = 5.0;

            giantSlime.ConfigureState(CharacterState.Dead, onEnter: () =>
            {
                giantSlime.Stop();
                _gameManager.SendEvent(Event_.SecretEndingCompleted);
                DropItem("black_bottle", giantSlime.Position);
                DropItem("slime_goo", giantSlime.Position);
                DropItem("yellow_thing", giantSlime.Position);
                DropItem("cheese", giantSlime.Position);
                _gameManager.RemoveCharacter(giantSlime);
            });

            giantSlime.ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                if (!giantSlime.IsAlive) { giantSlime.SetState(CharacterState.Dead); return; }
                giantSlime.Stop();
                if (_player != null && _player.IsAlive && Vector2D.Distance(giantSlime.Position, _player.Position) < 400.0)
                {
                    giantSlime.SetState(CharacterState.Chase);
                }
            });

            giantSlime.ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (!giantSlime.IsAlive) { giantSlime.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { giantSlime.SetState(CharacterState.Idle); return; }

                double dist = Vector2D.Distance(giantSlime.Position, _player.Position);
                if (dist < 100.0)
                {
                    giantSlime.SetState(CharacterState.Attack);
                }
                else
                {
                    Vector2D direction = (_player.Position - giantSlime.Position).Normalize();
                    giantSlime.Move(direction);
                }
            });

            giantSlime.ConfigureState(CharacterState.Attack,
                onEnter: () => { giantSlime.Stop(); },
                update: (machine) =>
                {
                    if (!giantSlime.IsAlive) { giantSlime.SetState(CharacterState.Dead); return; }
                    if (_player == null || !_player.IsAlive) { giantSlime.SetState(CharacterState.Idle); return; }

                    double dist = Vector2D.Distance(giantSlime.Position, _player.Position);
                    if (dist > 150.0) { giantSlime.SetState(CharacterState.Chase); return; }

                    double currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
                    if (currentTime - lastAttackTime >= ATTACK_COOLDOWN)
                    {
                        lastAttackTime = currentTime;
                        giantSlime.Attack(_player);
                        TriggerShake(15.0);
                        ShowFloatingDamageNumber(_player.Position, 150f, false);
                        _gameManager.PlaySound("attack.mp3", 1.0f);

                        if (!_player.IsAlive)
                        {
                            _gameManager.SendEvent(Event_.BadEndingCompleted);
                        }
                    }
                    giantSlime.SetState(CharacterState.Chase);
                });

            giantSlime.SetState(CharacterState.Idle);
        }

        private void InitializeSchoolGirl()
        {
            string schoolGirlSpritePath = Path.Combine(_spritesPath, "SchoolGirl.png");
            _schoolGirl = new NPC(_gameManager.Grid, new Vector2D(30 * 32, 35 * 32), "SchoolGirl",
                0, 100f, 0f, spritePath: schoolGirlSpritePath, visualScale: 0.8, spriteInfo: new SpriteInfo("SchoolGirl", 128, 128));
            _schoolGirl.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_schoolGirl);

            var schoolGirlQuest = new Quest("schoolgirl_quest", "Потерянное ожерелье", "Найди ожерелье")
                .SetDialogues("Привет! Я потеряла свое любимое ожерелье. Ты не мог бы его найти для меня?",
                    "Ура! Ты нашел мое ожерелье! Спасибо большое! Держи 5 зелий лечения в качестве награды.",
                    "Спасибо, что нашел мое ожерелье! Я так рада!")
                .AddRequiredItem("necklace", 1).AddRewardItem("health_potion", 5).SetRewardStrength(0);
            _availableQuests.Add("schoolgirl_quest", schoolGirlQuest);
        }

        private void InitializeWoman()
        {
            string womanSpritePath = Path.Combine(_spritesPath, "Woman.png");
            _woman = new NPC(_gameManager.Grid, new Vector2D(70 * 32, 65 * 32), "Woman",
                0, 100f, 0f, spritePath: womanSpritePath, visualScale: 1.0, spriteInfo: new SpriteInfo("Woman", 48, 48));
            _woman.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_woman);

            var womanQuest = new Quest("woman_quest", "Нужен пояс", "Найди пояс для платья")
                .SetDialogues("Здравствуйте! У меня проблема - порвался пояс на платье. Я потеряла его где-то в этом лесу. Не могли бы вы найти его для меня?",
                    "О, какой красивый пояс! Спасибо вам огромное! Я чувствую себя намного лучше!",
                    "Спасибо за пояс! Теперь мое платье снова в порядке.")
                .AddRequiredItem("belt", 1).SetRewardStrength(0);

            womanQuest.OnQuestCompleted += (q) =>
            {
                _player.MaxHealth += 100;
                _player.Heal(100);
            };

            _availableQuests.Add("woman_quest", womanQuest);
        }

        private void InitializeGorgon()
        {
            string GorgonSpritePath = Path.Combine(_spritesPath, "Gorgon.png");
            _gorgon = new Enemy(
                grid: _gameManager.Grid,
                position: new Vector2D(82 * 32, 79 * 32),
                speed: 2,
                id: "Gorgon",
                health: 100f,
                strength: 20f,
                spritePath: GorgonSpritePath,
                visualScale: 1.0,
                spriteInfo: new SpriteInfo("Gorgon", 128, 128),
                type: "Gorgon");

            SetupGorgonBehavior(_gorgon);
            _gameManager.AddCharacter(_gorgon);

            var gorgonNecklaceQuest = new Quest("gorgon_necklace_quest", "Ожерелье для горгоны", "Отдай горгоне ожерелье")
                .SetDialogues("Ты пришел ко мне? У тебя есть ожерелье? Отдай его мне...",
                    "Спасибо за ожерелье... Оно было моим когда-то давно. Возьми эту серебряную сферу в благодарность.",
                    "Спасибо за ожерелье... Теперь я спокойна.")
                .AddRequiredItem("necklace", 1).AddRewardItem("silver_thing", 1).SetRewardStrength(0);

            if (!_availableQuests.ContainsKey("gorgon_necklace_quest"))
            {
                _availableQuests.Add("gorgon_necklace_quest", gorgonNecklaceQuest);
            }
        }

        private void InitializeFinn()
        {
            string finnSpritePath = Path.Combine(_spritesPath, "Orc.png");
            _finn = new NSM_NPC(_gameManager.Grid, new Vector2D(25 * 32, 25 * 32), "Finn",
                2.5, 60f, 8f, 1.0, spritePath: finnSpritePath, spriteInfo: new SpriteInfo("Orc", 48, 48));

            _gorgonKillQuest = new Quest("gorgon_kill_quest", "Убийство Горгоны", "Победи древнее чудовище - Горгону")
                .SetDialogues("Ты в юго-восточный лес не ходи, там опасно - Горгона живет.",
                    "Невероятно! Ты действительно победил Горгону!",
                    "Спасибо, что избавил нас от Горгоны! Теперь мы можем жить в безопасности.")
                .SetRequiredEnemyKill("Gorgon").SetRewardStrength(0);

            _availableQuests.Add("gorgon_kill_quest", _gorgonKillQuest);

            ConfigureFinnStates();
            _finn.SetState(CharacterState.Decision);
            _gameManager.AddCharacter(_finn);
        }

        private void SetupSlimeBehavior(Enemy slime)
        {
            if (slime.Type != "Slime") return;

            slime.ConfigureState(CharacterState.Dead, onEnter: () =>
            {
                slime.Stop();
                DropItem("slime_goo", slime.Position);
                _gameManager.RemoveCharacter(slime);
            });

            slime.ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                if (!slime.IsAlive) { slime.SetState(CharacterState.Dead); return; }
                slime.Stop();
                if (_player != null && _player.IsAlive && Vector2D.Distance(slime.Position, _player.Position) < 150.0)
                    slime.SetState(CharacterState.Chase);
            });

            slime.ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (!slime.IsAlive) { slime.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { slime.SetState(CharacterState.Idle); return; }
                double dist = Vector2D.Distance(slime.Position, _player.Position);
                if (dist < 40.0) slime.SetState(CharacterState.Attack);
                else slime.Move((_player.Position - slime.Position).Normalize());
            });

            slime.ConfigureState(CharacterState.Attack, update: (machine) =>
            {
                if (!slime.IsAlive) { slime.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { slime.SetState(CharacterState.Idle); return; }
                slime.Stop();
                double dist = Vector2D.Distance(slime.Position, _player.Position);
                if (dist > 50.0) slime.SetState(CharacterState.Chase);
                else if (_rng.NextDouble() < 0.05)
                {
                    slime.Attack(_player);
                    TriggerShake(3.0);
                    ShowFloatingDamageNumber(_player.Position, slime.Strength, false);
                    if (!_player.IsAlive) _gameManager.SendEvent(Event_.PlayerDead);
                }
            });

            slime.SetState(CharacterState.Idle);
        }

        private void SetupDarkSlimeBehavior(Enemy darkSlime)
        {
            if (darkSlime.Type != "DarkSlime") return;

            darkSlime.ConfigureState(CharacterState.Dead, onEnter: () =>
            {
                darkSlime.Stop();
                DropItem("black_bottle", darkSlime.Position);
                _gameManager.RemoveCharacter(darkSlime);
            });

            darkSlime.ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                if (!darkSlime.IsAlive) { darkSlime.SetState(CharacterState.Dead); return; }
                darkSlime.Stop();
                if (_player != null && _player.IsAlive && Vector2D.Distance(darkSlime.Position, _player.Position) < 150.0)
                    darkSlime.SetState(CharacterState.Chase);
            });

            darkSlime.ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (!darkSlime.IsAlive) { darkSlime.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { darkSlime.SetState(CharacterState.Idle); return; }
                double dist = Vector2D.Distance(darkSlime.Position, _player.Position);
                if (dist < 40.0) darkSlime.SetState(CharacterState.Attack);
                else darkSlime.Move((_player.Position - darkSlime.Position).Normalize());
            });

            darkSlime.ConfigureState(CharacterState.Attack, update: (machine) =>
            {
                if (!darkSlime.IsAlive) { darkSlime.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { darkSlime.SetState(CharacterState.Idle); return; }
                darkSlime.Stop();
                double dist = Vector2D.Distance(darkSlime.Position, _player.Position);
                if (dist > 50.0) darkSlime.SetState(CharacterState.Chase);
                else if (_rng.NextDouble() < 0.05)
                {
                    darkSlime.Attack(_player);
                    TriggerShake(3.0);
                    ShowFloatingDamageNumber(_player.Position, darkSlime.Strength, false);
                    if (!_player.IsAlive) _gameManager.SendEvent(Event_.PlayerDead);
                }
            });

            darkSlime.SetState(CharacterState.Idle);
        }

        private void SetupBeeBehavior(Enemy bee)
        {
            if (bee.Type != "Bee") return;

            bee.ConfigureState(CharacterState.Dead, onEnter: () =>
            {
                bee.Stop();
                DropItem("honey", bee.Position);
                _gameManager.RemoveCharacter(bee);
                _currentBeeCount--;
            });

            bee.ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                if (!bee.IsAlive) { bee.SetState(CharacterState.Dead); return; }
                if (_rng.NextDouble() < 0.02)
                {
                    double angle = _rng.NextDouble() * Math.PI * 2;
                    bee.Move(new Vector2D(Math.Cos(angle), Math.Sin(angle)));
                }
                if (_player != null && _player.IsAlive && Vector2D.Distance(bee.Position, _player.Position) < 200.0)
                    bee.SetState(CharacterState.Chase);
            });

            bee.ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (!bee.IsAlive) { bee.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { bee.SetState(CharacterState.Idle); return; }
                double dist = Vector2D.Distance(bee.Position, _player.Position);
                if (dist < 35.0) bee.SetState(CharacterState.Attack);
                else
                {
                    Vector2D direction = (_player.Position - bee.Position).Normalize();
                    if (_rng.NextDouble() < 0.3)
                    {
                        direction += new Vector2D((_rng.NextDouble() - 0.5) * 0.5, (_rng.NextDouble() - 0.5) * 0.5);
                        direction = direction.Normalize();
                    }
                    bee.Move(direction);
                }
            });

            bee.ConfigureState(CharacterState.Attack, update: (machine) =>
            {
                if (!bee.IsAlive) { bee.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { bee.SetState(CharacterState.Idle); return; }
                bee.Stop();
                double dist = Vector2D.Distance(bee.Position, _player.Position);
                if (dist > 45.0) bee.SetState(CharacterState.Chase);
                else if (_rng.NextDouble() < 0.08)
                {
                    bee.Attack(_player);
                    TriggerShake(2.0);
                    ShowFloatingDamageNumber(_player.Position, bee.Strength, false);
                    if (!_player.IsAlive) _gameManager.SendEvent(Event_.PlayerDead);
                }
            });

            bee.SetState(CharacterState.Idle);
        }

        private void SetupGorgonBehavior(Enemy gorgon)
        {
            if (gorgon.Type != "Gorgon") return;

            double lastAttackTime = 0;
            const double ATTACK_COOLDOWN = 1.5;

            gorgon.ConfigureState(CharacterState.Dead, onEnter: () =>
            {
                gorgon.Stop();

                bool necklaceQuestCompleted = false;
                if (_availableQuests.TryGetValue("gorgon_necklace_quest", out var gorgonQuest))
                {
                    necklaceQuestCompleted = (gorgonQuest.Status == QuestStatus.Completed);
                }

                if (necklaceQuestCompleted)
                {
                    DropItem("necklace", gorgon.Position);
                }
                else
                {
                    DropItem("yellow_thing", gorgon.Position);
                }

                _gorgonDefeated = true;

                if (_gorgonKillQuest?.Status == QuestStatus.Active)
                {
                    _gorgonKillQuest.EnemyDefeated = true;
                    if (_isQuestLogOpen) RefreshQuestLog();
                }

                _gameManager.RemoveCharacter(gorgon);
                if (gorgon == _gorgon) _gorgon = null;
            });

            gorgon.ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                if (!gorgon.IsAlive) { gorgon.SetState(CharacterState.Dead); return; }
                gorgon.Stop();
            });

            gorgon.ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (!gorgon.IsAlive) { gorgon.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { gorgon.SetState(CharacterState.Idle); return; }
                double dist = Vector2D.Distance(gorgon.Position, _player.Position);
                if (dist < 40.0) gorgon.SetState(CharacterState.Attack);
                else gorgon.Move((_player.Position - gorgon.Position).Normalize());
            });

            gorgon.ConfigureState(CharacterState.Attack, update: (machine) =>
            {
                if (!gorgon.IsAlive) { gorgon.SetState(CharacterState.Dead); return; }
                if (_player == null || !_player.IsAlive) { gorgon.SetState(CharacterState.Idle); return; }
                double dist = Vector2D.Distance(gorgon.Position, _player.Position);
                if (dist > 70.0) { gorgon.SetState(CharacterState.Chase); return; }
                double currentTime = DateTime.Now.TimeOfDay.TotalSeconds;
                if (currentTime - lastAttackTime >= ATTACK_COOLDOWN)
                {
                    lastAttackTime = currentTime;
                    gorgon.Attack(_player);
                    TriggerShake(5.0);
                    ShowFloatingDamageNumber(_player.Position, 20f, false);
                    _gameManager.PlaySound("attack.mp3", 0.8f);
                    if (!_player.IsAlive) _gameManager.SendEvent(Event_.PlayerDead);
                }
                gorgon.SetState(CharacterState.Chase);
            });

            gorgon.SetState(CharacterState.Idle);
        }

        private void ConfigureFinnStates()
        {
            _finn.ConfigureState(CharacterState.Dead, onEnter: () =>
            {
                _finn.Stop();
                DropItem("health_potion", _finn.Position);
                _gameManager.RemoveCharacter(_finn);
            });

            _finn.ConfigureState(CharacterState.Idle,
                onEnter: () => _finn.Stop(),
                update: (machine) =>
                {
                    if (!_finn.IsAlive) { _finn.SetState(CharacterState.Dead); return; }
                    if (_rng.NextDouble() < 0.01)
                    {
                        double angle = _rng.NextDouble() * Math.PI * 2;
                        _finn.Move(new Vector2D(Math.Cos(angle), Math.Sin(angle)));
                    }
                    Enemy nearbyEnemy = FindNearestEnemy(120);
                    if (nearbyEnemy != null) { _finn.SetState(CharacterState.Chase); return; }
                    if (_rng.NextDouble() < 0.02) _finn.SetState(CharacterState.Decision);
                });

            _finn.ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (!_finn.IsAlive) { _finn.SetState(CharacterState.Dead); return; }
                Enemy target = FindNearestEnemy(250);
                if (target == null || !target.IsAlive) { _finn.SetState(CharacterState.Decision); return; }
                double dist = Vector2D.Distance(_finn.Position, target.Position);
                if (dist < 45) _finn.SetState(CharacterState.Attack);
                else
                {
                    _finn.Move((target.Position - _finn.Position).Normalize());
                    if (_rng.NextDouble() < 0.005) _finn.SetState(CharacterState.Decision);
                }
            });

            _finn.ConfigureState(CharacterState.Attack,
                onEnter: () => _finn.Stop(),
                update: (machine) =>
                {
                    if (!_finn.IsAlive) { _finn.SetState(CharacterState.Dead); return; }
                    Enemy target = FindNearestEnemy(60);
                    if (target != null && target.IsAlive)
                    {
                        target.TakeDamage(_finn.Strength);
                        ShowFloatingDamageNumber(target.Position, _finn.Strength, false);
                    }
                    _finn.SetState(CharacterState.Decision);
                });

            _finn.AddTransition(CharacterState.Idle, CharacterState.Chase, 0.30);
            _finn.AddTransition(CharacterState.Idle, CharacterState.Idle, 0.70);
            _finn.AddTransition(CharacterState.Chase, CharacterState.Idle, 0.20);
            _finn.AddTransition(CharacterState.Chase, CharacterState.Attack, 0.25);
            _finn.AddTransition(CharacterState.Chase, CharacterState.Chase, 0.55);
            _finn.AddTransition(CharacterState.Attack, CharacterState.Idle, 0.30);
            _finn.AddTransition(CharacterState.Attack, CharacterState.Chase, 0.70);
        }

        private void SpawnBee(Vector2D spawnPos)
        {
            if (_currentBeeCount >= MAX_BEES) return;

            string beeSpritePath = Path.Combine(_spritesPath, "Bee.png");
            Enemy bee = new Enemy(_gameManager.Grid, spawnPos, 2.8,
                $"Bee_{DateTime.Now.Ticks}", 20f, 6f, beeSpritePath, 0.8,
                new SpriteInfo("Bee", 48, 48), "Bee");

            SetupBeeBehavior(bee);
            _gameManager.AddCharacter(bee);
            _currentBeeCount++;
        }

        private void SpawnRandomEnemy()
        {
            if (_gameManager == null) return;
            int currentEnemies = _gameManager.Characters.Count(c => c is Enemy && c.IsAlive);
            if (currentEnemies >= MAX_ENEMIES) return;

            Vector2D spawnPos = GetRandomSpawnPosition();
            int tileX = (int)(spawnPos.X / 32);
            int tileY = (int)(spawnPos.Y / 32);

            const int HALF_MAP = 50;
            const int BOTTOM_AREA_START_Y = 75;

            bool isBottomLeftSquare = (tileX < HALF_MAP && tileY > BOTTOM_AREA_START_Y);
            bool isBottomLeftTriangle = (tileX < HALF_MAP && tileY >= HALF_MAP && tileY <= BOTTOM_AREA_START_Y);
            bool isTopRight = (tileX >= HALF_MAP && tileY < HALF_MAP);

            if (isBottomLeftSquare)
            {
                string darkSlimeSpritePath = Path.Combine(_spritesPath, "DarkSlime.png");
                if (!File.Exists(darkSlimeSpritePath))
                    darkSlimeSpritePath = Path.Combine(_spritesPath, "Slime.png");

                Enemy darkSlime = new Enemy(_gameManager.Grid, spawnPos, 1.5,
                    $"DarkSlime_{DateTime.Now.Ticks}", 35f, 6f, darkSlimeSpritePath, 1.0,
                    new SpriteInfo("DarkSlime", 48, 48), "DarkSlime");
                SetupDarkSlimeBehavior(darkSlime);
                _gameManager.AddCharacter(darkSlime);
            }
            else if (isTopRight && _currentBeeCount < MAX_BEES)
            {
                SpawnBee(spawnPos);
            }
            else if (isBottomLeftTriangle)
            {
                string slimeSpritePath = Path.Combine(_spritesPath, "Slime.png");
                Enemy slime = new Enemy(_gameManager.Grid, spawnPos, 1.5,
                    $"Slime_{DateTime.Now.Ticks}", 30f, 4f, slimeSpritePath, 1.0,
                    new SpriteInfo("Slime", 48, 48), "Slime");
                SetupSlimeBehavior(slime);
                _gameManager.AddCharacter(slime);
            }
            else
            {
                string slimeSpritePath = Path.Combine(_spritesPath, "Slime.png");
                Enemy slime = new Enemy(_gameManager.Grid, spawnPos, 1.5,
                    $"Slime_{DateTime.Now.Ticks}", 30f, 4f, slimeSpritePath, 1.0,
                    new SpriteInfo("Slime", 48, 48), "Slime");
                SetupSlimeBehavior(slime);
                _gameManager.AddCharacter(slime);
            }
        }

        private void RotateGorgonToPlayer()
        {
            if (_player == null || _gorgon == null) return;
            Vector2D direction = _player.Position - _gorgon.Position;
            if (direction.X > 0 && _gorgonLastDirection != 1)
            {
                _gorgonLastDirection = 1;
                _gameManager?.FlipHorizontally(_gorgon, false);
            }
            else if (direction.X < 0 && _gorgonLastDirection != -1)
            {
                _gorgonLastDirection = -1;
                _gameManager?.FlipHorizontally(_gorgon, true);
            }
        }

        private void InitializeMap(GameManager gm)
        {
            var backgroundMappings = new Dictionary<char, (TileType, string)>
            {
                { 'g', (TileType.Floor, "Grass") }, { 'G', (TileType.Wall, "Grass") },
                { 'r', (TileType.Floor, "Road_r2") }, { 'l', (TileType.Floor, "Road_l") },
                { 'u', (TileType.Floor, "Road_u2") }, { 'd', (TileType.Floor, "Road_d") },
                { 's', (TileType.Floor, "Road_s") }, { 'R', (TileType.Floor, "Road") },
                { 'S', (TileType.Floor, "Road2") }
            };
            var largeDecorMappings = new Dictionary<char, (TileType type, string spriteId, int width, int height)>
            {
                { 'T', (TileType.Wall, "Tree1", 2, 2) }, { 'H', (TileType.Wall, "House", 7, 6) },
                { 'r', (TileType.Wall, "Rock5", 3, 3) }, { 'R', (TileType.Wall, "Rock7", 2, 1) },
                { 't', (TileType.Wall, "Bush2", 2, 1) }, { 'p', (TileType.Wall, "Bush1", 1, 1) },
                { 'a', (TileType.Wall, "Dragon_bones", 10, 10) }, {'F', (TileType.Floor, "F_hint", 2, 2) },
                { 'W', (TileType.Floor, "W_hint", 5, 5) }, { 'S', (TileType.Floor, "S_hint", 5,5) },
                { 'A', (TileType.Floor, "A_hint", 5,5) }, { 'D', (TileType.Floor, "D_hint", 5,5) },
                { 'J', (TileType.Floor, "J_hint", 5,5) }, { 'I', (TileType.Floor, "i_hint", 5,5) },
                { 'щ', (TileType.Floor, "K_hint", 5,5) }, { 'E', (TileType.Floor, "e_hint", 2,2) }
            };

            string mapPath = Path.Combine(_mapsPath, "level2.txt");
            string largeDecorPath = Path.Combine(_mapsPath, "decor_large2.txt");

            if (File.Exists(mapPath))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    gm.LoadMap(mapPath, largeDecorPath, backgroundMappings, largeDecorMappings);
                }), DispatcherPriority.Background);
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => CreateDefaultMap(gm)), DispatcherPriority.Background);
            }
        }

        private void CreateDefaultMap(GameManager gm)
        {
            for (int x = 0; x < MAP_WIDTH; x++) { gm.SetTile(x, 0, TileType.Wall, "Road"); gm.SetTile(x, MAP_HEIGHT - 1, TileType.Wall, "Road"); }
            for (int y = 0; y < MAP_HEIGHT; y++) { gm.SetTile(0, y, TileType.Wall, "Road"); gm.SetTile(MAP_WIDTH - 1, y, TileType.Wall, "Road"); }
            for (int x = 1; x < MAP_WIDTH - 1; x++)
                for (int y = 1; y < MAP_HEIGHT - 1; y++)
                    gm.SetTile(x, y, TileType.Floor, "Grass");
            Random rand = new Random();
            for (int i = 0; i < 30; i++) gm.SetTile(rand.Next(2, MAP_WIDTH - 2), rand.Next(2, MAP_HEIGHT - 2), TileType.Wall, "Tree");
        }

        private void SubscribeInventoryEvents() => _player.Inventory.ItemsChanged += OnInventoryChanged;
        private void OnInventoryChanged(IEnumerable<int> indexes) { if (InventoryPanel.Visibility == Visibility.Visible && !_isQuestLogOpen) RefreshInventory(); }

        private void UpdateStatusBar()
        {
            if (_player == null) return;
            var statusBar = InventoryPanel.FindName("StatusBar") as TextBlock;
            if (statusBar != null)
            {
                statusBar.Text = $"ОЗ: {_player.Health}/{_player.MaxHealth} СИЛА: {_player.Strength}";
            }
        }
        private void ToggleInventory()
        {
            if (InventoryPanel.Visibility == Visibility.Visible && !_isQuestLogOpen)
            {
                InventoryPanel.Visibility = Visibility.Collapsed;
                InventoryPanel.Focusable = false;
            }
            else if (InventoryPanel.Visibility == Visibility.Visible && _isQuestLogOpen)
            {
                InventoryPanel.Visibility = Visibility.Collapsed;
                InventoryPanel.Focusable = false;
                _isQuestLogOpen = false;
            }
            else
            {
                RefreshInventory();
                InventoryPanel.Visibility = Visibility.Visible;
                InventoryPanel.Focusable = true;
                InventoryPanel.Focus();
                _isQuestLogOpen = false;
                InventoryDescriptionText.Text = "Нет предметов";

                var statusBar = InventoryPanel.FindName("StatusBar") as TextBlock;
                if (statusBar != null)
                {
                    statusBar.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ToggleQuestLog()
        {
            if (InventoryPanel.Visibility == Visibility.Visible && !_isQuestLogOpen)
            {
                InventoryPanel.Visibility = Visibility.Collapsed;
                InventoryPanel.Focusable = false;
            }

            if (InventoryPanel.Visibility == Visibility.Visible && _isQuestLogOpen)
            {
                InventoryPanel.Visibility = Visibility.Collapsed;
                InventoryPanel.Focusable = false;
                _isQuestLogOpen = false;
            }
            else
            {
                RefreshQuestLog();
                UpdateStatusBar();
                InventoryPanel.Visibility = Visibility.Visible;
                InventoryPanel.Focusable = true;
                InventoryPanel.Focus();
                _isQuestLogOpen = true;

                var statusBar = InventoryPanel.FindName("StatusBar") as TextBlock;
                if (statusBar != null)
                {
                    statusBar.Visibility = Visibility.Visible;
                }
            }
        }

        private void RefreshInventory()
        {
            if (_player == null) return;
            _inventoryItems.Clear();
            var inventory = _player.Inventory;

            for (int i = 0; i < inventory.TotalSlots; i++)
            {
                var item = inventory.GetItem(i);
                if (item != null && item.Quantity > 0)
                    _inventoryItems.Add(item);
            }

            if (_selectedInventoryIndex >= _inventoryItems.Count)
                _selectedInventoryIndex = _inventoryItems.Count > 0 ? _inventoryItems.Count - 1 : 0;

            UpdateInventoryDescription();

            var statusBar = InventoryPanel.FindName("StatusBar") as TextBlock;
            if (statusBar != null && !_isQuestLogOpen)
            {
                statusBar.Visibility = Visibility.Collapsed;
            }

            bool wasQuestLog = _isQuestLogOpen;

            InventoryItemsControl.ItemsSource = null;
            InventoryItemsControl.ItemsSource = _inventoryItems;

            if (!wasQuestLog)
            {
                InventoryItemsControl.SelectedIndex = _selectedInventoryIndex;
                InventoryItemsControl.UpdateLayout();
            }
        }

        private void RefreshQuestLog()
        {
            if (_player == null) return;

            _activeQuests.Clear();

            foreach (var quest in _availableQuests.Values)
            {
                if (quest.Status == QuestStatus.Active)
                {
                    _activeQuests.Add(quest);
                }
            }

            if (_selectedQuestIndex >= _activeQuests.Count)
                _selectedQuestIndex = _activeQuests.Count > 0 ? _activeQuests.Count - 1 : -1;

            UpdateQuestDescription();
            UpdateStatusBar();

            var statusBar = InventoryPanel.FindName("StatusBar") as TextBlock;
            if (statusBar != null)
            {
                statusBar.Visibility = Visibility.Visible;
            }

            bool wasQuestLog = _isQuestLogOpen;

            InventoryItemsControl.ItemsSource = null;
            InventoryItemsControl.ItemsSource = _activeQuests;

            if (wasQuestLog && _selectedQuestIndex >= 0)
            {
                InventoryItemsControl.SelectedIndex = _selectedQuestIndex;
                InventoryItemsControl.UpdateLayout();
            }
        }

        private void UpdateInventoryDescription()
        {
            if (_selectedInventoryIndex >= 0 && _selectedInventoryIndex < _inventoryItems.Count)
                InventoryDescriptionText.Text = _inventoryItems[_selectedInventoryIndex].Description;
            else
                InventoryDescriptionText.Text = "Нет предметов";
        }

        private void UpdateQuestDescription()
        {
            if (_activeQuests.Count == 0)
            {
                InventoryDescriptionText.Text = "Нет активных заданий";
                return;
            }

            if (_selectedQuestIndex >= 0 && _selectedQuestIndex < _activeQuests.Count)
            {
                var quest = _activeQuests[_selectedQuestIndex];
                InventoryDescriptionText.Text = quest.Description;
            }
        }

        private void MoveInventorySelection(int delta)
        {
            if (_isQuestLogOpen)
            {
                if (_activeQuests.Count == 0) return;
                _selectedQuestIndex += delta;
                if (_selectedQuestIndex < 0) _selectedQuestIndex = _activeQuests.Count - 1;
                if (_selectedQuestIndex >= _activeQuests.Count) _selectedQuestIndex = 0;

                InventoryItemsControl.SelectedIndex = _selectedQuestIndex;
                InventoryItemsControl.ScrollIntoView(InventoryItemsControl.SelectedItem);
                UpdateQuestDescription();
            }
            else
            {
                if (_inventoryItems.Count == 0) return;
                _selectedInventoryIndex += delta;
                if (_selectedInventoryIndex < 0) _selectedInventoryIndex = _inventoryItems.Count - 1;
                if (_selectedInventoryIndex >= _inventoryItems.Count) _selectedInventoryIndex = 0;

                InventoryItemsControl.SelectedIndex = _selectedInventoryIndex;
                InventoryItemsControl.ScrollIntoView(InventoryItemsControl.SelectedItem);
                UpdateInventoryDescription();
            }
        }

        private void UseSelectedInventoryItem()
        {
            if (_isQuestLogOpen)
            {
                if (_selectedQuestIndex >= 0 && _selectedQuestIndex < _activeQuests.Count)
                {
                    var quest = _activeQuests[_selectedQuestIndex];
                }
                return;
            }

            if (_selectedInventoryIndex < 0 || _selectedInventoryIndex >= _inventoryItems.Count) return;
            var item = _inventoryItems[_selectedInventoryIndex];

            // Зелье здоровья
            if (item.Key == "health_potion")
            {
                if (_player.Health < _player.MaxHealth)
                {
                    _player.Heal(30f);
                    for (int i = 0; i < _player.Inventory.TotalSlots; i++)
                    {
                        var invItem = _player.Inventory.GetItem(i);
                        if (invItem == item) { _player.Inventory.ModifyItemQuantity(i, -1); break; }
                    }
                    RefreshInventory();
                    if (_inventoryItems.Count == 0) _selectedInventoryIndex = -1;
                    else if (_selectedInventoryIndex >= _inventoryItems.Count) _selectedInventoryIndex = _inventoryItems.Count - 1;
                    UpdateInventoryDescription();
                    UpdateStatusBar();
                }
            }

            if (item.Key == "honey")
            {
                _player.MaxHealth += 1;
                _player.Heal(1); // Восстанавливаем 1 HP
                for (int i = 0; i < _player.Inventory.TotalSlots; i++)
                {
                    var invItem = _player.Inventory.GetItem(i);
                    if (invItem == item) { _player.Inventory.ModifyItemQuantity(i, -1); break; }
                }
                RefreshInventory();
                if (_inventoryItems.Count == 0) _selectedInventoryIndex = -1;
                else if (_selectedInventoryIndex >= _inventoryItems.Count) _selectedInventoryIndex = _inventoryItems.Count - 1;
                UpdateInventoryDescription();
                UpdateStatusBar();

                ShowFloatingDamageNumber(_player.Position, 1, true);
                _gameManager.PlaySound("item.mp3", 0.6f);
            }

            // ДОБАВИТЬ ЭТОТ БЛОК - Сыр (+25 HP)
            if (item.Key == "cheese")
            {
                if (_player.Health < _player.MaxHealth)
                {
                    _player.Heal(25f);
                    for (int i = 0; i < _player.Inventory.TotalSlots; i++)
                    {
                        var invItem = _player.Inventory.GetItem(i);
                        if (invItem == item) { _player.Inventory.ModifyItemQuantity(i, -1); break; }
                    }
                    RefreshInventory();
                    if (_inventoryItems.Count == 0) _selectedInventoryIndex = -1;
                    else if (_selectedInventoryIndex >= _inventoryItems.Count) _selectedInventoryIndex = _inventoryItems.Count - 1;
                    UpdateInventoryDescription();
                    UpdateStatusBar();

                    ShowFloatingDamageNumber(_player.Position, 25, true);
                    _gameManager.PlaySound("item.mp3", 0.6f);
                }
            }
        }

        private void DropItem(string itemId, Vector2D position)
        {
            if (!_itemPrefabs.ContainsKey(itemId)) return;

            Item originalItem = _itemPrefabs[itemId];
            Item itemToDrop = new Item(originalItem.Key, originalItem.Name, originalItem.Description,
                originalItem.IconPath, originalItem.IsStackable, 1);

            ImageSource source = GetItemIcon(itemId);

            var image = new Image
            {
                Source = source,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                Tag = itemToDrop
            };

            Canvas.SetLeft(image, position.X - 12);
            Canvas.SetTop(image, position.Y - 12);
            GameArea.Children.Add(image);

            var bounceAnimation = new DoubleAnimation
            {
                From = position.Y - 30,
                To = position.Y - 12,
                Duration = TimeSpan.FromSeconds(0.35),
                EasingFunction = new BounceEase { Bounces = 2, Bounciness = 2 }
            };
            image.BeginAnimation(Canvas.TopProperty, bounceAnimation);
        }

        private ImageSource GetItemIcon(string itemId)
        {
            if (_iconCache.TryGetValue(itemId, out var cached)) return cached;

            string iconPath = Path.Combine(_itemsPath, $"{itemId}.png");
            ImageSource source;
            if (File.Exists(iconPath))
            {
                var bmp = new BitmapImage(new Uri(iconPath));
                bmp.Freeze();
                source = bmp;
            }
            else
            {
                var drawing = new DrawingGroup();
                Brush color = itemId.Contains("sword") ? Brushes.Silver :
                             itemId.Contains("potion") ? Brushes.Red :
                             itemId.Contains("honey") ? Brushes.Gold :
                             itemId.Contains("gorgon") ? Brushes.Purple : Brushes.LimeGreen;
                drawing.Children.Add(new GeometryDrawing { Brush = color, Geometry = new EllipseGeometry(new Rect(0, 0, 20, 20)) });
                source = new DrawingImage(drawing);
            }
            _iconCache[itemId] = source;
            return source;
        }

        private Enemy FindNearestEnemy(double radius)
        {
            if (_finn == null || !_finn.IsAlive) return null;
            Enemy nearest = null;
            double minDist = radius;
            foreach (var character in _gameManager.Characters)
                if (character is Enemy enemy && enemy.IsAlive)
                {
                    double dist = Vector2D.Distance(_finn.Position, enemy.Position);
                    if (dist < minDist) { minDist = dist; nearest = enemy; }
                }
            return nearest;
        }

        private Vector2D GetRandomSpawnPosition()
        {
            if (_gameManager == null) return Vector2D.Zero;
            var grid = _gameManager.Grid;
            for (int i = 0; i < 100; i++)
            {
                int x = _rng.Next(1, grid.Width - 1);
                int y = _rng.Next(1, grid.Height - 1);
                if (grid.IsWalkable(x, y)) return new Vector2D(x * 32 + 16, y * 32 + 16);
            }
            return new Vector2D(grid.Width * 16, grid.Height * 16);
        }

        private bool TryPlayerAttack()
        {
            if (_attackCooldown < 0) return false;
            if (_player == null || !_player.IsAlive) return false;

            float playerDamage = _player.Strength;
            bool hitSomething = false;

            for (int i = _gameManager.Characters.Count - 1; i >= 0; i--)
            {
                var character = _gameManager.Characters[i];

                if (character is Enemy enemy && enemy.IsAlive)
                {
                    double attackRange = enemy.HitboxRadius;

                    if (Vector2D.Distance(_player.Position, enemy.Position) <= attackRange)
                    {
                        enemy.TakeDamage(playerDamage);
                        hitSomething = true;
                        ShowFloatingDamageNumber(enemy.Position, playerDamage, false);
                        TriggerShake(2.0);
                        _gameManager.PlaySound("attack.mp3", 0.6f);

                        if (enemy.Type == "Gorgon" && _gorgon != null && _gorgon.CurrentState == CharacterState.Idle)
                        {
                            _gorgon.SetState(CharacterState.Chase);
                            _gameManager.PlaySound("attack.mp3", 0.7f);
                        }
                    }
                }

                if (character == _finn && _finn.IsAlive)
                {
                    double attackRange = 50;

                    if (Vector2D.Distance(_player.Position, _finn.Position) <= attackRange)
                    {
                        _finn.TakeDamage(playerDamage);  // Уменьшаем здоровье
                        hitSomething = true;
                        ShowFloatingDamageNumber(_finn.Position, playerDamage, false);
                        TriggerShake(2.0);
                        _gameManager.PlaySound("attack.mp3", 0.6f);

                        // Проверяем, не умер ли Finn
                        if (!_finn.IsAlive)
                        {
                            _finn.SetState(CharacterState.Dead);
                        }
                    }
                }
            }

            if (hitSomething) _attackCooldown = ATTACK_COOLDOWN_TIME;
            else _attackCooldown = 0.2;

            return hitSomething;
        }

        private void HandleQuestDialogue(Player player, NPC npc)
        {
            Quest quest = null;
            bool isGirlNPC = (npc == _girl);

            if (npc == _questAlly)
            {
                _availableQuests.TryGetValue("slime_quest", out quest);
            }
            else if (isGirlNPC)
            {
                if (_availableQuests.TryGetValue("girl_quest_first", out var firstQuest) && firstQuest.Status != QuestStatus.Completed)
                {
                    quest = firstQuest;
                }
                else if (_availableQuests.TryGetValue("girl_quest_second", out var secondQuest) && secondQuest.Status != QuestStatus.Completed)
                {
                    quest = secondQuest;
                }
            }
            else if (npc == _schoolGirl)
            {
                _availableQuests.TryGetValue("schoolgirl_quest", out quest);
            }
            else if (npc == _woman)
            {
                _availableQuests.TryGetValue("woman_quest", out quest);
            }
            else if (npc == null && _gorgon != null && Vector2D.Distance(_player.Position, _gorgon.Position) <= 60)
            {
                _availableQuests.TryGetValue("gorgon_necklace_quest", out quest);
            }

            if (quest == null)
            {
                if (isGirlNPC && _availableQuests.ContainsKey("girl_quest_second") &&
                    _availableQuests["girl_quest_second"].Status == QuestStatus.Completed)
                {
                    ShowDialogue("Спасибо за всю помощь! Теперь я в безопасности.");
                }
                return;
            }

            if (quest.Id == "girl_quest_second" && quest.Status == QuestStatus.Active)
            {
                bool hasYellowSphere = player.Inventory.GetTotalQuantity("yellow_thing") >= 1;
                bool hasSilverSphere = player.Inventory.GetTotalQuantity("silver_thing") >= 1;

                if (hasYellowSphere || hasSilverSphere)
                {
                    string usedSphere = hasYellowSphere ? "yellow_thing" : "silver_thing";
                    quest.RequiredItems.Clear();
                    quest.AddRequiredItem(usedSphere, 1);

                    foreach (var reward in quest.RewardsItems)
                    {
                        if (_itemPrefabs.ContainsKey(reward.Key))
                        {
                            player.Inventory.AddItem(_itemPrefabs[reward.Key], reward.Value);
                        }
                    }

                    if (quest.RewardStrength > 0)
                        player.Strength += quest.RewardStrength;

                    quest.Complete();

                    ShowDialogue(quest.CompletionDialogue);
                    _gameManager?.PlaySound("item.mp3", 0.6f);

                    if (_isQuestLogOpen) RefreshQuestLog();
                    if (InventoryPanel.Visibility == Visibility.Visible) UpdateStatusBar();
                    return;
                }
                else
                {
                    ShowDialogue("Принеси магическую сферу, если найдешь. Говорят, они где-то в этом лесу...");
                    return;
                }
            }

            if (quest.Status == QuestStatus.NotStarted)
            {
                quest.Start();
                ShowDialogue(quest.StartDialogue);
                if (_isQuestLogOpen) RefreshQuestLog();
                return;
            }

            if (quest.Status == QuestStatus.Active)
            {
                bool isComplete = false;

                if (quest.RequiresEnemyKill)
                {
                    isComplete = quest.EnemyDefeated;
                }
                else
                {
                    isComplete = quest.IsComplete(player.Inventory);
                }

                if (isComplete)
                {
                    if (!quest.RequiresEnemyKill)
                    {
                        foreach (var required in quest.RequiredItems)
                        {
                            player.Inventory.RemoveItem(required.Key, required.Value);
                        }
                    }

                    foreach (var reward in quest.RewardsItems)
                    {
                        if (_itemPrefabs.ContainsKey(reward.Key))
                        {
                            player.Inventory.AddItem(_itemPrefabs[reward.Key], reward.Value);
                        }
                    }

                    if (quest.RewardStrength > 0)
                        player.Strength += quest.RewardStrength;

                    quest.Complete();

                    ShowDialogue(quest.CompletionDialogue);
                    _gameManager?.PlaySound("item.mp3", 0.6f);

                    if (_isQuestLogOpen) RefreshQuestLog();
                    if (InventoryPanel.Visibility == Visibility.Visible) UpdateStatusBar();

                }
                else
                {
                    if (quest.RequiresEnemyKill)
                    {
                        ShowDialogue($"Ты еще не победил {quest.RequiredEnemyType}. Будь осторожен!");
                    }
                    else
                    {
                        string missingItems = "";
                        foreach (var req in quest.RequiredItems)
                        {
                            int has = player.Inventory.GetTotalQuantity(req.Key);
                            if (has < req.Value)
                            {
                                var item = _itemPrefabs.ContainsKey(req.Key) ? _itemPrefabs[req.Key] : null;
                                string itemName = item != null ? item.Name : req.Key;
                                missingItems += $"\n  • {itemName}: {has}/{req.Value}";
                            }
                        }
                        if (!string.IsNullOrEmpty(missingItems))
                            ShowDialogue($"Ты еще не собрал все предметы:{missingItems}");
                        else
                            ShowDialogue("Ты еще не собрал все предметы. Приходи, когда соберешь.");
                    }
                }
                return;
            }

            if (quest.Status == QuestStatus.Completed)
            {
                ShowDialogue(quest.AlreadyCompletedDialogue);
            }
        }

        private void HandleFinnDialogue()
        {
            if (_gorgonKillQuest == null) return;

            if (_gorgonKillQuest.Status == QuestStatus.NotStarted)
            {
                if (_gorgonDefeated)
                {
                    ShowDialogue("Ого! Ты уже победил Горгону? Невероятно! Вот твоя награда!");

                    foreach (var reward in _gorgonKillQuest.RewardsItems)
                        if (_itemPrefabs.ContainsKey(reward.Key))
                            _player.Inventory.AddItem(_itemPrefabs[reward.Key], reward.Value);

                    if (_gorgonKillQuest.RewardStrength > 0)
                        _player.Strength += _gorgonKillQuest.RewardStrength;

                    _gameManager.PlaySound("item.mp3", 0.8f);

                    _gorgonKillQuest.Start();
                    _gorgonKillQuest.EnemyDefeated = true;
                    _gorgonKillQuest.Complete();
                    _gameManager.SendEvent(Event_.GoodEndingCompleted);

                    if (_isQuestLogOpen) RefreshQuestLog();
                    if (InventoryPanel.Visibility == Visibility.Visible) UpdateStatusBar();
                }
                else
                {
                    _gorgonKillQuest.Start();
                    ShowDialogue(_gorgonKillQuest.StartDialogue);
                    if (_isQuestLogOpen) RefreshQuestLog();
                }
                return;
            }

            if (_gorgonKillQuest.Status == QuestStatus.Active)
            {
                if (_gorgonDefeated)
                {
                    _gorgonKillQuest.EnemyDefeated = true;
                    _gorgonKillQuest.Complete();

                    foreach (var reward in _gorgonKillQuest.RewardsItems)
                        if (_itemPrefabs.ContainsKey(reward.Key))
                            _player.Inventory.AddItem(_itemPrefabs[reward.Key], reward.Value);

                    if (_gorgonKillQuest.RewardStrength > 0)
                        _player.Strength += _gorgonKillQuest.RewardStrength;

                    ShowDialogue(_gorgonKillQuest.CompletionDialogue);
                    _gameManager.PlaySound("item.mp3", 0.8f);

                    _gameManager.SendEvent(Event_.GoodEndingCompleted);

                    if (_isQuestLogOpen) RefreshQuestLog();
                    if (InventoryPanel.Visibility == Visibility.Visible) UpdateStatusBar();
                }
                else
                {
                    ShowDialogue("Горгона всё ещё там... Будь осторожен!");
                }
                return;
            }

            if (_gorgonKillQuest.Status == QuestStatus.Completed)
            {
                ShowDialogue(_gorgonKillQuest.AlreadyCompletedDialogue);
            }
        }

        private void HandleGorgonInteraction()
        {
            _gorgonInteractionCount++;

            if (_gorgon != null && _gorgon.CurrentState == CharacterState.Idle)
            {
                ShowDialogue("Ты разозлил меня! Теперь ты пожалеешь!");
                _gorgon.SetState(CharacterState.Chase);
                _gameManager.PlaySound("attack.mp3", 0.7f);
            }
            else if (_gorgon != null)
            {
                string[] annoyedDialogues = { "Пшел вон отсюда!", "Я же сказала - отстань!", "Сколько можно приставать?!" };
                int dialogueIndex = Math.Min(_gorgonInteractionCount - 1, annoyedDialogues.Length - 1);
                ShowDialogue(annoyedDialogues[dialogueIndex]);
            }
        }

        private bool TryInteractWithNPC()
        {
            Character nearestNPC = null;

            foreach (var character in _gameManager.Characters)
            {
                if ((character is NPC || character is Enemy) && character.IsAlive)
                {
                    double distance = Vector2D.Distance(_player.Position, character.Position);
                    if (distance <= 60)
                    {
                        nearestNPC = character;
                        break;
                    }
                }
            }

            if (nearestNPC == null) return false;

            _gameManager.PlaySound("action.mp3", 0.7f);

            if (nearestNPC is Enemy enemy && enemy.Type == "Gorgon")
            {
                if (_availableQuests.TryGetValue("gorgon_necklace_quest", out var quest) && quest.Status != QuestStatus.Completed)
                {
                    HandleQuestDialogue(_player, null);
                }
                else
                {
                    HandleGorgonInteraction();
                }
            }
            else if (nearestNPC is NSM_NPC && nearestNPC == _finn)
                HandleFinnDialogue();
            else if (nearestNPC is NPC npc)
                HandleQuestDialogue(_player, npc);

            return true;
        }

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
                if (_player.Inventory.AddItem(itemToPickup))
                {
                    _gameManager.PlaySound("pickup.mp3", 0.5f);
                    GameArea.Children.Remove(closestItemImage);
                    return true;
                }
            }
            return false;
        }

        private void TryInteractWithFinn()
        {
            if (_finn == null || !_finn.IsAlive || _player == null || !_player.IsAlive) return;
            if (Vector2D.Distance(_player.Position, _finn.Position) <= 60)
                HandleFinnDialogue();
        }

        public void Restart()
        {
            WriteLog("");
            WriteLog("ПЕРЕЗАПУСК ИГРЫ");

            _gameTimer?.Stop();
            _spawnTimer?.Stop();

            GameOverBox.Visibility = Visibility.Collapsed;
            InventoryPanel.Visibility = Visibility.Collapsed;
            DialogueBox.Visibility = Visibility.Collapsed;

            // Скрываем панель громкости при рестарте
            VolumePanel.Visibility = Visibility.Collapsed;

            _isQuestLogOpen = false;
            _dialogueQueue.Clear();

            var itemsToRemove = GameArea.Children.OfType<Image>().Where(img => img.Tag is Item).ToList();
            foreach (var item in itemsToRemove) GameArea.Children.Remove(item);

            var messagesToRemove = OverlayCanvas.Children.OfType<TextBlock>().ToList();
            foreach (var msg in messagesToRemove) OverlayCanvas.Children.Remove(msg);

            _gorgonDefeated = false;
            _tutorialCompleted = false;
            _currentBeeCount = 0;
            _gorgonInteractionCount = 0;
            _attackCooldown = 0.4;
            _currentZoom = 1.5;
            if (_cameraScale != null)
            {
                _cameraScale.ScaleX = _currentZoom;
                _cameraScale.ScaleY = _currentZoom;
            }

            var allEnemies = _gameManager.Characters.Where(c => c is Enemy).ToList();
            foreach (var enemy in allEnemies)
            {
                _gameManager.RemoveCharacter(enemy);
            }
            _gorgon = null;
            _currentBeeCount = 0;

            DropItem("belt", new Vector2D(76 * 32, 92 * 32));

            _player.SetPosition(48 * 32, 48 * 32);
            _player.ResetHealth();
            _player.Strength = 15f;
            _player.Stop();
            _player.Inventory.Clear();

            _questAlly.Position = new Vector2D(46 * 32, 48 * 32);
            _questAlly.ResetHealth();
            _questAlly.SetState(CharacterState.Idle);
            _questAlly.Stop();

            _girl.Position = new Vector2D(18 * 32, 4 * 32);
            _girl.ResetHealth();
            _girl.SetState(CharacterState.Idle);
            _girl.Stop();

            _schoolGirl.Position = new Vector2D(30 * 32, 35 * 32);
            _schoolGirl.ResetHealth();
            _schoolGirl.SetState(CharacterState.Idle);
            _schoolGirl.Stop();

            _woman.Position = new Vector2D(70 * 32, 65 * 32);
            _woman.ResetHealth();
            _woman.SetState(CharacterState.Idle);
            _woman.Stop();

            _finn.Position = new Vector2D(25 * 32, 25 * 32);
            _finn.ResetHealth();
            _finn.SetState(CharacterState.Decision);
            _finn.Stop();

            foreach (var quest in _availableQuests.Values)
            {
                quest.Reset();
            }

            InitializeGorgon();

            RefreshInventory();
            RefreshQuestLog();
            UpdateStatusBar();

            _gameManager.PlayMusic("soundtrack.mp3");
            _gameManager.SetMusicVolume(0.1);
            StartSpawnTimer();
            WriteLog("начальное состояние Tutorial (после рестарта)");
            _gameManager.SetState(State_.Tutorial);
            StartGameLoop();

            Focus();
        }

        public void ShowDialogue(params string[] texts)
        {
            if (texts == null || texts.Length == 0) return;
            _player.Stop();
            foreach (var text in texts) _dialogueQueue.Enqueue(text);
            if (DialogueBox.Visibility != Visibility.Visible && _dialogueQueue.Count > 0)
                ShowNextDialogue();
        }

        private void ShowNextDialogue()
        {
            if (_dialogueQueue.Count > 0)
            {
                DialogueText.Text = _dialogueQueue.Dequeue();
                DialogueBox.Visibility = Visibility.Visible;
            }
        }

        public void ShowFloatingDamageNumber(Vector2D worldPosition, float amount, bool isHeal)
        {
            if (_cameraTransform == null || _cameraScale == null) return;
            var text = new TextBlock
            {
                Text = isHeal ? $"+{amount}" : $"-{amount}",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = isHeal ? Brushes.LimeGreen : Brushes.Red,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 1 }
            };
            double screenX = (worldPosition.X * _cameraScale.ScaleX) + _cameraTransform.X;
            double screenY = (worldPosition.Y * _cameraScale.ScaleY) + _cameraTransform.Y;
            Canvas.SetLeft(text, screenX - 20);
            Canvas.SetTop(text, screenY - 40);
            Canvas.SetZIndex(text, 200);
            OverlayCanvas.Children.Add(text);
            var translateAnimation = new DoubleAnimation { From = screenY - 40, To = screenY - 100, Duration = TimeSpan.FromSeconds(0.8), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            var opacityAnimation = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromSeconds(0.8), BeginTime = TimeSpan.FromSeconds(0.2) };
            opacityAnimation.Completed += (s, e) => OverlayCanvas.Children.Remove(text);
            text.BeginAnimation(Canvas.TopProperty, translateAnimation);
            text.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
        }

        public void TriggerShake(double intensity = 5.0) => _shakeIntensity = intensity;

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

        private void CenterCameraOnPlayer()
        {
            if (_player != null && _cameraTransform != null && _cameraScale != null)
            {
                _cameraTransform.X = ActualWidth / 2 - _player.Position.X * _currentZoom;
                _cameraTransform.Y = ActualHeight / 2 - _player.Position.Y * _currentZoom;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_player != null && _player.IsAlive) CenterCameraOnPlayer();
            double scale = ActualWidth / 800;
            DialogueText.FontSize = Math.Max(14, Math.Min(24, 18 * scale));
            double maxDialogueWidth = ActualWidth * 0.8;
            DialogueBox.MaxWidth = maxDialogueWidth;
            DialogueBox.Width = Math.Min(600, maxDialogueWidth);
            Canvas.SetLeft(DialogueBox, (ActualWidth - DialogueBox.Width) / 2);
            Canvas.SetTop(DialogueBox, ActualHeight - DialogueBox.Height - (ActualHeight * 0.05));
            if (InventoryPanel.Visibility == Visibility.Visible)
            {
                Canvas.SetLeft(InventoryPanel, (ActualWidth - 700) / 2);
                Canvas.SetTop(InventoryPanel, (ActualHeight - 500) / 2);
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double newZoom = _currentZoom + (e.Delta > 0 ? ZOOM_STEP : -ZOOM_STEP);
            if (newZoom >= MIN_ZOOM && newZoom <= MAX_ZOOM)
            {
                _currentZoom = newZoom;
                _cameraScale.ScaleX = _currentZoom;
                _cameraScale.ScaleY = _currentZoom;
                CenterCameraOnPlayer();
            }
            e.Handled = true;
        }

        private void StartSpawnTimer()
        {
            _spawnTimer?.Stop();
            _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _spawnTimer.Tick += (s, e) => SpawnRandomEnemy();
            _spawnTimer.Start();
            SpawnRandomEnemy();
        }

        private void OnGameTick(object sender, EventArgs e)
        {
            HandleInput();
            _gameManager.Update();
            UpdateCameraShake();
            if (_player != null && _player.IsAlive && _cameraTransform != null && _shakeIntensity == 0)
                CenterCameraOnPlayer();
            RotateGorgonToPlayer();

            if (InventoryPanel.Visibility == Visibility.Visible && _isQuestLogOpen)
            {
                UpdateStatusBar();
            }
        }

        private void HandleInput()
        {
            if (IsUIOpen) return;

            if (_gameManager.FSM.CurrentState.Id == _gameManager._tutorialState.Id && !_tutorialCompleted)
            {
                if (Keyboard.IsKeyDown(Key.W) || Keyboard.IsKeyDown(Key.S) ||
                    Keyboard.IsKeyDown(Key.A) || Keyboard.IsKeyDown(Key.D) ||
                    Keyboard.IsKeyDown(Key.E) || Keyboard.IsKeyDown(Key.I))
                {
                    string keyName = "";
                    if (Keyboard.IsKeyDown(Key.W)) keyName = "W";
                    else if (Keyboard.IsKeyDown(Key.S)) keyName = "S";
                    else if (Keyboard.IsKeyDown(Key.A)) keyName = "A";
                    else if (Keyboard.IsKeyDown(Key.D)) keyName = "D";
                    else if (Keyboard.IsKeyDown(Key.E)) keyName = "E";
                    else if (Keyboard.IsKeyDown(Key.I)) keyName = "I";

                    WriteLog($"нажата клавиша {keyName}");
                    WriteLog($"отправлено событие TutorialCompleted");
                    WriteLog($"переход Tutorial - Game");

                    _tutorialCompleted = true;
                    _gameManager.SendEvent(Event_.TutorialCompleted);
                }
                return;
            }

            Vector2D dir = Vector2D.Zero;
            if (Keyboard.IsKeyDown(Key.W)) dir += new Vector2D(0, -1);
            if (Keyboard.IsKeyDown(Key.S)) dir += new Vector2D(0, 1);
            if (Keyboard.IsKeyDown(Key.A)) dir += new Vector2D(-1, 0);
            if (Keyboard.IsKeyDown(Key.D)) dir += new Vector2D(1, 0);
            if (dir.Length() > 0) _player.Move(dir);
            else _player.Stop();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Перезапуск по R
            if (e.Key == Key.R)
            {
                Restart();
                e.Handled = true;
                return;
            }

            // Открытие панели громкости по K
            if (e.Key == Key.K)
            {
                ToggleVolumePanel();
                e.Handled = true;
                return;
            }

            // Если панель громкости открыта, игнорируем остальные клавиши
            if (VolumePanel.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape || e.Key == Key.K)
                {
                    ToggleVolumePanel();
                    e.Handled = true;
                }
                return;
            }

            base.OnKeyDown(e);

            if (InventoryPanel.Visibility == Visibility.Visible)
            {
                switch (e.Key)
                {
                    case Key.I:
                        if (!_isQuestLogOpen) ToggleInventory();
                        else ToggleQuestLog();
                        e.Handled = true;
                        break;
                    case Key.J:
                        if (_isQuestLogOpen) ToggleQuestLog();
                        else ToggleQuestLog();
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        if (_isQuestLogOpen) ToggleQuestLog();
                        else ToggleInventory();
                        e.Handled = true;
                        break;
                    case Key.W: MoveInventorySelection(-1); e.Handled = true; break;
                    case Key.S: MoveInventorySelection(1); e.Handled = true; break;
                    case Key.Enter: UseSelectedInventoryItem(); e.Handled = true; break;
                }
                return;
            }

            if (DialogueBox.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.F || e.Key == Key.Escape || e.Key == Key.Enter || e.Key == Key.Space)
                {
                    DialogueBox.Visibility = Visibility.Collapsed;
                    if (_dialogueQueue.Count > 0) ShowNextDialogue();
                    e.Handled = true;
                    return;
                }
            }

            var currentState = _gameManager.FSM.CurrentState;

            if (currentState.Id == _gameManager._tutorialState.Id)
            {
                if (e.Key == Key.Escape)
                {
                    OnExitToMenu?.Invoke();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.W || e.Key == Key.S || e.Key == Key.A || e.Key == Key.D || e.Key == Key.E || e.Key == Key.I || e.Key == Key.Space)
                {
                    HandleInput();
                    e.Handled = true;
                }
                return;
            }

            if (currentState.Id == _gameManager._gameState.Id)
            {
                if (e.Key == Key.I) { ToggleInventory(); e.Handled = true; }
                else if (e.Key == Key.J) { ToggleQuestLog(); e.Handled = true; }
                else if (e.Key == Key.E) { TryPlayerAttack(); e.Handled = true; }
                else if (e.Key == Key.F)
                {
                    if (!TryPickupItem() && !TryInteractWithNPC()) TryInteractWithFinn();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape) OnExitToMenu?.Invoke();
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); e.Handled = true; }

        private void MusicVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_gameManager != null)
            {
                _gameManager.SetMusicVolume(e.NewValue);
                _gameManager.SetSFXVolume(e.NewValue);
            }
        }

        private void ToggleVolumePanel()
        {
            if (VolumePanel.Visibility == Visibility.Visible)
            {
                VolumePanel.Visibility = Visibility.Collapsed;
                Focus(); // Возвращаем фокус на игровое поле
            }
            else
            {
                // Обновляем значение слайдера текущей громкостью
                if (_gameManager != null)
                {
                    MusicVolumeSlider.Value = _gameManager.GetMusicVolume();
                }
                VolumePanel.Visibility = Visibility.Visible;
                VolumePanel.Focusable = true;
                VolumePanel.Focus();
            }
        }
    }

}