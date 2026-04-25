using System;
using System.Collections.Generic;
using System.Linq;
using GameProj.src;

namespace GameProj
{
    /// <summary>
    /// Базовый класс для всех игровых событий, используемых в системе конечных автоматов
    /// </summary>
    public abstract class GameEvent { }

    /// <summary>
    /// Событие, возникающее при взаимодействии персонажа с другим персонажем
    /// </summary>
    public class InteractionEvent : GameEvent
    {
        public Character Other { get; private set; }

        public InteractionEvent(Character other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            Other = other;
        }
    }

    /// <summary>
    /// Возможные состояния поведения персонажа
    /// </summary>
    public enum CharacterState
    {
        Idle,       // Бездействие
        Patrol,     // Патрулирование по точкам
        Chase,      // Преследование цели
        Flee,       // Бегство от опасности
        Dead,       // Персонаж мёртв
        Attack,     // Атака цели
        GoToPoint,   // Движение к точке интереса
        Decision    // Принятие решения о следующем состоянии
    }

    /// <summary>
    /// Интерфейс для предоставления диалогового текста персонажам
    /// </summary>
    public interface IDialogueProvider
    {
        string GetDialogueFor(Character other);
    }

    /// <summary>
    /// Базовый класс для всех персонажей в игре (Игрок, NPC, Враг, Союзник)
    /// </summary>
    public class Character
    {
        // Константы
        protected const float DefaultInteractionRadius = 2.0f;  // Радиус взаимодействия по умолчанию
        protected const double DefaultCharacterSize = 24.0;     // Физический размер персонажа по умолчанию
        protected const double DefaultSpeed = 1.0;              // Скорость по умолчанию

        // Свойства движения
        public Vector2D Position { get; set; }      // Позиция персонажа
        public Vector2D Velocity { get; set; }      // Вектор скорости
        protected double Speed { get; set; }        // Скорость передвижения

        // Свойства здоровья
        public float Health { get; private set; }   // Текущее здоровье
        public float MaxHealth { get; private set; } // Максимальное здоровье
        protected float _strength;
        public float Strength
        {
            get => _strength;
            set => _strength = value;
        }
        public bool IsAlive => Health > 0;          // Жив ли персонаж

        // Инвентарь и диалоги
        public Inventory Inventory { get; protected set; }           // Инвентарь
        public IDialogueProvider DialogueProvider { get; protected set; } // Провайдер диалогов

        // Идентификация и визуальное отображение
        public string Id { get; protected set; }        // Уникальный идентификатор
        public string SpritePath { get; protected set; } // Путь к спрайту

        public int FrameSize { get; set; } = 48;        // Размер кадра спрайта в пикселях (для нарезки)

        /// <summary>
        /// Масштаб отображения персонажа. 
        /// 1.0 = нормальный размер. 
        /// 2.0 = визуально в 2 раза больше (не влияет на физику).
        /// </summary>
        public double VisualScale { get; set; } = 1.0;

        // Виртуальные свойства для переопределения в производных классах
        public virtual double Size => DefaultCharacterSize; // Физический размер для коллизий
        protected virtual double InteractionRadius => DefaultInteractionRadius;

        // События
        public event Action<Character, string> OnItemPickedUp;  // Событие подбора предмета
        public event Action<Character, float> OnHealthChanged;  // Событие изменения здоровья


        /// <summary>
        /// Конструктор класса Character
        /// </summary>
        /// <param name="startPosition">Начальная позиция</param>
        /// <param name="id">Уникальный идентификатор</param>
        /// <param name="speed">Скорость движения</param>
        /// <param name="health">Начальное здоровье</param>
        /// <param name="frameSize">Размер кадра спрайта (для нарезки)</param>
        /// <param name="strength">Сила атаки</param>
        /// <param name="inventory">Инвентарь</param>
        /// <param name="dialogueProvider">Провайдер диалогов</param>
        /// <param name="spritePath">Путь к спрайту</param>
        /// <param name="visualScale">Визуальный масштаб отображения</param>
        public Character(
            Vector2D startPosition,
            string id,
            double speed = DefaultSpeed,
            float health = 20f,
            int frameSize = 48,
            float strength = 1f,
            Inventory inventory = null,
            IDialogueProvider dialogueProvider = null,
            string spritePath = "",
            double visualScale = 1.0)
        {
            Position = startPosition;
            Velocity = Vector2D.Zero;
            Speed = Math.Max(0, speed);
            MaxHealth = health;
            FrameSize = frameSize;
            Health = health;
            Strength = strength;
            VisualScale = visualScale;

            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            Id = id;

            Inventory = inventory ?? new Inventory();
            DialogueProvider = dialogueProvider;
            SpritePath = spritePath;
        }

        /// <summary>
        /// Обновляет позицию персонажа на основе вектора скорости
        /// </summary>
        public virtual void Update()
        {
            if (!IsAlive) return;
            Position += Velocity;
        }

        /// <summary>
        /// Перемещает персонажа в указанном направлении
        /// </summary>
        /// <param name="direction">Направление движения</param>
        public void Move(Vector2D direction)
        {
            if (!IsAlive) return;
            double lenSq = direction.X * direction.X + direction.Y * direction.Y;
            if (lenSq < 0.001f)
            {
                Velocity = Vector2D.Zero;
            }
            else
            {
                Velocity = direction.Normalize() * Speed;
            }
        }

        /// <summary>
        /// Останавливает движение персонажа
        /// </summary>
        public void Stop() => Velocity = Vector2D.Zero;

        /// <summary>
        /// Убивает персонажа
        /// </summary>
        public virtual void Die()
        {
            if (!IsAlive) return;
            Health = 0;
            Stop();
            OnDeath();
        }

        /// <summary>
        /// Лечит персонажа на указанную величину
        /// </summary>
        /// <param name="amount">Количество восстанавливаемого здоровья</param>
        public virtual void Heal(float amount)
        {
            if (amount <= 0 || !IsAlive) return;

            float oldHealth = Health;
            Health = Math.Min(Health + amount, MaxHealth);
            float actualHeal = Health - oldHealth;

            if (actualHeal > 0)
            {
                OnHealthChanged?.Invoke(this, actualHeal);
            }
        }

        /// <summary>
        /// Наносит урон персонажу
        /// </summary>
        /// <param name="damage">Величина урона</param>
        public virtual void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0) return;

            float oldHealth = Health;
            Health -= damage;

            float actualDamage = oldHealth - Health;

            if (Health <= 0)
            {
                Health = 0;
                Die();
            }

            // ВЫЗЫВАЕМ СОБЫТИЕ ДЛЯ ОТОБРАЖЕНИЯ УРОНА
            OnHealthChanged?.Invoke(this, -actualDamage);
        }

        /// <summary>
        /// Атакует другого персонажа
        /// </summary>
        /// <param name="enemy">Цель атаки</param>
        public virtual void Attack(Character enemy)
        {
            if (!IsAlive || enemy == null || !enemy.IsAlive) return;
            enemy.TakeDamage(Strength);
        }

        /// <summary>
        /// Вызывается при смерти персонажа - может быть переопределён для кастомного поведения
        /// </summary>
        protected virtual void OnDeath() { }

        /// <summary>
        /// Проверяет, может ли персонаж взаимодействовать с другим персонажем
        /// </summary>
        /// <param name="other">Другой персонаж</param>
        /// <param name="maxDistance">Максимальная дистанция взаимодействия</param>
        public virtual bool CanInteractWith(Character other, double? maxDistance = null)
        {
            if (other == null || !IsAlive || !other.IsAlive) return false;
            var radius = maxDistance ?? InteractionRadius;
            return Vector2D.Distance(Position, other.Position) <= radius;
        }

        /// <summary>
        /// Выполняет взаимодействие с другим персонажем
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
        /// Подбирает предмет из игрового мира
        /// </summary>
        /// <param name="itemId">Идентификатор предмета</param>
        public void PickupItem(string itemId)
        {
            OnItemPickedUp?.Invoke(this, itemId);
        }

        /// <summary>
        /// Использует предмет из инвентаря
        /// </summary>
        /// <param name="itemId">Идентификатор предмета</param>
        /// <returns>True если предмет успешно использован</returns>
        public bool UseItem(string itemId)
        {
            if (Inventory.HasItem(itemId))
            {
                if (itemId == "Potion")
                {
                    Heal(10); // Восстанавливаем 10 HP
                }

                Inventory.RemoveItem(itemId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Возвращает ключ анимации на основе направления движения
        /// </summary>
        /// <param name="velocity">Вектор скорости</param>
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

        public override string ToString() =>
            $"[{Id}] на позиции {Position}, HP: {Health}/{MaxHealth}, Жив: {IsAlive}";
    }

    /// <summary>
    /// Персонаж игрока - управляется пользователем
    /// </summary>
    public class Player : Character
    {
        public Player(
           Vector2D startPosition,
           string id = "Player",
           float health = 20f,
           double speed = 4.0,
           Inventory inventory = null,
           IDialogueProvider dialogueProvider = null,
           string spritePath = "",
           double visualScale = 1.0) // Добавлен визуальный масштаб
           : base(startPosition, id, speed, health, frameSize: 48, strength: 1f,
                  inventory: inventory, dialogueProvider: dialogueProvider,
                  spritePath: spritePath, visualScale: visualScale)
        { }

        /// <summary>
        /// Прямая установка позиции игрока (для телепортации или возрождения)
        /// </summary>
        public void SetPosition(double x, double y)
        {
            Position = new Vector2D(x, y);
            Velocity = Vector2D.Zero;
        }
    }

    /// <summary>
    /// Базовый класс для неигровых персонажей с конечным автоматом
    /// </summary>
    public class NPC : Character
    {
        protected Dictionary<CharacterState, State<CharacterState, GameEvent>> States { get; set; }
        public FSM<CharacterState, GameEvent> Brain { get; protected set; }

        /// <summary>
        /// Конструктор NPC
        /// </summary>
        /// <param name="grid">Игровая сетка</param>
        /// <param name="startPosition">Начальная позиция</param>
        /// <param name="id">ID персонажа</param>
        /// <param name="speed">Скорость</param>
        /// <param name="health">Здоровье</param>
        /// <param name="strength">Сила</param>
        /// <param name="frameSize">Размер кадра спрайта (для нарезки)</param>
        /// <param name="inventory">Инвентарь</param>
        /// <param name="dialogueProvider">Провайдер диалогов</param>
        /// <param name="spritePath">Путь к спрайту</param>
        /// <param name="visualScale">Визуальный масштаб (1.0 = 100%)</param>
        public NPC(GameGrid grid,
                Vector2D startPosition,
                string id = "NPC",
                double speed = Character.DefaultSpeed,
                float health = 15f,
                float strength = 1f,
                int frameSize = 72, // <-- Теперь можно задавать размер фрейма
                Inventory inventory = null,
                IDialogueProvider dialogueProvider = null,
                string spritePath = "",
                double visualScale = 1.0) // <-- Теперь можно задавать визуальный масштаб
            : base(startPosition, id, speed, health, frameSize: frameSize, strength: strength,
                   inventory: inventory, dialogueProvider: dialogueProvider,
                   spritePath: spritePath, visualScale: visualScale)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            // Инициализируем все состояния
            States = new Dictionary<CharacterState, State<CharacterState, GameEvent>>();
            foreach (CharacterState s in Enum.GetValues(typeof(CharacterState)))
                States[s] = new State<CharacterState, GameEvent>(s);

            // Настраиваем базовые состояния
            ConfigureBaseStates();
        }

        /// <summary>
        /// Настраивает базовое поведение для всех NPC
        /// </summary>
        protected virtual void ConfigureBaseStates()
        {
            // Состояние смерти - останавливаем движение
            States[CharacterState.Dead].SetEnter(() => Stop());

            // Состояние бездействия - стоим на месте
            States[CharacterState.Idle].SetUpdate(m => Stop());
        }

        /// <summary>
        /// Настраивает поведение для конкретного состояния
        /// </summary>
        public virtual void ConfigureState(CharacterState state,
                                          Action onEnter = null,
                                          Action onExit = null,
                                          Action<FSM<CharacterState, GameEvent>> update = null)
        {
            if (state == CharacterState.Dead) return;
            if (!States.TryGetValue(state, out var s)) return;

            if (onEnter != null) s.SetEnter(onEnter);
            if (onExit != null) s.SetExit(onExit);
            if (update != null) s.SetUpdate(update);
        }

        /// <summary>
        /// Устанавливает текущее состояние NPC
        /// </summary>
        public virtual void SetState(CharacterState state)
        {
            if (States.TryGetValue(state, out var s))
                Brain?.SetState(s);
        }

        /// <summary>
        /// Инициализирует конечный автомат с указанным начальным состоянием
        /// </summary>
        protected void InitializeBrain(CharacterState initialState)
        {
            if (States.TryGetValue(initialState, out var startState))
                Brain = new FSM<CharacterState, GameEvent>(startState);
            else
                Brain = new FSM<CharacterState, GameEvent>(States[CharacterState.Idle]);
        }

        public override void Update()
        {
            base.Update();

            // Проверка смерти - если умер, переводим в состояние Dead
            if (!IsAlive && Brain?.CurrentState?.Id != CharacterState.Dead)
            {
                SetState(CharacterState.Dead);
            }

            Brain?.Update();
        }

        public override void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            base.Interact(other);
            Brain?.HandleEvent(new InteractionEvent(other));
        }
    }

    /// <summary>
    /// Враг - преследует и атакует игрока
    /// </summary>
    public class Enemy : NPC
    {
        private Character _target; // Цель для преследования
        public Character Target => _target;

        public Enemy(GameGrid grid, Vector2D position, double speed, string id = "Dragon",
                     string spritePath = "", int frameSize = 72, double visualScale = 1.0)
            : base(grid, position, id, speed, health: 100f, strength: 10f,
                   frameSize: frameSize, spritePath: spritePath, visualScale: visualScale)
        {
            ConfigureEnemyStates();
            InitializeBrain(CharacterState.Idle);

            // Настраиваем обработку события взаимодействия
            States[CharacterState.Idle].SetEventHandler((m, e) => {
                if (e is InteractionEvent ie)
                {
                    _target = ie.Other;
                    SetState(CharacterState.Chase);
                }
            });
        }

        /// <summary>
        /// Настраивает состояния для врага
        /// </summary>
        private void ConfigureEnemyStates()
        {
            // Настройка состояния бездействия
            ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                Stop();
                if (_target != null && _target.IsAlive && Vector2D.Distance(Position, _target.Position) < 8.0)
                {
                    SetState(CharacterState.Chase);
                }
            });

            // Настройка состояния преследования
            ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (_target == null || !_target.IsAlive)
                {
                    SetState(CharacterState.Idle);
                    return;
                }

                var dist = Vector2D.Distance(Position, _target.Position);
                if (dist < 1.5)
                {
                    SetState(CharacterState.Attack);
                }
                else
                {
                    var dir = _target.Position - Position;
                    Move(dir.Normalize());
                }
            });

            // Настройка состояния атаки
            ConfigureState(CharacterState.Attack, update: (machine) =>
            {
                if (_target == null || !_target.IsAlive)
                {
                    SetState(CharacterState.Idle);
                    return;
                }

                Stop();
                var dist = Vector2D.Distance(Position, _target.Position);
                if (dist > 2.0)
                {
                    SetState(CharacterState.Chase);
                    return;
                }

                if (new Random().NextDouble() < 0.02)
                {
                    Attack(_target);
                }
            });
        }

        /// <summary>
        /// Устанавливает цель для преследования
        /// </summary>
        public void SetTarget(Character target) { _target = target; }
    }

    /// <summary>
    /// Союзник - персонаж с настраиваемым конечным автоматом и вероятностными переходами между состояниями
    /// </summary>
    public class Ally : NPC
    {
        private static readonly Random _random = new Random();

        public List<Vector2D> PatrolPoints { get; set; } = new List<Vector2D>(); // Точки патрулирования
        public Vector2D CurrentTarget { get; set; } // Текущая цель

        // Приватные поля
        private readonly State<CharacterState, GameEvent> _decisionState;
        private readonly Dictionary<CharacterState, List<Transition>> _transitions = new Dictionary<CharacterState, List<Transition>>();


        /// <summary>
        /// Вспомогательный класс для хранения информации о переходах между состояниями
        /// </summary>
        private class Transition
        {
            public CharacterState Target { get; set; }      // Целевое состояние
            public double Probability { get; set; }         // Вероятность перехода

            public Transition(CharacterState target, double probability)
            {
                Target = target;
                Probability = probability;
            }
        }

        // Публичные свойства
        public CharacterState CurrentState
        {
            get
            {
                if (Brain == null || Brain.CurrentState == null)
                    return CharacterState.Dead;
                return Brain.CurrentState.Id;
            }
        }

        /// Конструктор персонажа-союзника
        public Ally(GameGrid grid, Vector2D startPosition, string id = "Ally",
                    double speed = Character.DefaultSpeed, float health = 15f,
                    float strength = 10f,
                    int frameSize = 72,
                    double visualScale = 1.0,
                    Inventory inventory = null,
                    IDialogueProvider dialogueProvider = null,
                    string spritePath = "")
            : base(grid, startPosition, id, speed, health, strength,
                   frameSize, inventory, dialogueProvider, spritePath, visualScale)
        {
            // Настраиваем состояние принятия решений
            _decisionState = States[CharacterState.Decision];
            _decisionState.SetUpdate(DecisionUpdate);

            // Инициализируем конечный автомат с состоянием принятия решений
            Brain = new FSM<CharacterState, GameEvent>(_decisionState)
            {
                LastState = CharacterState.Idle
            };
        }

        /// <summary>
        /// Настраивает поведение для конкретного состояния (переопределен для учета Decision и Dead)
        /// </summary>
        public override void ConfigureState(CharacterState state,
                                           Action onEnter = null,
                                           Action onExit = null,
                                           Action<FSM<CharacterState, GameEvent>> update = null)
        {
            if (state == CharacterState.Dead || state == CharacterState.Decision) return;
            base.ConfigureState(state, onEnter, onExit, update);
        }

        /// <summary>
        /// Устанавливает текущее состояние союзника
        /// </summary>
        public override void SetState(CharacterState state)
        {
            if (States.TryGetValue(state, out var s))
                Brain.SetState(s);
        }

        /// <summary>
        /// Добавляет вероятностный переход между состояниями
        /// </summary>
        public void AddTransition(CharacterState from, CharacterState to, double probability)
        {
            if (from == CharacterState.Dead || from == CharacterState.Decision) return;

            if (!_transitions.TryGetValue(from, out var list))
            {
                list = new List<Transition>();
                _transitions[from] = list;
            }

            // Удаляем существующий переход к той же цели
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].Target == to)
                    list.RemoveAt(i);

            list.Add(new Transition(to, probability));
        }

        /// <summary>
        /// Логика состояния принятия решений - выбирает следующее состояние на основе вероятностей
        /// </summary>
        private void DecisionUpdate(FSM<CharacterState, GameEvent> machine)
        {
            // Проверяем доступные переходы из последнего состояния
            if (!_transitions.TryGetValue(Brain.LastState, out var options) || options.Count == 0)
            {
                SetState(CharacterState.Idle);
                return;
            }

            // Вычисляем общую сумму вероятностей
            double total = 0;
            foreach (var o in options)
                total += o.Probability;

            if (total <= 0)
            {
                SetState(CharacterState.Idle);
                return;
            }

            // Выбираем случайный переход на основе вероятностей
            var roll = _random.NextDouble() * total;
            var cumulative = 0.0;
            foreach (var transition in options)
            {
                cumulative += transition.Probability;
                if (roll <= cumulative)
                {
                    SetState(transition.Target);
                    return;
                }
            }

            SetState(options[options.Count - 1].Target);
        }

        public override void Update()
        {
            base.Update();
            Brain?.Update();
        }

        public override void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            base.Interact(other);
            Brain?.HandleEvent(new InteractionEvent(other));
        }

        /// <summary>
        /// Вызывается при появлении предмета в мире
        /// </summary>
        public void OnItemAppeared(string itemId, Vector2D position)
        {
            CurrentTarget = position;
        }
    }
}