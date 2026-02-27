using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameProj
{
    /// <summary>
    /// Базовый класс для всех игровых событий.
    /// Служит маркерным интерфейсом для событий, обрабатываемых системой.
    /// </summary>
    public abstract class GameEvent { }

    /// <summary>
    /// Событие взаимодействия между персонажами.
    /// Содержит ссылку на персонажа, с которым произошло взаимодействие.
    /// </summary>
    public class InteractionEvent : GameEvent
    {
        public Character Other { get; }
        public InteractionEvent(Character other)
        {
            Other = other ?? throw new ArgumentNullException(nameof(other));
        }
    }

    /// <summary>
    /// Состояния, в которых может находиться персонаж.
    /// Определяют текущее поведение и возможные действия.
    /// </summary>
    public enum CharacterState
    {
        Idle,       // Бездействие
        Patrol,     // Патрулирование по заданным точкам
        Chase,      // Преследование цели
        Flee,       // Бегство от опасности
        Dead,       // Смерть персонажа
        Attack,     // Атака цели
        GoToItem,   // Движение к предмету
        Decision    // Принятие решения (выбор следующего состояния)
    }

    /// <summary>
    /// Двумерный вектор для представления позиций, скоростей и направлений.
    /// Предоставляет основные математические операции для работы с векторами.
    /// </summary>
    public class Vector2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>
        /// Создает новый вектор с указанными координатами.
        /// </summary>
        /// <param name="x">X координата (по умолчанию 0)</param>
        /// <param name="y">Y координата (по умолчанию 0)</param>
        public Vector2D(double x = 0, double y = 0)
        {
            X = x;
            Y = y;
        }

        // Операции с векторами

        /// <summary>
        /// Вычитание векторов (покоординатное).
        /// </summary>
        public static Vector2D operator -(Vector2D a, Vector2D b) => new Vector2D(a.X - b.X, a.Y - b.Y);

        /// <summary>
        /// Сложение векторов (покоординатное).
        /// </summary>
        public static Vector2D operator +(Vector2D a, Vector2D b) => new Vector2D(a.X + b.X, a.Y + b.Y);

        /// <summary>
        /// Умножение вектора на скаляр.
        /// </summary>
        public static Vector2D operator *(Vector2D v, double k) => new Vector2D(v.X * k, v.Y * k);

        /// <summary>
        /// Деление вектора на скаляр.
        /// </summary>
        /// <exception cref="DivideByZeroException">При делении на ноль</exception>
        public static Vector2D operator /(Vector2D v, double k)
        {
            if (k == 0) throw new DivideByZeroException();
            return new Vector2D(v.X / k, v.Y / k);
        }

        /// <summary>
        /// Вычисляет длину (модуль) вектора.
        /// </summary>
        public double Length() => Math.Sqrt(X * X + Y * Y);

        /// <summary>
        /// Нормализует вектор (приводит к длине 1).
        /// Возвращает нулевой вектор если исходный вектор нулевой.
        /// </summary>
        public Vector2D Normalize()
        {
            double len = Length();
            return len == 0 ? new Vector2D(0, 0) : this / len;
        }

        /// <summary>
        /// Вычисляет квадрат расстояния между двумя векторами.
        /// Более быстрый метод чем Distance, так как не вычисляет квадратный корень.
        /// </summary>
        public static double DistanceSquared(Vector2D a, Vector2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Вычисляет расстояние между двумя векторами.
        /// </summary>
        public static double Distance(Vector2D a, Vector2D b) => Math.Sqrt(DistanceSquared(a, b));

        /// <summary>
        /// Нулевой вектор (0, 0).
        /// </summary>
        public static Vector2D Zero => new Vector2D(0, 0);

        /// <summary>
        /// Возвращает строковое представление вектора с округлением до 2 знаков.
        /// </summary>
        public override string ToString() => $"({X:F2}, {Y:F2})";

        /// <summary>
        /// Сравнивает векторы с заданной точностью (1e-6).
        /// </summary>
        public override bool Equals(object obj) => obj is Vector2D v && Math.Abs(X - v.X) < 1e-6 && Math.Abs(Y - v.Y) < 1e-6;

        /// <summary>
        /// Вычисляет хэш-код вектора.
        /// </summary>
        public override int GetHashCode() => (X.GetHashCode() * 17) ^ Y.GetHashCode();
    }

    /// <summary>
    /// Конечный автомат (Finite State Machine).
    /// Управляет переходами между состояниями на основе событий.
    /// </summary>
    /// <typeparam name="TState">Тип идентификаторов состояний</typeparam>
    /// <typeparam name="TEvent">Тип событий</typeparam>
    public class FSM<TState, TEvent>
    {
        /// <summary>
        /// Текущее состояние автомата.
        /// </summary>
        public State<TState, TEvent> CurrentState { get; private set; }

        /// <summary>
        /// Создает конечный автомат с указанным начальным состоянием.
        /// </summary>
        /// <param name="initialState">Начальное состояние</param>
        public FSM(State<TState, TEvent> initialState)
        {
            CurrentState = initialState;
            CurrentState?.Enter();
        }

        /// <summary>
        /// Устанавливает новое состояние автомата.
        /// Вызывает Exit у старого состояния и Enter у нового.
        /// </summary>
        /// <param name="newState">Новое состояние</param>
        public void SetState(State<TState, TEvent> newState)
        {
            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState?.Enter();
        }

        /// <summary>
        /// Обрабатывает событие в текущем состоянии.
        /// </summary>
        /// <param name="_event">Событие для обработки</param>
        public void HandleEvent(TEvent _event)
        {
            CurrentState?.HandleEvent(this, _event);
        }

        /// <summary>
        /// Обновляет текущее состояние (вызывает Update логику).
        /// </summary>
        public void Update()
        {
            CurrentState?.Update(this);
        }
    }

    /// <summary>
    /// Состояние конечного автомата.
    /// Содержит логику входа, выхода, обработки событий и обновления.
    /// </summary>
    /// <typeparam name="TState">Тип идентификаторов состояний</typeparam>
    /// <typeparam name="TEvent">Тип событий</typeparam>
    public class State<TState, TEvent>
    {
        /// <summary>
        /// Идентификатор состояния.
        /// </summary>
        public TState Id { get; }

        // Делегаты для обработки различных аспектов состояния
        protected Action onEnter;           // При входе в состояние
        protected Action onExit;            // При выходе из состояния
        private Action<FSM<TState, TEvent>, TEvent> eventHandler; // Обработчик событий
        private Action<FSM<TState, TEvent>> updateHandler;       // Обработчик обновления

        /// <summary>
        /// Создает состояние с указанным идентификатором.
        /// </summary>
        /// <param name="id">Идентификатор состояния</param>
        public State(TState id) => Id = id;

        // Методы настройки поведения состояния

        /// <summary>
        /// Устанавливает действие при входе в состояние.
        /// </summary>
        /// <param name="act">Действие при входе</param>
        public virtual void SetEnter(Action act) => onEnter = act;

        /// <summary>
        /// Устанавливает действие при выходе из состояния.
        /// </summary>
        /// <param name="act">Действие при выходе</param>
        public virtual void SetExit(Action act) => onExit = act;

        /// <summary>
        /// Устанавливает обработчик событий для состояния.
        /// </summary>
        /// <param name="handler">Обработчик событий</param>
        public virtual void SetEventHandler(Action<FSM<TState, TEvent>, TEvent> handler) => eventHandler = handler;

        /// <summary>
        /// Устанавливает обработчик обновления для состояния.
        /// </summary>
        /// <param name="handler">Обработчик обновления</param>
        public virtual void SetUpdate(Action<FSM<TState, TEvent>> handler) => updateHandler = handler;

        // Методы вызова поведения

        /// <summary>
        /// Вызывается при входе в состояние.
        /// </summary>
        public virtual void Enter() => onEnter?.Invoke();

        /// <summary>
        /// Вызывается при выходе из состояния.
        /// </summary>
        public virtual void Exit() => onExit?.Invoke();

        /// <summary>
        /// Обрабатывает событие в текущем состоянии.
        /// </summary>
        /// <param name="machine">Автомат, которому принадлежит состояние</param>
        /// <param name="_event">Событие для обработки</param>
        public virtual void HandleEvent(FSM<TState, TEvent> machine, TEvent _event) => eventHandler?.Invoke(machine, _event);

        /// <summary>
        /// Обновляет состояние (вызывается каждый кадр).
        /// </summary>
        /// <param name="machine">Автомат, которому принадлежит состояние</param>
        public virtual void Update(FSM<TState, TEvent> machine) => updateHandler?.Invoke(machine);
    }

    /// <summary>
    /// Интерфейс для объектов, способных предоставлять диалоговые реплики.
    /// </summary>
    public interface IDialogueProvider
    {
        /// <summary>
        /// Получает диалоговую реплику для указанного персонажа.
        /// </summary>
        /// <param name="other">Персонаж, с которым ведется диалог</param>
        /// <returns>Диалоговая реплика</returns>
        string GetDialogueFor(Character other);
    }

    /// <summary>
    /// Интерфейс для триггеров клетки.
    /// Триггеры вызываются при входе/выходе персонажа из клетки.
    /// </summary>
    public interface ICellTrigger
    {
        /// <summary>
        /// Вызывается при входе персонажа в клетку.
        /// </summary>
        /// <param name="character">Персонаж, вошедший в клетку</param>
        /// <param name="grid">Игровая сетка</param>
        /// <param name="x">X координата клетки</param>
        /// <param name="y">Y координата клетки</param>
        void OnEnter(Character character, GameGrid grid, int x, int y);

        /// <summary>
        /// Вызывается при выходе персонажа из клетки.
        /// </summary>
        /// <param name="character">Персонаж, вышедший из клетки</param>
        /// <param name="grid">Игровая сетка</param>
        /// <param name="x">X координата клетки</param>
        /// <param name="y">Y координата клетки</param>
        void OnExit(Character character, GameGrid grid, int x, int y);
    }

    /// <summary>
    /// Реализация триггера через лямбда-выражения.
    /// Позволяет быстро создавать триггеры без создания новых классов.
    /// </summary>
    public class LambdaTrigger : ICellTrigger
    {
        private readonly Action<Character, GameGrid, int, int> _onEnter;
        private readonly Action<Character, GameGrid, int, int> _onExit;

        /// <summary>
        /// Создает триггер с указанными действиями.
        /// </summary>
        /// <param name="onEnter">Действие при входе (опционально)</param>
        /// <param name="onExit">Действие при выходе (опционально)</param>
        public LambdaTrigger(Action<Character, GameGrid, int, int> onEnter = null,
                           Action<Character, GameGrid, int, int> onExit = null)
        {
            _onEnter = onEnter;
            _onExit = onExit;
        }

        /// <summary>
        /// Вызывается при входе персонажа в клетку.
        /// </summary>
        public void OnEnter(Character ch, GameGrid g, int x, int y) => _onEnter?.Invoke(ch, g, x, y);

        /// <summary>
        /// Вызывается при выходе персонажа из клетки.
        /// </summary>
        public void OnExit(Character ch, GameGrid g, int x, int y) => _onExit?.Invoke(ch, g, x, y);
    }

    /// <summary>
    /// Типы клеток игровой сетки.
    /// </summary>
    public enum CellType
    {
        Floor,  // Пол - проходимая клетка
        Wall    // Стена - непроходимая клетка
    }

    /// <summary>
    /// Представляет одну клетку игровой сетки.
    /// Содержит информацию о типе клетки, спрайтах, триггерах и предметах.
    /// </summary>
    public class Cell
    {
        public CellType Type { get; set; }                   // Тип клетки
        public string BackgroundSpriteId { get; set; }      // ID спрайта фона
        public string DecorSpriteId { get; set; }           // ID спрайта декорации
        public string Id { get; set; }                      // Уникальный идентификатор клетки
        public ICellTrigger Trigger { get; set; }           // Триггер клетки
        public Item ItemOnGround { get; set; }              // Предмет на земле (если есть)

        /// <summary>
        /// Создает новую клетку.
        /// </summary>
        /// <param name="type">Тип клетки</param>
        /// <param name="backgroundSpriteId">ID спрайта фона</param>
        /// <param name="decorSpriteId">ID спрайта декорации (опционально)</param>
        /// <param name="id">Уникальный идентификатор (опционально)</param>
        /// <param name="trigger">Триггер клетки (опционально)</param>
        public Cell(CellType type, string backgroundSpriteId, string decorSpriteId = null,
                   string id = null, ICellTrigger trigger = null)
        {
            Type = type;
            BackgroundSpriteId = backgroundSpriteId;
            DecorSpriteId = decorSpriteId;
            Id = id;
            Trigger = trigger;
        }

        /// <summary>
        /// Определяет, является ли клетка проходимой.
        /// Клетка проходима если она является полом (не стеной).
        /// </summary>
        public bool IsWalkable() => Type == CellType.Floor;
    }

    /// <summary>
    /// Игровая сетка (карта).
    /// Содержит двумерный массив клеток и предоставляет методы для работы с ними.
    /// </summary>
    public class GameGrid
    {
        private readonly Cell[,] _grid;  // Двумерный массив клеток
        public int Width { get; }        // Ширина сетки в клетках
        public int Height { get; }       // Высота сетки в клетках

        /// <summary>
        /// Создает новую игровую сетку указанных размеров.
        /// Все клетки инициализируются как пол (проходимые) с травяным спрайтом.
        /// </summary>
        /// <param name="width">Ширина сетки (положительное число)</param>
        /// <param name="height">Высота сетки (положительное число)</param>
        /// <exception cref="ArgumentException">При неположительных размерах</exception>
        public GameGrid(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Размеры должны быть положительными.");
            Width = width;
            Height = height;
            _grid = new Cell[width, height];

            // Инициализация всех клеток как пол с травяным спрайтом
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    _grid[x, y] = new Cell(CellType.Floor, "Grass");
        }

        /// <summary>
        /// Размещает предмет на земле в указанной клетке.
        /// </summary>
        /// <param name="x">X координата клетки</param>
        /// <param name="y">Y координата клетки</param>
        /// <param name="item">Предмет для размещения</param>
        public void PlaceItem(int x, int y, Item item)
        {
            if (!InBounds(x, y)) return;
            _grid[x, y].ItemOnGround = item;
        }

        /// <summary>
        /// Индексатор для доступа к клеткам сетки.
        /// </summary>
        /// <param name="x">X координата</param>
        /// <param name="y">Y координата</param>
        /// <exception cref="IndexOutOfRangeException">При выходе за границы сетки</exception>
        public Cell this[int x, int y]
        {
            get
            {
                if (!InBounds(x, y)) throw new IndexOutOfRangeException();
                return _grid[x, y];
            }
            set
            {
                if (!InBounds(x, y)) throw new IndexOutOfRangeException();
                _grid[x, y] = value;
            }
        }

        /// <summary>
        /// Проверяет, находятся ли координаты в пределах сетки.
        /// </summary>
        /// <param name="x">X координата</param>
        /// <param name="y">Y координата</param>
        /// <returns>True если координаты в пределах сетки</returns>
        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>
        /// Проверяет, является ли клетка проходимой.
        /// </summary>
        /// <param name="x">X координата</param>
        /// <param name="y">Y координата</param>
        /// <returns>True если клетка существует и проходима</returns>
        public bool IsWalkable(int x, int y) => InBounds(x, y) && _grid[x, y]?.IsWalkable() == true;

        /// <summary>
        /// Устанавливает свойства клетки по координатам.
        /// </summary>
        /// <param name="x">X координата</param>
        /// <param name="y">Y координата</param>
        /// <param name="type">Тип клетки</param>
        /// <param name="backgroundSpriteId">ID спрайта фона</param>
        /// <param name="decorSpriteId">ID спрайта декорации (опционально)</param>
        /// <param name="id">Уникальный идентификатор (опционально)</param>
        /// <param name="trigger">Триггер клетки (опционально)</param>
        public void SetCell(int x, int y, CellType type, string backgroundSpriteId,
                           string decorSpriteId = null, string id = null, ICellTrigger trigger = null)
        {
            this[x, y] = new Cell(type, backgroundSpriteId, decorSpriteId, id, trigger);
        }
    }

    /// <summary>
    /// Базовый класс для всех игровых персонажей.
    /// Содержит общую логику движения, здоровья, инвентаря и взаимодействия.
    /// </summary>
    public class Character
    {
        public Vector2D Position { get; set; }   // Текущая позиция персонажа
        public Vector2D Velocity { get; set; }   // Текущая скорость (направление * скорость)
        protected double Speed { get; set; }        // Максимальная скорость движения
        public float Health { get; private set; }   // Текущее здоровье
        protected float Strength { get; private set; } // Сила атаки
        public Inventory Inventory { get; protected set; } // Инвентарь персонажа
        public IDialogueProvider DialogueProvider { get; protected set; } // Поставщик диалогов
        public string Id { get; protected set; }    // Уникальный идентификатор персонажа
        // Размер хитбокса (должен быть меньше 1.0, например 0.6 или 0.7)
        public virtual double Size => 0.6;

        /// <summary>
        /// Определяет, жив ли персонаж (здоровье > 0).
        /// </summary>
        public bool IsAlive => Health > 0;

        /// <summary>
        /// Радиус взаимодействия по умолчанию.
        /// </summary>
        protected virtual double DefaultInteractionRadius => 2.0;

        /// <summary>
        /// Создает нового персонажа.
        /// </summary>
        /// <param name="startPosition">Начальная позиция</param>
        /// <param name="startSpeed">Начальная скорость</param>
        /// <param name="startHealth">Начальное здоровье</param>
        /// <param name="startStrength">Начальная сила</param>
        /// <param name="id">Идентификатор персонажа</param>
        /// <param name="inventory">Инвентарь (если null создается новый)</param>
        /// <param name="dialogueProvider">Поставщик диалогов (опционально)</param>
        public Character(
            Vector2D startPosition = default,
            double startSpeed = 1.0,
            float startHealth = 20,
            float startStrength = 1,
            string id = "Character",
            Inventory inventory = null,
            IDialogueProvider dialogueProvider = null)
        {
            Position = startPosition;
            Velocity = Vector2D.Zero;
            Speed = Math.Max(0, startSpeed);
            Health = startHealth;
            Strength = startStrength;
            Id = id;
            Inventory = inventory ?? new Inventory();
            DialogueProvider = dialogueProvider;
        }

        // Свойства только для чтения для внешнего доступа

        /// <summary>
        /// Текущая позиция персонажа (копия для предотвращения изменений извне).
        /// </summary>
        public Vector2D position => new Vector2D(Position.X, Position.Y);

        /// <summary>
        /// Текущая скорость персонажа (копия для предотвращения изменений извне).
        /// </summary>
        public Vector2D velocity => new Vector2D(Velocity.X, Velocity.Y);

        /// <summary>
        /// Текущая скорость движения.
        /// </summary>
        public double speed => Speed;

        /// <summary>
        /// Убивает персонажа.
        /// </summary>
        public virtual void Die()
        {
            if (!IsAlive) return;
            Health = 0;
            Console.WriteLine($"{Id} погиб.");
            OnDeath();
        }

        /// <summary>
        /// Восстанавливает здоровье персонажу.
        /// </summary>
        /// <param name="amount">Количество восстанавливаемого здоровья</param>
        public virtual void Heal(float amount)
        {
            if (amount <= 0 || !IsAlive) return;
            Health = Math.Min(Health + amount, 20); // Максимум 20 здоровья
        }

        /// <summary>
        /// Вызывается при смерти персонажа.
        /// Может быть переопределен в наследниках для дополнительной логики.
        /// </summary>
        protected virtual void OnDeath() { }

        /// <summary>
        /// Обновляет состояние персонажа (вызывается каждый кадр).
        /// В базовой реализации обновляет позицию на основе скорости.
        /// </summary>
        public virtual void Update()
        {
            Position += Velocity;
        }

        /// <summary>
        /// Устанавливает скорость движения в указанном направлении.
        /// </summary>
        /// <param name="direction">Направление движения (нормализуется)</param>
        public void Move(Vector2D direction)
        {
            if (direction.Length() == 0)
            {
                Velocity = Vector2D.Zero;
                return;
            }
            Velocity = direction.Normalize() * Speed;
        }

        /// <summary>
        /// Событие, возникающее при подборе предмета.
        /// </summary>
        public event Action<Character, string> OnItemPickedUp;

        /// <summary>
        /// Вызывается при подборе предмета.
        /// </summary>
        /// <param name="itemId">ID подобранного предмета</param>
        public void PickupItem(string itemId)
        {
            OnItemPickedUp?.Invoke(this, itemId);
        }

        /// <summary>
        /// Атакует указанного врага.
        /// </summary>
        /// <param name="enemy">Персонаж для атаки</param>
        public virtual void Attack(Character enemy)
        {
            if (!IsAlive || enemy == null) return;
            enemy.TakeDamage(Strength);
        }

        /// <summary>
        /// Получает урон.
        /// </summary>
        /// <param name="damage">Количество урона</param>
        public virtual void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0) return;
            Health -= damage;
            if (Health <= 0)
            {
                Health = 0;
                Die();
            }
        }

        /// <summary>
        /// Останавливает движение персонажа.
        /// </summary>
        public void Stop() => Velocity = Vector2D.Zero;

        /// <summary>
        /// Проверяет возможность взаимодействия с другим персонажем.
        /// </summary>
        /// <param name="other">Другой персонаж</param>
        /// <param name="maxDistance">Максимальное расстояние для взаимодействия (если null используется DefaultInteractionRadius)</param>
        /// <returns>True если взаимодействие возможно</returns>
        public virtual bool CanInteractWith(Character other, double? maxDistance = null)
        {
            if (other == null || !IsAlive || !other.IsAlive) return false;
            double dist = Vector2D.Distance(Position, other.Position);
            return dist <= (maxDistance ?? DefaultInteractionRadius);
        }

        /// <summary>
        /// Взаимодействует с другим персонажем.
        /// В базовой реализации выводит диалог в консоль.
        /// </summary>
        /// <param name="other">Другой персонаж</param>
        public virtual void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            var text = DialogueProvider?.GetDialogueFor(other);
            if (!string.IsNullOrEmpty(text))
            {
                Console.WriteLine($"{Id} говорит: \"{text}\"");
            }
        }

        /// <summary>
        /// Определяет ключ анимации на основе текущей скорости.
        /// </summary>
        /// <param name="velocity">Текущая скорость персонажа</param>
        /// <returns>Ключ анимации или null если персонаж не двигается</returns>
        public virtual string GetAnimationKey(Vector2D velocity)
        {
            if (velocity.Length() == 0) return null;
            double dx = Math.Abs(velocity.X);
            double dy = Math.Abs(velocity.Y);
            if (dy >= dx)
                return velocity.Y > 0 ? "_D_Walk" : "_U_Walk"; // Вниз или вверх
            else
                return velocity.X > 0 ? "_R_Walk" : "_L_Walk"; // Вправо или влево
        }

        /// <summary>
        /// Возвращает строковое представление персонажа.
        /// </summary>
        public override string ToString() => $"[{Id}] at {Position}, HP: {Health}, Alive: {IsAlive}";
    }

    /// <summary>
    /// Инвентарь персонажа.
    /// Хранит предметы в виде словаря "ID предмета -> количество".
    /// </summary>
    public class Inventory
    {
        private readonly Dictionary<string, int> _items = new Dictionary<string, int>();

        /// <summary>
        /// Словарь предметов в инвентаре (только для чтения).
        /// </summary>
        public IReadOnlyDictionary<string, int> Items => _items;

        /// <summary>
        /// Пытается добавить предмет в инвентарь.
        /// </summary>
        /// <param name="itemId">ID предмета</param>
        /// <param name="count">Количество для добавления</param>
        /// <returns>True если предмет успешно добавлен</returns>
        public bool TryAddItem(string itemId, int count = 1)
        {
            if (count <= 0) return false;
            int current = _items.TryGetValue(itemId, out int value) ? value : 0;
            _items[itemId] = current + count;
            return true;
        }

        /// <summary>
        /// Пытается удалить предмет из инвентаря.
        /// </summary>
        /// <param name="itemId">ID предмета</param>
        /// <param name="count">Количество для удаления</param>
        /// <returns>True если предмет успешно удален</returns>
        public bool TryRemoveItem(string itemId, int count = 1)
        {
            if (!_items.TryGetValue(itemId, out int current) || current < count || count <= 0)
                return false;
            if (current == count)
                _items.Remove(itemId);
            else
                _items[itemId] = current - count;
            return true;
        }

        /// <summary>
        /// Проверяет наличие предмета в инвентаре.
        /// </summary>
        /// <param name="itemId">ID предмета</param>
        /// <param name="count">Требуемое количество</param>
        /// <returns>True если предмет есть в достаточном количестве</returns>
        public bool HasItem(string itemId, int count = 1)
        {
            return _items.TryGetValue(itemId, out int current) && current >= count;
        }
    }

    /// <summary>
    /// Игровой предмет.
    /// Представляет любой объект, который может быть подобран и использован.
    /// </summary>
    public class Item
    {
        public string Id { get; }                // Уникальный идентификатор
        public string Name { get; }              // Отображаемое имя
        public string Description { get; }       // Описание предмета
        public int Price { get; }                // Цена в игровой валюте
        public bool IsStackable { get; }         // Можно ли складывать несколько в одну ячейку
        public Action<Character> UseAction { get; } // Действие при использовании
        public ImageSource Sprite { get; }       // Спрайт предмета

        /// <summary>
        /// Создает новый предмет.
        /// </summary>
        /// <param name="id">Уникальный идентификатор</param>
        /// <param name="name">Отображаемое имя</param>
        /// <param name="description">Описание (опционально)</param>
        /// <param name="price">Цена (по умолчанию 0)</param>
        /// <param name="isStackable">Можно ли складывать (по умолчанию true)</param>
        /// <param name="useAction">Действие при использовании (опционально)</param>
        /// <param name="sprite">Спрайт предмета (опционально)</param>
        public Item(string id, string name, string description = "", int price = 0,
                   bool isStackable = true, Action<Character> useAction = null,
                   ImageSource sprite = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? "";
            Price = price >= 0 ? price : throw new ArgumentOutOfRangeException(nameof(price));
            IsStackable = isStackable;
            UseAction = useAction;
            Sprite = sprite;
        }

        /// <summary>
        /// Использует предмет на указанном персонаже.
        /// </summary>
        /// <param name="user">Персонаж, использующий предмет</param>
        public void Use(Character user)
        {
            UseAction?.Invoke(user);
        }

        /// <summary>
        /// Возвращает строковое представление предмета.
        /// </summary>
        public override string ToString() => $"{Name} (ID: {Id}, Цена: {Price})";
    }

    /// <summary>
    /// Игрок - контролируемый пользователем персонаж.
    /// Наследует базового персонажа и добавляет специфичные для игрока возможности.
    /// </summary>
    public class Player : Character
    {
        /// <summary>
        /// Создает нового игрока.
        /// </summary>
        /// <param name="startPosition">Начальная позиция</param>
        /// <param name="speed">Скорость движения</param>
        /// <param name="health">Начальное здоровье</param>
        /// <param name="id">Идентификатор игрока</param>
        /// <param name="inventory">Инвентарь (если null создается новый)</param>
        /// <param name="dialogueProvider">Поставщик диалогов (опционально)</param>
        public Player(Vector2D startPosition = default, double speed = 0.2,
                     float health = 20, string id = "Player",
                     Inventory inventory = null, IDialogueProvider dialogueProvider = null)
            : base(startPosition, speed, health, 1, id, inventory, dialogueProvider)
        {
        }

        /// <summary>
        /// Устанавливает позицию игрока напрямую.
        /// Используется для телепортации или корректировки позиции при коллизиях.
        /// </summary>
        /// <param name="x">X координата</param>
        /// <param name="y">Y координата</param>
        public virtual void SetPosition(double x, double y)
        {
            Position = new Vector2D(x, y);
            Velocity = Vector2D.Zero;
        }
    }

    /// <summary>
    /// Неигровой персонаж (NPC).
    /// Персонаж, управляемый искусственным интеллектом.
    /// </summary>
    public class NPC : Character
    {
        /// <summary>
        /// Мозг NPC - конечный автомат, управляющий поведением.
        /// </summary>
        public virtual FSM<CharacterState, GameEvent> Brain { get; set; }

        /// <summary>
        /// Создает нового NPC.
        /// </summary>
        /// <param name="grid">Игровая сетка (для навигации)</param>
        /// <param name="startPosition">Начальная позиция</param>
        /// <param name="speed">Скорость движения</param>
        /// <param name="health">Начальное здоровье</param>
        /// <param name="id">Идентификатор NPC</param>
        /// <param name="brain">Конечный автомат поведения (если null создается новый)</param>
        /// <param name="inventory">Инвентарь (если null создается новый)</param>
        /// <param name="dialogueProvider">Поставщик диалогов (опционально)</param>
        public NPC(
            GameGrid grid,
            Vector2D startPosition = default,
            double speed = 1.0,
            float health = 15,
            string id = "NPC",
            FSM<CharacterState, GameEvent> brain = null,
            Inventory inventory = null,
            IDialogueProvider dialogueProvider = null)
            : base(startPosition, speed, health, 1, id, inventory, dialogueProvider)
        {
            Brain = brain;
        }

        /// <summary>
        /// Обновляет состояние NPC и его конечный автомат.
        /// </summary>
        public override void Update()
        {
            base.Update();
            Brain?.Update();
        }

        /// <summary>
        /// Взаимодействует с другим персонажем и отправляет событие в мозг NPC.
        /// </summary>
        /// <param name="other">Другой персонаж</param>
        public override void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            base.Interact(other);
            Brain?.HandleEvent(new InteractionEvent(other));
        }
    }

    /// <summary>
    /// Дракон - босс игры.
    /// Особый NPC с уникальным поведением и характеристиками.
    /// </summary>
    public class Dragon : NPC
    {
        private FSM<CharacterState, GameEvent> _dragonBrain;

        /// <summary>
        /// Создает нового дракона.
        /// </summary>
        /// <param name="grid">Игровая сетка</param>
        /// <param name="position">Позиция дракона</param>
        /// <param name="id">Идентификатор дракона</param>
        public Dragon(GameGrid grid, Vector2D position, string id = "Dragon")
            : base(grid, position, speed: 0, health: 100, id: id)
        {
            SetupDragonBrain();
            Brain = _dragonBrain;
        }

        /// <summary>
        /// Настраивает конечный автомат дракона.
        /// </summary>
        private void SetupDragonBrain()
        {
            var idleState = new State<CharacterState, GameEvent>(CharacterState.Idle);
            var deadState = new State<CharacterState, GameEvent>(CharacterState.Dead);

            // Логика состояния бездействия
            idleState.SetUpdate(machine =>
            {
                if (!IsAlive)
                {
                    machine.SetState(deadState);
                    return;
                }
                Stop();
            });

            // Логика состояния смерти
            deadState.SetEnter(() =>
            {
                Stop();
            });

            _dragonBrain = new FSM<CharacterState, GameEvent>(idleState);
        }

        /// <summary>
        /// Определяет ключ анимации для дракона.
        /// </summary>
        /// <param name="velocity">Скорость (не используется для дракона)</param>
        /// <returns>Ключ анимации</returns>
        public override string GetAnimationKey(Vector2D velocity)
        {
            if (!IsAlive)
                return "_death";      // Анимация смерти
            return "_idle";           // Анимация бездействия
        }
    }

    /// <summary>
    /// Союзник - особый NPC, который помогает игроку.
    /// Имеет сложную систему принятия решений на основе вероятностей.
    /// </summary>
    public class Ally : NPC
    {
        /// <summary>
        /// Текущее состояние союзника.
        /// </summary>
        public CharacterState CurrentState => _brain.CurrentState?.Id ?? CharacterState.Dead;

        /// <summary>
        /// Точки патрулирования (маршрут).
        /// </summary>
        public List<Vector2D> PatrolPoints { get; set; } = new List<Vector2D>();

        /// <summary>
        /// Текущая цель движения.
        /// </summary>
        public Vector2D CurrentTarget { get; set; }

        /// <summary>
        /// ID предмета, к которому движется союзник.
        /// </summary>
        public string TargetItemId { get; set; }

        /// <summary>
        /// Делегат для предоставления вероятностных переходов между состояниями.
        /// </summary>
        /// <returns>Список состояний с вероятностями перехода в них</returns>
        public delegate List<(CharacterState state, double probability)> TransitionProvider();

        private readonly TransitionProvider _transitionProvider;
        private readonly Random _random = new Random();

        /// <summary>
        /// Словарь всех состояний союзника.
        /// </summary>
        public Dictionary<CharacterState, State<CharacterState, GameEvent>> _states;

        /// <summary>
        /// Состояние принятия решения.
        /// </summary>
        private readonly State<CharacterState, GameEvent> _decisionState;

        /// <summary>
        /// Конечный автомат поведения союзника.
        /// </summary>
        public FSM<CharacterState, GameEvent> _brain;

        /// <summary>
        /// Создает нового союзника.
        /// </summary>
        /// <param name="grid">Игровая сетка</param>
        /// <param name="startPosition">Начальная позиция</param>
        /// <param name="speed">Скорость движения</param>
        /// <param name="health">Начальное здоровье</param>
        /// <param name="id">Идентификатор союзника</param>
        /// <param name="transitionProvider">Поставщик вероятностных переходов (если null используется стандартный)</param>
        /// <param name="inventory">Инвентарь (если null создается новый)</param>
        /// <param name="dialogueProvider">Поставщик диалогов (опционально)</param>
        public Ally(
            GameGrid grid,
            Vector2D startPosition = default,
            double speed = 1.0,
            float health = 15,
            string id = "Ally",
            TransitionProvider transitionProvider = null,
            Inventory inventory = null,
            IDialogueProvider dialogueProvider = null)
            : base(grid, startPosition, speed, health, id, null, inventory, dialogueProvider)
        {
            _transitionProvider = transitionProvider ?? DefaultTransitionProvider;
            _states = new Dictionary<CharacterState, State<CharacterState, GameEvent>>();

            // Создаем состояния для всех значений перечисления, кроме Decision
            foreach (CharacterState s in Enum.GetValues(typeof(CharacterState)))
            {
                if (s != CharacterState.Decision)
                    _states[s] = new State<CharacterState, GameEvent>(s);
            }

            // Создаем специальное состояние принятия решения
            _decisionState = new State<CharacterState, GameEvent>(CharacterState.Decision);
            _decisionState.SetUpdate(DecisionUpdate);
            _states[CharacterState.Decision] = _decisionState;

            // Настраиваем состояние смерти
            _states[CharacterState.Dead].SetEnter(() => Stop());

            // Создаем конечный автомат с начальным состоянием "принятие решения"
            _brain = new FSM<CharacterState, GameEvent>(_decisionState);
        }

        /// <summary>
        /// Устанавливает текущее состояние союзника.
        /// </summary>
        /// <param name="targetState">Целевое состояние</param>
        public void SetState(CharacterState targetState)
        {
            if (_states.TryGetValue(targetState, out var state))
            {
                _brain.SetState(state);
            }
            else if (targetState == CharacterState.Decision)
            {
                _brain.SetState(_decisionState);
            }
        }

        /// <summary>
        /// Настраивает поведение указанного состояния.
        /// </summary>
        /// <param name="state">Состояние для настройки</param>
        /// <param name="onEnter">Действие при входе (опционально)</param>
        /// <param name="onExit">Действие при выходе (опционально)</param>
        /// <param name="update">Действие при обновлении (опционально)</param>
        public void ConfigureState(
            CharacterState state,
            Action onEnter = null,
            Action onExit = null,
            Action<FSM<CharacterState, GameEvent>> update = null)
        {
            if (!_states.TryGetValue(state, out var s) || state == CharacterState.Dead)
                return;
            s.SetEnter(onEnter);
            s.SetExit(onExit);
            s.SetUpdate(update);
        }

        /// <summary>
        /// Устанавливает скорость движения в указанном направлении.
        /// </summary>
        /// <param name="direction">Направление движения</param>
        public void SetVelocity(Vector2D direction)
        {
            if (direction.Length() == 0)
            {
                Velocity = Vector2D.Zero;
            }
            else
            {
                Velocity = direction.Normalize() * Speed;
            }
        }

        /// <summary>
        /// Метод принятия решений (выбора следующего состояния).
        /// Использует вероятностную систему на основе весов.
        /// </summary>
        /// <param name="machine">Конечный автомат</param>
        private void DecisionUpdate(FSM<CharacterState, GameEvent> machine)
        {
            // Получаем список возможных переходов с их вероятностями
            var transitions = _transitionProvider();
            if (transitions.Count == 0) return;

            // Вычисляем общую сумму всех вероятностей
            double total = transitions.Sum(t => t.probability);
            if (total <= 0) return;

            // Генерируем случайное число в диапазоне [0, total)
            double pick = _random.NextDouble() * total;
            double cumulative = 0;

            // Выбираем состояние на основе вероятностей
            foreach (var (state, prob) in transitions)
            {
                cumulative += prob;
                if (pick <= cumulative && _states.TryGetValue(state, out var target))
                {
                    machine.SetState(target);
                    return;
                }
            }

            // Запасной вариант: выбираем первое состояние из списка
            if (_states.TryGetValue(transitions[0].state, out var fallback))
                machine.SetState(fallback);
        }

        /// <summary>
        /// Обновляет состояние союзника и его конечный автомат.
        /// </summary>
        public override void Update()
        {
            base.Update();
            _brain?.Update();
        }

        /// <summary>
        /// Взаимодействует с другим персонажем.
        /// </summary>
        /// <param name="other">Другой персонаж</param>
        public override void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            base.Interact(other);
            _brain?.HandleEvent(new InteractionEvent(other));
        }

        /// <summary>
        /// Вызывается при появлении предмета на карте.
        /// Устанавливает предмет как цель для союзника.
        /// </summary>
        /// <param name="itemId">ID предмета</param>
        /// <param name="position">Позиция предмета</param>
        public void OnItemAppeared(string itemId, Vector2D position)
        {
            TargetItemId = itemId;
            CurrentTarget = position;
        }

        /// <summary>
        /// Стандартный поставщик переходов по умолчанию.
        /// </summary>
        /// <returns>Список состояний с вероятностями</returns>
        private static List<(CharacterState state, double probability)> DefaultTransitionProvider()
        {
            return new List<(CharacterState state, double probability)>
            {
                (CharacterState.Idle, 1.0),   // Бездействие с вероятностью 1
                (CharacterState.Patrol, 5.0), // Патрулирование с вероятностью 5
            };
        }
    }

    /// <summary>
    /// Анимированный спрайт для персонажей.
    /// Работает с sprite sheet (листами спрайтов).
    /// </summary>
    public class AnimatedSprite
    {
        private readonly BitmapImage _spriteSheet;  // Лист спрайтов
        public int FrameCount { get; }              // Количество кадров в анимации
        public int FrameWidth { get; }              // Ширина одного кадра
        public int FrameHeight { get; }             // Высота одного кадра
        public double FrameDuration { get; }        // Длительность одного кадра в секундах

        /// <summary>
        /// Создает новый анимированный спрайт.
        /// </summary>
        /// <param name="sheet">Лист спрайтов</param>
        /// <param name="frameCount">Количество кадров</param>
        /// <param name="frameWidth">Ширина кадра</param>
        /// <param name="frameHeight">Высота кадра</param>
        /// <param name="frameDuration">Длительность кадра</param>
        public AnimatedSprite(BitmapImage sheet, int frameCount, int frameWidth,
                             int frameHeight, double frameDuration)
        {
            _spriteSheet = sheet;
            FrameCount = frameCount;
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            FrameDuration = frameDuration;
        }

        /// <summary>
        /// Получает указанный кадр анимации.
        /// </summary>
        /// <param name="frameIndex">Индекс кадра (0-based)</param>
        /// <returns>Изображение кадра</returns>
        public BitmapSource GetFrame(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= FrameCount)
                frameIndex = 0; // Защита от некорректного индекса

            // Вырезаем нужный кадр из листа спрайтов
            return new CroppedBitmap(
                _spriteSheet,
                new Int32Rect(frameIndex * FrameWidth, 0, FrameWidth, FrameHeight)
            );
        }
    }
}