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
using System.Xml;
using Path = System.IO.Path;

namespace GameProj
{
    public enum QuestStatus
    {
        NotStarted,
        Active,
        Completed
    }

    // ==================== КЛАСС КВЕСТА ====================
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

        // Для квестов на убийство
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
            if (RequiresEnemyKill)
                return false;

            foreach (var required in RequiredItems)
            {
                if (inventory.GetTotalQuantity(required.Key) < required.Value)
                    return false;
            }
            return true;
        }

        public string GetDialogue(Inventory inventory)
        {
            switch (Status)
            {
                case QuestStatus.NotStarted:
                    return StartDialogue;
                case QuestStatus.Active:
                    if (RequiresEnemyKill && EnemyDefeated)
                        return "Ты победил Горгону? Невероятно! Вот твоя награда!";
                    if (RequiresEnemyKill)
                        return "Горгона всё ещё там... Будь осторожен!";
                    if (IsComplete(inventory))
                        return CompletionDialogue;
                    else
                        return "Ты еще не собрал все предметы. Приходи, когда соберешь.";
                case QuestStatus.Completed:
                    return AlreadyCompletedDialogue;
                default:
                    return Description;
            }
        }

        public void Reset()
        {
            Status = QuestStatus.NotStarted;
            EnemyDefeated = false;
        }
    }

    public enum State_
    {
        Tutorial,
        Game,
        End,
        DeadEnding,
        GoodEnding,
        BadEnding,
        NeutralEnding
    }

    public enum Event_
    {
        TutorialCompleted,
        PlayerDead,
        GoodEndingCompleted,
        BadEndingCompleted,
        NeutralEndingCompleted,
        GorgonDefeated
    }

    // Класс-обертка для предметов инвентаря
    public class ItemWrapper
    {
        public Item Item { get; set; }
        public string DisplayText => Item?.ToString() ?? "";
        public bool IsSelected { get; set; }
    }

    public partial class GameCanvas : UserControl
    {
        public static event Action OnExitToMenu;

        private const int SLIME_GOAL = 5;
        private const double ATTACK_COOLDOWN_TIME = 0.4;
        private const int MAX_ENEMIES = 10;
        private const double MIN_ZOOM = 1;
        private const double MAX_ZOOM = 2.0;
        private const double ZOOM_STEP = 0.1;
        private const int MAP_WIDTH = 100;
        private const int MAP_HEIGHT = 100;
        private const double PLAYER_SPEED = 4.0;
        private const int GORGON_ANNOYANCE_THRESHOLD = 3;
        private Queue<string> _dialogueQueue = new Queue<string>();

        private GameManager _gameManager;
        private Player _player;
        private NPC _questAlly, _girl, _schoolGirl, _woman;
        private Enemy _gorgon;
        private NSM_NPC _finn;

        private Dictionary<string, Quest> _availableQuests;
        private Quest _gorgonKillQuest;

        // Инвентарь
        private List<ItemWrapper> _inventoryItems = new List<ItemWrapper>();
        private int _selectedInventoryIndex = 0;

        private double _attackCooldown = 0.4;
        private Random _rng = new Random();

        private int _gorgonInteractionCount = 0;
        private bool _gorgonHasGivenItems = false;
        private double _gorgonLastDirection = 1;

        private bool _gorgonDefeated = false;

        private TranslateTransform _cameraTransform;
        private ScaleTransform _cameraScale;
        private TransformGroup _cameraTransformGroup;
        private double _currentZoom = 1.0;
        private double _shakeIntensity = 0;


        private string _tilesPath, _spritesPath, _itemsPath, _mapsPath, _soundsPath, _musicPath, _uiPath;
        private Dictionary<string, Item> _itemPrefabs;

        private DispatcherTimer _gameTimer;
        private DispatcherTimer _spawnTimer;

        private bool _tutorialCompleted = false;

        public bool IsUIOpen => (InventoryPanel.Visibility == Visibility.Visible || DialogueBox.Visibility == Visibility.Visible);

        public GameCanvas()
        {
            InitializeComponent();
            Focusable = true;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InitializePaths();
            InitializeItemPrefabs();
            InitializeCamera();
            this.SizeChanged += OnSizeChanged;
            this.PreviewMouseWheel += OnMouseWheel;

            InitializeGame();

            Focus();
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
                { "slime_goo", new Item("slime_goo", "Слизь", "Холодная и липкая.", $"Items/slime_goo.png", true, 1) },
                { "sword", new Item("sword", "Меч Силы", "Больше похож на кухонный нож, но все равно бьет больно.", $"Items/sword.png", false, 1) },
                { "health_potion", new Item("health_potion", "Зелье здоровья", "Восстанавливает 50 HP.", $"Items/health_potion.png", true, 1) },
                { "axe", new Item("axe", "Топор", "Я не умею пользоваться им по назначению.", $"Items/axe.png", false, 1) },
                { "belt", new Item("belt", "Пояс", "Издалека похож на знак \"Стоп\".", $"Items/belt.png", false, 1) },
                { "black_bottle", new Item("black_bottle", "Черная жижа", "Фу, гадость.", $"Items/black_bottle.png", true, 1) },
                { "necklace", new Item("necklace", "Ожерелье", "Симпатичное.", $"Items/necklace.png", false, 1) },
                { "note", new Item("note", "Записка", "Я читать не умею вообще-то.", $"Items/necklace.png", false, 1) },
                { "cheese", new Item("cheese", "Сыр", "Немного дор-блю. Не то чтобы по вкусу вкусно, но по сути вкусно.", $"Items/cheese.png", false, 1) },
                { "rock", new Item("rock", "Камень", "Камень.", $"Items/rock.png", false, 1) },
                { "silver_thing", new Item("silver_thing", "Серая штука", "Блестит.", $"Items/silver_thing.png", false, 1) },
                { "yellow_thing", new Item("yellow_thing", "Желтая штука", "Ого как блестит.", $"Items/yellow_thing.png", false, 1) },
                { "spoon", new Item("spoon", "Ложка", "Деревянная.", $"Items/spoon.png", true, 1) },
                { "sword_part", new Item("sword_part", "Ручка от меча", "Ну и куда её?", $"Items/sword_part.png", false, 1) },
                { "tail", new Item("tail", "Чей-то хвост", "Бедная корова?", $"Items/tail.png", false, 1) },
                { "web", new Item("web", "Паутина", "Здесь же пауков нет...", $"Items/web.png", true, 1) },
                { "honey", new Item("honey", "Мёд", "Сладкий и липкий.", $"Items/honey.png", true, 1) },
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
            _gorgonHasGivenItems = false;
            _gorgonDefeated = false;
            _inventoryItems.Clear();
            _selectedInventoryIndex = 0;

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
            DebugCheckAnimationFiles();

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
            var neutralEndingState = new State<State_, Event_>(State_.NeutralEnding);

            tutorialState.SetEventHandler((machine, ev) =>
            {
                if (ev == Event_.TutorialCompleted)
                    machine.SetState(gameState);
            });

            gameState.SetEventHandler((machine, ev) =>
            {
                switch (ev)
                {
                    case Event_.PlayerDead:
                        machine.SetState(deadEndingState);
                        break;
                    case Event_.GorgonDefeated:
                        machine.SetState(goodEndingState);
                        break;
                    case Event_.GoodEndingCompleted:
                        machine.SetState(goodEndingState);
                        break;
                    case Event_.BadEndingCompleted:
                        machine.SetState(badEndingState);
                        break;
                    case Event_.NeutralEndingCompleted:
                        machine.SetState(neutralEndingState);
                        break;
                }
            });

            deadEndingState.SetEnter(() =>
            {
                Dispatcher.Invoke(() => GameOver(false, "ПЛОХАЯ КОНЦОВКА\n\nВы не смогли выполнить свою миссию...\n\nНажмите R для рестарта"));
            });

            goodEndingState.SetEnter(() =>
            {
                Dispatcher.Invoke(() => GameOver(true, "ХОРОШАЯ КОНЦОВКА\n\nВы спасли мир и всех жителей!\n\nНажмите R для рестарта"));
            });

            badEndingState.SetEnter(() =>
            {
                Dispatcher.Invoke(() => GameOver(false, "ПЛОХАЯ КОНЦОВКА\n\nВаши действия привели к катастрофе...\n\nНажмите R для рестарта"));
            });

            neutralEndingState.SetEnter(() =>
            {
                Dispatcher.Invoke(() => GameOver(true, "НЕЙТРАЛЬНАЯ КОНЦОВКА\n\nВы выполнили свой долг, но не более...\n\nНажмите R для рестарта"));
            });

            _gameManager._tutorialState = new CompositeState<State_, Event_>(State_.Tutorial, State_.Tutorial);
            _gameManager._gameState = new CompositeState<State_, Event_>(State_.Game, State_.Game);

            var endComposite = new CompositeState<State_, Event_>(State_.End, State_.DeadEnding);
            endComposite.AddSubState(deadEndingState);
            endComposite.AddSubState(goodEndingState);
            endComposite.AddSubState(badEndingState);
            endComposite.AddSubState(neutralEndingState);

            _gameManager._endState = endComposite;

            _gameManager._tutorialState.AddSubState(tutorialState);
            _gameManager._gameState.AddSubState(gameState);

            _gameManager.InitializeFSM(_gameManager._tutorialState);
        }

        public void GameOver(bool isWin, string message)
        {
            _gameTimer?.Stop();
            _spawnTimer?.Stop();

            GameOverText.Text = isWin ? "ПОБЕДА!\n\nНажмите R для рестарта" : "ВЫ УМЕРЛИ!\n\nНажмите R для рестарта";
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

        private void DebugCheckAnimationFiles()
        {
            System.Diagnostics.Debug.WriteLine("=== Checking animation files ===");

            string[] characters = { "MC", "QuestGiver", "Girl", "SchoolGirl", "Woman", "Gorgon", "Slime", "Orc", "Bee" };
            string[] animations = { "_Idle.png", "_R_Walk.png", "_D_Walk.png", "_U_Walk.png", "_Attack.png" };

            foreach (var character in characters)
            {
                bool hasAny = false;
                foreach (var anim in animations)
                {
                    string filePath = Path.Combine(_spritesPath, character + anim);
                    if (File.Exists(filePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"  ✓ Found: {character}{anim}");
                        hasAny = true;
                    }
                }
                if (!hasAny)
                {
                    System.Diagnostics.Debug.WriteLine($"  ✗ MISSING: No animation files for {character}");
                }
            }

            System.Diagnostics.Debug.WriteLine("=== End of check ===");
        }

        private void InitializePlayer()
        {
            string mcSpritePath = Path.Combine(_spritesPath, "MC.png");
            _player = new Player(new Vector2D(48 * 32, 48 * 32), "Player", health: 100, speed: PLAYER_SPEED,
                spritePath: mcSpritePath, visualScale: 1.0,
                spriteInfo: new SpriteInfo("MC", 48, 48));
            _player.Strength = 15f;
            _player.Inventory.AddItem(_itemPrefabs["slime_goo"], 7);
            _gameManager.AddCharacter(_player);
        }

        private void InitializeQuestAlly()
        {
            string allySpritePath = Path.Combine(_spritesPath, "MC.png");
            _questAlly = new NPC(_gameManager.Grid, new Vector2D(46 * 32, 48 * 32), "MC",
                speed: 0, health: 100f, strength: 0f,
                spritePath: allySpritePath, visualScale: 1.0,
                spriteInfo: new SpriteInfo("MC", 48, 48));
            _questAlly.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_questAlly);

            var slimeQuest = new Quest("slime_quest", "Сбор слизи", "Принеси 5 бутылочек со слизью")
                .SetDialogues(
                    "Привет! Мне нужна помощь! Надо победить пять слизней, чтобы слизь с них получить, а я не могу!",
                    "Ухты, разобрался, как сражаться? А у меня не получилось :( Держи мой меч, тебе нужнее будет",
                    "На северо-запад пойдешь - деревню найдешь!"
                )
                .AddRequiredItem("slime_goo", SLIME_GOAL)
                .AddRewardItem("sword", 1)
                .SetRewardStrength(15);

            slimeQuest.OnQuestStarted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест начат: {q.Name}"); };
            slimeQuest.OnQuestCompleted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест завершен: {q.Name}"); };

            _availableQuests.Add("slime_quest", slimeQuest);
        }

        private void InitializeGirl()
        {
            string girlSpritePath = Path.Combine(_spritesPath, "Girl.png");
            _girl = new NPC(_gameManager.Grid, new Vector2D(18 * 32, 4
                * 32), "Girl",
                speed: 0, health: 100f, strength: 0f,
                spritePath: girlSpritePath, visualScale: 1.0,
                spriteInfo: new SpriteInfo("Girl", 48, 48));
            _girl.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_girl);

            var girlQuest = new Quest("girl_quest", "Темная просьба", "Принеси черную жижу и слизь")
                .SetDialogues(
                    "Пожалуйста, помоги мне! Мне нужна черная жижа и 10 слизей. Я знаю, это странно, но это очень важно!",
                    "Спасибо! Ты принес черную жижу и 10 слизей. Ты спас меня! Вот твоя награда - Ожерелье.",
                    "Спасибо за помощь! Теперь я в безопасности."
                )
                .AddRequiredItem("black_bottle", 1)
                .AddRequiredItem("slime_goo", 10)
                .AddRewardItem("necklace", 1)
                .SetRewardStrength(10);

            girlQuest.OnQuestStarted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест начат: {q.Name}"); };
            girlQuest.OnQuestCompleted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест завершен: {q.Name}"); };

            _availableQuests.Add("girl_quest", girlQuest);
        }

        private void InitializeSchoolGirl()
        {
            string schoolGirlSpritePath = Path.Combine(_spritesPath, "SchoolGirl.png");
            _schoolGirl = new NPC(_gameManager.Grid, new Vector2D(30 * 32, 35 * 32), "SchoolGirl",
                speed: 0, health: 100f, strength: 0f,
                spritePath: schoolGirlSpritePath, visualScale: 0.8,
                spriteInfo: new SpriteInfo("SchoolGirl", 128, 128));
            _schoolGirl.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_schoolGirl);

            var schoolGirlQuest = new Quest("schoolgirl_quest", "Потерянное ожерелье", "Найди ожерелье")
                .SetDialogues(
                    "Привет! Я потеряла свое любимое ожерелье. Ты не мог бы его найти для меня?",
                    "Ура! Ты нашел мое ожерелье! Спасибо большое! Держи этот серебряный предмет в качестве награды.",
                    "Спасибо, что нашел мое ожерелье! Я так рада!"
                )
                .AddRequiredItem("necklace", 1)
                .AddRewardItem("silver_thing", 1)
                .SetRewardStrength(5);

            schoolGirlQuest.OnQuestStarted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест начат: {q.Name}"); };
            schoolGirlQuest.OnQuestCompleted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест завершен: {q.Name}"); };

            _availableQuests.Add("schoolgirl_quest", schoolGirlQuest);
        }

        private void InitializeWoman()
        {
            string womanSpritePath = Path.Combine(_spritesPath, "Woman.png");
            _woman = new NPC(_gameManager.Grid, new Vector2D(70 * 32, 65 * 32), "Woman",
                speed: 0, health: 100f, strength: 0f,
                spritePath: womanSpritePath, visualScale: 1.0,
                spriteInfo: new SpriteInfo("Woman", 48, 48));
            _woman.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_woman);

            var womanQuest = new Quest("woman_quest", "Нужен пояс", "Принеси пояс для платья")
                .SetDialogues(
                    "Здравствуйте! У меня проблема - порвался пояс на платье. Не могли бы вы найти мне новый пояс?",
                    "О, какой красивый пояс! Спасибо вам огромное! Возьмите эту желтую штуку в благодарность.",
                    "Спасибо за пояс! Теперь мое платье снова в порядке."
                )
                .AddRequiredItem("belt", 1)
                .AddRewardItem("yellow_thing", 1)
                .SetRewardStrength(8);

            womanQuest.OnQuestStarted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест начат: {q.Name}"); };
            womanQuest.OnQuestCompleted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест завершен: {q.Name}"); };

            _availableQuests.Add("woman_quest", womanQuest);
        }

        private void InitializeGorgon()
        {
            string GorgonSpritePath = Path.Combine(_spritesPath, "Gorgon.png");
            _gorgon = new Enemy(
                grid: _gameManager.Grid,
                position: new Vector2D(82 * 32, 79 * 32),
                speed: 0,
                id: "Gorgon",
                health: 100f,
                strength: 0f,
                spritePath: GorgonSpritePath,
                visualScale: 1.0,
                spriteInfo: new SpriteInfo("Gorgon", 128, 128),
                type: "Gorgon");
            _gorgon.SetState(CharacterState.Idle);
            _gameManager.AddCharacter(_gorgon);
        }

        private void InitializeFinn()
        {
            string finnSpritePath = Path.Combine(_spritesPath, "Orc.png");
            _finn = new NSM_NPC(_gameManager.Grid, new Vector2D(25 * 32, 25 * 32), "Finn",
                speed: 2.5, health: 60f, strength: 8f,
                visualScale: 1.0, spritePath: finnSpritePath,
                spriteInfo: new SpriteInfo("Orc", 48, 48));

            _gorgonKillQuest = new Quest("gorgon_kill_quest", "Убийство Горгоны", "Победи древнее чудовище - Горгону")
                .SetDialogues(
                    "Ты в юго-восточный лес не ходи, там опасно - Горгона живет.",
                    "Невероятно! Ты действительно победил Горгону!",
                    "Спасибо, что избавил нас от Горгоны! Теперь мы можем жить в безопасности."
                )
                .SetRequiredEnemyKill("Gorgon")
                .AddRewardItem("sword", 1)
                .SetRewardStrength(30);

            _gorgonKillQuest.OnQuestStarted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест начат: {q.Name}"); };
            _gorgonKillQuest.OnQuestCompleted += (q) => { System.Diagnostics.Debug.WriteLine($"Квест завершен: {q.Name}"); };

            _availableQuests.Add("gorgon_kill_quest", _gorgonKillQuest);

            ConfigureFinnStates();
            _finn.SetState(CharacterState.Decision);
            _gameManager.AddCharacter(_finn);
        }

        private void SpawnBee(Vector2D spawnPos)
        {
            string beeSpritePath = Path.Combine(_spritesPath, "Bee.png");
            Enemy bee = new Enemy(_gameManager.Grid, spawnPos, speed: 2.8,
                id: $"Bee_{DateTime.Now.Ticks}",
                health: 20f,
                strength: 6f,
                spritePath: beeSpritePath,
                visualScale: 0.8,
                spriteInfo: new SpriteInfo("Bee", 48, 48),
                type: "Bee");

            SetupBeeBehavior(bee);
            _gameManager.AddCharacter(bee);
        }

        private void SetupBeeBehavior(Enemy bee)
        {
            if (bee.Type != "Bee")
                return;

            bee.ConfigureState(CharacterState.Idle,
                update: (machine) =>
                {
                    if (_rng.NextDouble() < 0.02)
                    {
                        double angle = _rng.NextDouble() * Math.PI * 2;
                        bee.Move(new Vector2D(Math.Cos(angle), Math.Sin(angle)));
                    }

                    if (_player != null && _player.IsAlive && Vector2D.Distance(bee.Position, _player.Position) < 200.0)
                        bee.SetState(CharacterState.Chase);
                });

            bee.ConfigureState(CharacterState.Chase,
                update: (machine) =>
                {
                    if (_player == null || !_player.IsAlive)
                    {
                        bee.SetState(CharacterState.Idle);
                        return;
                    }
                    double dist = Vector2D.Distance(bee.Position, _player.Position);
                    if (dist < 35.0)
                        bee.SetState(CharacterState.Attack);
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

            bee.ConfigureState(CharacterState.Attack,
                update: (machine) =>
                {
                    if (_player == null || !_player.IsAlive)
                    {
                        bee.SetState(CharacterState.Idle);
                        return;
                    }
                    bee.Stop();
                    double dist = Vector2D.Distance(bee.Position, _player.Position);
                    if (dist > 45.0)
                        bee.SetState(CharacterState.Chase);
                    else if (_rng.NextDouble() < 0.08)
                    {
                        bee.Attack(_player);
                        TriggerShake(2.0);
                        ShowFloatingDamageNumber(_player.Position, bee.Strength, false);
                        if (!_player.IsAlive)
                            _gameManager.SendEvent(Event_.PlayerDead);
                    }
                });

            bee.SetState(CharacterState.Idle);
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

            bool isBottomLeft = (tileX < HALF_MAP && tileY >= HALF_MAP);
            bool isTopRight = (tileX >= HALF_MAP && tileY < HALF_MAP);

            if (isBottomLeft)
            {
                string slimeSpritePath = Path.Combine(_spritesPath, "Slime.png");
                Enemy slime = new Enemy(_gameManager.Grid, spawnPos, speed: 1.5,
                    id: $"Slime_{DateTime.Now.Ticks}",
                    health: 30f,
                    strength: 4f,
                    spritePath: slimeSpritePath,
                    visualScale: 1.0,
                    spriteInfo: new SpriteInfo("Slime", 48, 48),
                    type: "Slime");
                SetupSlimeBehavior(slime);
                _gameManager.AddCharacter(slime);
            }
            else if (isTopRight)
            {
                SpawnBee(spawnPos);
            }
            else
            {
                if (_rng.NextDouble() < 0.5)
                {
                    string slimeSpritePath = Path.Combine(_spritesPath, "Slime.png");
                    Enemy slime = new Enemy(_gameManager.Grid, spawnPos, speed: 1.5,
                        id: $"Slime_{DateTime.Now.Ticks}",
                        health: 30f,
                        strength: 4f,
                        spritePath: slimeSpritePath,
                        visualScale: 1.0,
                        spriteInfo: new SpriteInfo("Slime", 48, 48),
                        type: "Slime");
                    SetupSlimeBehavior(slime);
                    _gameManager.AddCharacter(slime);
                }
                else
                {
                    SpawnBee(spawnPos);
                }
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

        private void GiveAllQuestItemsFromGorgon()
        {
            if (_gorgonHasGivenItems) return;

            var questItems = new Dictionary<string, int>
            {
                { "slime_goo", 15 },
                { "black_bottle", 1 },
                { "necklace", 1 },
                { "belt", 1 },
                { "cheese", 2 }
            };

            foreach (var item in questItems)
            {
                if (_itemPrefabs.ContainsKey(item.Key))
                {
                    for (int i = 0; i < item.Value; i++)
                    {
                        _player.Inventory.AddItem(_itemPrefabs[item.Key], 1);
                    }
                }
            }

            _gorgonHasGivenItems = true;
            ShowFloatingMessage("Горгона выбросила все необходимые предметы и 2 куска сыра!", 3.0);
            _gameManager.PlaySound("item.mp3", 0.8f);
            TriggerShake(5.0);
        }

        private void ConfigureFinnStates()
        {
            _finn.ConfigureState(CharacterState.Idle,
                onEnter: () => _finn.Stop(),
                update: (machine) =>
                {
                    if (_rng.NextDouble() < 0.01)
                    {
                        double angle = _rng.NextDouble() * Math.PI * 2;
                        _finn.Move(new Vector2D(Math.Cos(angle), Math.Sin(angle)));
                    }
                    Enemy nearbyEnemy = FindNearestEnemy(120);
                    if (nearbyEnemy != null) { _finn.SetState(CharacterState.Chase); return; }
                    if (_rng.NextDouble() < 0.02) _finn.SetState(CharacterState.Decision);
                });

            _finn.ConfigureState(CharacterState.Chase,
                update: (machine) =>
                {
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
                    Enemy target = FindNearestEnemy(60);
                    if (target != null && target.IsAlive)
                    {
                        target.TakeDamage(_finn.Strength);
                        ShowFloatingDamageNumber(target.Position, _finn.Strength, false);
                        if (!target.IsAlive)
                        {
                            if (target.Type == "Slime")
                                DropItem("slime_goo", target.Position);
                            else if (target.Type == "Bee")
                                DropItem("honey", target.Position);
                        }
                        _finn.SetState(CharacterState.Decision);
                    }
                    else _finn.SetState(CharacterState.Decision);
                });

            _finn.AddTransition(CharacterState.Idle, CharacterState.Chase, 0.30);
            _finn.AddTransition(CharacterState.Idle, CharacterState.Idle, 0.70);
            _finn.AddTransition(CharacterState.Chase, CharacterState.Idle, 0.20);
            _finn.AddTransition(CharacterState.Chase, CharacterState.Attack, 0.25);
            _finn.AddTransition(CharacterState.Chase, CharacterState.Chase, 0.55);
            _finn.AddTransition(CharacterState.Attack, CharacterState.Idle, 0.30);
            _finn.AddTransition(CharacterState.Attack, CharacterState.Chase, 0.70);
        }

        private void SetupSlimeBehavior(Enemy slime)
        {
            if (slime.Type != "Slime")
                return;

            slime.ConfigureState(CharacterState.Idle,
                update: (machine) =>
                {
                    slime.Stop();
                    if (_player != null && _player.IsAlive && Vector2D.Distance(slime.Position, _player.Position) < 150.0)
                        slime.SetState(CharacterState.Chase);
                });

            slime.ConfigureState(CharacterState.Chase,
                update: (machine) =>
                {
                    if (_player == null || !_player.IsAlive)
                    {
                        slime.SetState(CharacterState.Idle);
                        return;
                    }
                    double dist = Vector2D.Distance(slime.Position, _player.Position);
                    if (dist < 40.0)
                        slime.SetState(CharacterState.Attack);
                    else
                        slime.Move((_player.Position - slime.Position).Normalize());
                });

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
                    if (dist > 50.0)
                        slime.SetState(CharacterState.Chase);
                    else if (_rng.NextDouble() < 0.05)
                    {
                        slime.Attack(_player);
                        TriggerShake(3.0);
                        ShowFloatingDamageNumber(_player.Position, slime.Strength, false);
                        if (!_player.IsAlive)
                            _gameManager.SendEvent(Event_.PlayerDead);
                    }
                });

            slime.SetState(CharacterState.Idle);
        }

        private void DropGorgonItems(Vector2D position)
        {
            var questItems = new Dictionary<string, int>
            {
                { "slime_goo", 15 },
                { "black_bottle", 1 },
                { "necklace", 1 },
                { "belt", 1 }
            };

            questItems["cheese"] = 2;

            foreach (var item in questItems)
            {
                if (_itemPrefabs.ContainsKey(item.Key))
                {
                    for (int i = 0; i < item.Value; i++)
                    {
                        DropItem(item.Key, position);
                    }
                }
            }

            ShowFloatingMessage("Горгона рассыпалась в прах, оставив все свои сокровища!", 3.0);
            _gameManager.PlaySound("item.mp3", 0.8f);
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
                { 'A', (TileType.Floor, "A_hint", 5,5) }, { 'D', (TileType.Floor, "D_hint", 5,5) }
            };
            string mapPath = Path.Combine(_mapsPath, "level2.txt");
            string largeDecorPath = Path.Combine(_mapsPath, "decor_large2.txt");
            if (File.Exists(mapPath)) gm.LoadMap(mapPath, largeDecorPath, backgroundMappings, largeDecorMappings);
            else CreateDefaultMap(gm);
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

        private void SubscribeInventoryEvents()
        {
            if (_player != null && _player.Inventory != null)
            {
                _player.Inventory.ItemsChanged += OnInventoryChanged;
            }
        }

        private void OnInventoryChanged(IEnumerable<int> indexes)
        {
            if (InventoryPanel.Visibility == Visibility.Visible)
            {
                RefreshInventory();
            }
        }

        // ==================== МЕТОДЫ ИНВЕНТАРЯ ====================

        private void ToggleInventory()
        {
            if (InventoryPanel.Visibility == Visibility.Visible)
            {
                InventoryPanel.Visibility = Visibility.Collapsed;
                InventoryPanel.Focusable = false;
            }
            else
            {
                RefreshInventory();
                InventoryPanel.Visibility = Visibility.Visible;
                InventoryPanel.Focusable = true;
                InventoryPanel.Focus();
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
                {
                    _inventoryItems.Add(new ItemWrapper { Item = item });
                }
            }

            if (_selectedInventoryIndex >= _inventoryItems.Count)
                _selectedInventoryIndex = _inventoryItems.Count > 0 ? _inventoryItems.Count - 1 : 0;

            UpdateInventorySelection();
            UpdateInventoryDescription();

            InventoryItemsControl.ItemsSource = null;
            InventoryItemsControl.ItemsSource = _inventoryItems;
        }

        private void UpdateInventorySelection()
        {
            for (int i = 0; i < _inventoryItems.Count; i++)
                _inventoryItems[i].IsSelected = (i == _selectedInventoryIndex);
            InventoryItemsControl.ItemsSource = null;
            InventoryItemsControl.ItemsSource = _inventoryItems;
        }

        private void UpdateInventoryDescription()
        {
            if (_selectedInventoryIndex >= 0 && _selectedInventoryIndex < _inventoryItems.Count)
            {
                var item = _inventoryItems[_selectedInventoryIndex].Item;
                InventoryDescriptionText.Text = $"{item.Description}";
            }
            else
            {
                InventoryDescriptionText.Text = "Нет предметов";
            }
        }

        private void MoveInventorySelection(int delta)
        {
            if (_inventoryItems.Count == 0) return;
            _selectedInventoryIndex += delta;
            if (_selectedInventoryIndex < 0) _selectedInventoryIndex = _inventoryItems.Count - 1;
            if (_selectedInventoryIndex >= _inventoryItems.Count) _selectedInventoryIndex = 0;
            UpdateInventorySelection();
            UpdateInventoryDescription();
        }

        private void UseSelectedInventoryItem()
        {
            if (_selectedInventoryIndex < 0 || _selectedInventoryIndex >= _inventoryItems.Count) return;
            var item = _inventoryItems[_selectedInventoryIndex].Item;

            if (item.Key == "health_potion")
            {
                if (_player.Health < _player.MaxHealth)
                {
                    float healAmount = 30f;
                    _player.Heal(healAmount);

                    for (int i = 0; i < _player.Inventory.TotalSlots; i++)
                    {
                        var invItem = _player.Inventory.GetItem(i);
                        if (invItem == item)
                        {
                            _player.Inventory.ModifyItemQuantity(i, -1);
                            break;
                        }
                    }

                    ShowFloatingMessage($"Вы использовали {item.Name} и восстановили {healAmount} HP.", 2.0);
                    RefreshInventory();

                    if (_inventoryItems.Count == 0)
                        _selectedInventoryIndex = -1;
                    else if (_selectedInventoryIndex >= _inventoryItems.Count)
                        _selectedInventoryIndex = _inventoryItems.Count - 1;

                    UpdateInventorySelection();
                    UpdateInventoryDescription();
                }
                else
                {
                    ShowFloatingMessage("У вас полное здоровье!", 1.5);
                }
            }
            else
            {
                ShowFloatingMessage($"Нельзя использовать {item.Name} (пока что)", 1.5);
            }
        }

        // ==================== ОСТАЛЬНЫЕ МЕТОДЫ ====================

        private void DropItem(string itemId, Vector2D position)
        {
            if (!_itemPrefabs.ContainsKey(itemId))
            {
                System.Diagnostics.Debug.WriteLine($"Предмет с ID '{itemId}' не найден в словаре!");
                return;
            }

            Item originalItem = _itemPrefabs[itemId];
            Item itemToDrop = new Item(
                originalItem.Key,
                originalItem.Name,
                originalItem.Description,
                originalItem.IconPath,
                originalItem.IsStackable,
                1
            );

            ImageSource source = null;
            string iconPath = Path.Combine(_itemsPath, $"{itemId}.png");

            if (File.Exists(iconPath))
            {
                var bmp = new BitmapImage(new Uri(iconPath));
                bmp.Freeze();
                source = bmp;
            }
            else
            {
                var drawing = new DrawingGroup();
                Brush defaultColor = Brushes.LimeGreen;

                if (itemId.Contains("sword")) defaultColor = Brushes.Silver;
                else if (itemId.Contains("potion")) defaultColor = Brushes.Red;
                else if (itemId.Contains("honey")) defaultColor = Brushes.Gold;
                else if (itemId.Contains("gorgon")) defaultColor = Brushes.Purple;

                drawing.Children.Add(new GeometryDrawing
                {
                    Brush = defaultColor,
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
                Tag = itemToDrop,
                Opacity = 1.0
            };

            Canvas.SetLeft(image, position.X - 12);
            Canvas.SetTop(image, position.Y - 12);
            Canvas.SetZIndex(image, 4);
            GameArea.Children.Add(image);

            image.Opacity = 0;
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.2),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            image.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var bounceAnimation = new DoubleAnimation
            {
                From = position.Y - 30,
                To = position.Y - 12,
                Duration = TimeSpan.FromSeconds(0.35),
                EasingFunction = new BounceEase { Bounces = 2, Bounciness = 2, EasingMode = EasingMode.EaseOut }
            };
            image.BeginAnimation(Canvas.TopProperty, bounceAnimation);
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

            double attackRange = 60.0;
            float playerDamage = _player.Strength;
            bool hitSomething = false;

            for (int i = _gameManager.Characters.Count - 1; i >= 0; i--)
            {
                var charac = _gameManager.Characters[i];

                if (charac is Enemy enemy && enemy.IsAlive && Vector2D.Distance(_player.Position, enemy.Position) <= attackRange)
                {
                    enemy.TakeDamage(playerDamage);
                    hitSomething = true;
                    ShowFloatingDamageNumber(enemy.Position, playerDamage, false);
                    TriggerShake(2.0);
                    _gameManager.PlaySound("attack.mp3", 0.6f);

                    if (!enemy.IsAlive)
                    {
                        if (enemy.Type == "Slime")
                        {
                            DropItem("slime_goo", enemy.Position);
                            _gameManager.RemoveCharacter(enemy);
                        }
                        else if (enemy.Type == "Bee")
                        {
                            DropItem("honey", enemy.Position);
                            _gameManager.RemoveCharacter(enemy);
                        }
                        else if (enemy.Type == "Gorgon")
                        {
                            DropGorgonItems(enemy.Position);
                            _gameManager.RemoveCharacter(enemy);

                            if (_gorgon == enemy)
                            {
                                _gorgon = null;
                            }

                            if (!_gorgonDefeated)
                            {
                                _gorgonDefeated = true;

                                if (_gorgonKillQuest != null && _gorgonKillQuest.Status == QuestStatus.Active)
                                {
                                    _gorgonKillQuest.EnemyDefeated = true;
                                    ShowFloatingMessage("Горгона побеждена! Вернись к Финну за наградой.", 3.0);
                                }
                            }
                        }
                        else
                        {
                            _gameManager.RemoveCharacter(enemy);
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

            if (npc == _questAlly)
                _availableQuests.TryGetValue("slime_quest", out quest);
            else if (npc == _girl)
                _availableQuests.TryGetValue("girl_quest", out quest);
            else if (npc == _schoolGirl)
                _availableQuests.TryGetValue("schoolgirl_quest", out quest);
            else if (npc == _woman)
                _availableQuests.TryGetValue("woman_quest", out quest);

            if (quest == null) return;

            if (quest.Status == QuestStatus.NotStarted)
            {
                quest.Start();
                ShowDialogue(quest.StartDialogue);
                return;
            }

            if (quest.Status == QuestStatus.Active)
            {
                if (quest.IsComplete(player.Inventory))
                {
                    foreach (var required in quest.RequiredItems)
                    {
                        player.Inventory.RemoveItem(required.Key, required.Value);
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

                    quest.Status = QuestStatus.Completed;

                    ShowDialogue(quest.CompletionDialogue, "Может мне дорожным знаком работать, а?");
                    ShowFloatingMessage($"Квест выполнен! +{quest.RewardStrength} Силы!", 3.0);
                    _gameManager?.PlaySound("item.mp3", 0.6f);
                }
                else
                {
                    ShowDialogue("Если не знаешь как сражаться, попробуй нажать на кнопку E");
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
                _gorgonKillQuest.Start();
                ShowDialogue(_gorgonKillQuest.StartDialogue);
            }
            else if (_gorgonKillQuest.Status == QuestStatus.Active)
            {
                if (_gorgonKillQuest.EnemyDefeated && _gorgonDefeated)
                {
                    _gorgonKillQuest.Complete();

                    foreach (var reward in _gorgonKillQuest.RewardsItems)
                    {
                        if (_itemPrefabs.ContainsKey(reward.Key))
                        {
                            _player.Inventory.AddItem(_itemPrefabs[reward.Key], reward.Value);
                        }
                    }

                    if (_gorgonKillQuest.RewardStrength > 0)
                        _player.Strength += _gorgonKillQuest.RewardStrength;

                    ShowFloatingMessage($"Квест выполнен! +{_gorgonKillQuest.RewardStrength} Силы!", 3.0);
                    ShowDialogue(_gorgonKillQuest.CompletionDialogue);
                    _gameManager.PlaySound("item.mp3", 0.8f);

                    _gameManager.SendEvent(Event_.GorgonDefeated);
                }
                else
                {
                    ShowDialogue("Горгона всё ещё там... .");
                }
            }
            else if (_gorgonKillQuest.Status == QuestStatus.Completed)
            {
                ShowDialogue(_gorgonKillQuest.AlreadyCompletedDialogue);
            }
        }

        private void HandleGorgonInteraction()
        {
            _gorgonInteractionCount++;

            string[] annoyedDialogues = {
                "Пшел вон отсюда, у меня для тебя ничего нет.",
                "Я же сказала - отстань!",
                "Сколько можно приставать?!"
            };

            if (_gorgonInteractionCount >= GORGON_ANNOYANCE_THRESHOLD && !_gorgonHasGivenItems)
            {
                ShowDialogue("Забери это и проваливай!");
                GiveAllQuestItemsFromGorgon();
                TriggerShake(8.0);
                _gameManager.PlaySound("attack.mp3", 0.9f);
            }
            else if (_gorgonHasGivenItems)
            {
                ShowDialogue("Я же сказала - проваливай! Больше ничего не дам!");
            }
            else
            {
                int dialogueIndex = Math.Min(_gorgonInteractionCount - 1, annoyedDialogues.Length - 1);
                ShowDialogue(annoyedDialogues[dialogueIndex]);
            }
        }

        private bool TryInteractWithNPC()
        {
            Character nearestNPC = null;
            double minDistance = 60.0;

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
                HandleGorgonInteraction();
            }
            else if (nearestNPC is NSM_NPC && nearestNPC == _finn)
            {
                HandleFinnDialogue();
            }
            else if (nearestNPC is NPC npc)
            {
                HandleQuestDialogue(_player, npc);
            }

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
                    ShowFloatingMessage($"Подобрано: {itemToPickup.Name}", 1.5);
                    return true;
                }
                else ShowFloatingMessage("Инвентарь полон!", 1.0);
            }
            return false;
        }

        private void TryInteractWithFinn()
        {
            if (_finn == null || !_finn.IsAlive || _player == null || !_player.IsAlive) return;
            if (Vector2D.Distance(_player.Position, _finn.Position) <= 60)
            {
                HandleFinnDialogue();
            }
        }

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

            _tutorialCompleted = false;
            InitializeGame();

            Focus();
        }

        public void ShowDialogue(params string[] texts)
        {
            if (texts == null || texts.Length == 0) return;

            _player.Stop();
            

            // Добавляем все строки в очередь
            foreach (var text in texts)
            {
                _dialogueQueue.Enqueue(text);
            }

            // Если диалоговое окно не видно - показываем первое сообщение
            if (DialogueBox.Visibility != Visibility.Visible && _dialogueQueue.Count > 0)
            {
                ShowNextDialogue();
            }
        }

        private void ShowNextDialogue()
        {
            if (_dialogueQueue.Count > 0)
            {
                DialogueText.Text = _dialogueQueue.Dequeue();
                DialogueBox.Visibility = Visibility.Visible;
            }
        }

        public void ShowFloatingMessage(string message, double durationSeconds)
        {
            var textBlock = new TextBlock { Text = message, FontSize = 14, Padding = new Thickness(15), TextAlignment = TextAlignment.Center, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)) };
            OverlayCanvas.Children.Add(textBlock);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
            timer.Tick += (s, args) => { timer.Stop(); OverlayCanvas.Children.Remove(textBlock); };
            timer.Start();
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
                Opacity = 1,
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
                double targetX = (ActualWidth / 2 - _player.Position.X * _currentZoom);
                double targetY = (ActualHeight / 2 - _player.Position.Y * _currentZoom);
                _cameraTransform.X = targetX;
                _cameraTransform.Y = targetY;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_player != null && _player.IsAlive) CenterCameraOnPlayer();

            double baseWidth = 800;
            double scale = ActualWidth / baseWidth;
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
            var currentState = _gameManager.FSM.CurrentState;
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

        private void StartSpawnTimer()
        {
            _spawnTimer?.Stop();
            _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
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
                    _tutorialCompleted = true;
                    _gameManager.SetState(State_.Game);
                    return;
                }
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
            base.OnKeyDown(e);

            // Обработка инвентаря
            if (InventoryPanel.Visibility == Visibility.Visible)
            {
                switch (e.Key)
                {
                    case Key.I:
                    case Key.Escape:
                        ToggleInventory();
                        e.Handled = true;
                        break;
                    case Key.W:
                        MoveInventorySelection(-1);
                        e.Handled = true;
                        break;
                    case Key.S:
                        MoveInventorySelection(1);
                        e.Handled = true;
                        break;
                    case Key.Enter:
                        UseSelectedInventoryItem();
                        e.Handled = true;
                        break;
                }
                return;
            }

            var currentState = _gameManager.FSM.CurrentState;
            if (e.Key == Key.R && currentState.Id == _gameManager._endState.Id)
            {
                Restart();
                e.Handled = true;
                return;
            }

            if (currentState.Id == _gameManager._tutorialState.Id)
            {
                if (e.Key == Key.W || e.Key == Key.S || e.Key == Key.A || e.Key == Key.D || e.Key == Key.E || e.Key == Key.I || e.Key == Key.Space)
                {
                    HandleInput();
                    e.Handled = true;
                }
                return;
            }

            if (currentState.Id == _gameManager._gameState.Id)
            {
                if (e.Key == Key.I)
                {
                    ToggleInventory();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.E)
                {
                    TryPlayerAttack();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.F)
                {
                    if (DialogueBox.Visibility == Visibility.Visible)
                    {
                        // Закрываем текущее окно и показываем следующее из очереди
                        DialogueBox.Visibility = Visibility.Collapsed;

                        if (_dialogueQueue.Count > 0)
                        {
                            ShowNextDialogue();
                        }
                    }
                    else
                    {
                        if (!TryPickupItem() && !TryInteractWithNPC())
                            TryInteractWithFinn();
                    }
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Escape)
                    OnExitToMenu?.Invoke();

                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            e.Handled = true;
        }
    }
}