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
        GoToPoint,  // Движение к точке интереса
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
    /// Информация о спрайте персонажа
    /// </summary>
    public struct SpriteInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string BaseName { get; set; }

        public SpriteInfo(string baseName, int width, int height)
        {
            BaseName = baseName;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Базовый класс для всех персонажей в игре (Игрок, NPC, Враг, Союзник)
    /// </summary>
    public class Character
    {
        // Константы
        protected const float DefaultInteractionRadius = 2.0f;
        protected const double DefaultCharacterSize = 24.0;
        protected const double DefaultSpeed = 1.0;

        // Свойства движения
        public Vector2D Position { get; set; }
        public Vector2D Velocity { get; set; }
        protected double Speed { get; set; }

        // Свойства здоровья
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        protected float _strength;
        public float Strength
        {
            get => _strength;
            set => _strength = value;
        }
        public bool IsAlive => Health > 0;

        // Инвентарь и диалоги
        public Inventory Inventory { get; protected set; }
        public IDialogueProvider DialogueProvider { get; protected set; }

        // Идентификация и визуальное отображение
        public string Id { get; protected set; }
        public string SpritePath { get; protected set; }
        public SpriteInfo SpriteData { get; set; }
        public double VisualScale { get; set; } = 1.0;
        public bool IsFacingRight { get; set; } = true;

        // Виртуальные свойства
        public virtual double Size => DefaultCharacterSize;
        protected virtual double InteractionRadius => DefaultInteractionRadius;

        // События
        public event Action<Character, string> OnItemPickedUp;
        public event Action<Character, float> OnHealthChanged;

        public Character(
            Vector2D startPosition,
            string id,
            double speed = DefaultSpeed,
            float health = 20f,
            float strength = 1f,
            Inventory inventory = null,
            IDialogueProvider dialogueProvider = null,
            string spritePath = "",
            double visualScale = 1.0,
            SpriteInfo? spriteInfo = null)
        {
            Position = startPosition;
            Velocity = Vector2D.Zero;
            Speed = Math.Max(0, speed);
            MaxHealth = health;
            Health = health;
            Strength = strength;
            VisualScale = visualScale;
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Inventory = inventory ?? new Inventory();
            DialogueProvider = dialogueProvider;
            SpritePath = spritePath;
            SpriteData = spriteInfo ?? new SpriteInfo(id, 48, 48);
        }

        public virtual void Update()
        {
            if (!IsAlive) return;
            Position += Velocity;
        }

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

                // Обновляем направление взгляда для анимации
                if (Math.Abs(Velocity.X) > 0.01)
                    IsFacingRight = Velocity.X > 0;
            }
        }

        public void Stop() => Velocity = Vector2D.Zero;

        public virtual void Die()
        {
            if (!IsAlive) return;
            Health = 0;
            Stop();
            OnDeath();
        }

        public virtual void Heal(float amount)
        {
            if (amount <= 0 || !IsAlive) return;
            float oldHealth = Health;
            Health = Math.Min(Health + amount, MaxHealth);
            float actualHeal = Health - oldHealth;
            if (actualHeal > 0)
                OnHealthChanged?.Invoke(this, actualHeal);
        }

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
            OnHealthChanged?.Invoke(this, -actualDamage);
        }

        public virtual void Attack(Character enemy)
        {
            if (!IsAlive || enemy == null || !enemy.IsAlive) return;
            enemy.TakeDamage(Strength);
        }

        protected virtual void OnDeath() { }

        public virtual bool CanInteractWith(Character other, double? maxDistance = null)
        {
            if (other == null || !IsAlive || !other.IsAlive) return false;
            var radius = maxDistance ?? InteractionRadius;
            return Vector2D.Distance(Position, other.Position) <= radius;
        }

        public virtual void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            var text = DialogueProvider?.GetDialogueFor(other);
        }

        public void PickupItem(string itemId)
        {
            OnItemPickedUp?.Invoke(this, itemId);
        }

        public bool UseItem(string itemId)
        {
            if (Inventory.HasItem(itemId))
            {
                if (itemId == "Potion")
                    Heal(10);
                Inventory.RemoveItem(itemId);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Возвращает ключ анимации на основе направления движения и состояния
        /// </summary>
        public virtual string GetAnimationKey(Vector2D velocity, CharacterState state = CharacterState.Idle)
        {
            if (state == CharacterState.Attack)
                return "_Attack";

            if (state == CharacterState.Dead)
                return "_Dead";

            if (velocity.Length() < 0.01)
                return "_Idle";

            double dx = Math.Abs(velocity.X);
            double dy = Math.Abs(velocity.Y);

            if (dy >= dx)
                return velocity.Y > 0 ? "_D_Walk" : "_U_Walk";
            else
                return velocity.X > 0 ? "_R_Walk" : "_L_Walk";
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
            double visualScale = 1.0,
            SpriteInfo? spriteInfo = null)
            : base(startPosition, id, speed, health, strength: 1f,
                   inventory: inventory, dialogueProvider: dialogueProvider,
                   spritePath: spritePath, visualScale: visualScale, spriteInfo: spriteInfo)
        { }

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
        public CharacterState CurrentState => Brain?.CurrentState?.Id ?? CharacterState.Dead;

        public NPC(GameGrid grid,
                Vector2D startPosition,
                string id = "NPC",
                double speed = Character.DefaultSpeed,
                float health = 15f,
                float strength = 1f,
                Inventory inventory = null,
                IDialogueProvider dialogueProvider = null,
                string spritePath = "",
                double visualScale = 1.0,
                SpriteInfo? spriteInfo = null)
            : base(startPosition, id, speed, health, strength,
                   inventory, dialogueProvider, spritePath, visualScale, spriteInfo)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            States = new Dictionary<CharacterState, State<CharacterState, GameEvent>>();
            foreach (CharacterState s in Enum.GetValues(typeof(CharacterState)))
                States[s] = new State<CharacterState, GameEvent>(s);

            ConfigureBaseStates();
        }

        protected virtual void ConfigureBaseStates()
        {
            States[CharacterState.Dead].SetEnter(() => Stop());
            States[CharacterState.Idle].SetUpdate(m => Stop());
        }

        public virtual void ConfigureState(
        CharacterState state,
        Action onEnter = null,
        Action onExit = null,
        Action<FSM<CharacterState, GameEvent>> update = null,
        Action<FSM<CharacterState, GameEvent>, GameEvent> eventHandler = null) 
        {
            if (state == CharacterState.Dead) return;
            if (!States.TryGetValue(state, out var s)) return;

            if (onEnter != null) s.SetEnter(onEnter);
            if (onExit != null) s.SetExit(onExit);
            if (update != null) s.SetUpdate(update);
            if (eventHandler != null) s.SetEventHandler(eventHandler); 
        }

        public virtual void SetState(CharacterState state)
        {
            if (States.TryGetValue(state, out var s))
                Brain?.SetState(s);
        }

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
            if (!IsAlive && Brain?.CurrentState?.Id != CharacterState.Dead)
                SetState(CharacterState.Dead);
            Brain?.Update();
        }

        public override void Interact(Character other)
        {
            if (!CanInteractWith(other)) return;
            base.Interact(other);
            Brain?.HandleEvent(new InteractionEvent(other));
        }

        /// <summary>
        /// Получить ключ анимации с учетом текущего состояния
        /// </summary>
        public virtual string GetAnimationKeyWithState()
        {
            return GetAnimationKey(Velocity, CurrentState);
        }
    }

    /// <summary>
    /// Враг - преследует и атакует игрока
    /// </summary>
    public class Enemy : NPC
    {
        private Character _target;
        public Character Target => _target;

        // Атрибут типа врага (строка)
        private string _type;
        public string Type
        {
            get => _type;
            set => _type = string.IsNullOrWhiteSpace(value) ? "Normal" : value;
        }

        public Enemy(GameGrid grid, Vector2D position, double speed,
             string id = "Dragon",
             float health = 100f,
             float strength = 10f,
             string spritePath = "",
             double visualScale = 1.0,
             SpriteInfo? spriteInfo = null,
             string type = "Normal")
        : base(grid, position, id, speed, health: health, strength: strength,
               spritePath: spritePath, visualScale: visualScale, spriteInfo: spriteInfo)
        {
            _type = type;
            ConfigureEnemyStates();
            InitializeBrain(CharacterState.Idle);

            States[CharacterState.Idle].SetEventHandler((m, e) => {
                if (e is InteractionEvent ie)
                {
                    _target = ie.Other;
                    SetState(CharacterState.Chase);
                }
            });
        }

        private void ConfigureEnemyStates()
        {
            ConfigureState(CharacterState.Idle, update: (machine) =>
            {
                Stop();
                if (_target != null && _target.IsAlive && Vector2D.Distance(Position, _target.Position) < 8.0)
                    SetState(CharacterState.Chase);
            });

            ConfigureState(CharacterState.Chase, update: (machine) =>
            {
                if (_target == null || !_target.IsAlive)
                {
                    SetState(CharacterState.Idle);
                    return;
                }
                var dist = Vector2D.Distance(Position, _target.Position);
                if (dist < 1.5)
                    SetState(CharacterState.Attack);
                else
                    Move((_target.Position - Position).Normalize());
            });

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
                    Attack(_target);
            });
        }

        public void SetTarget(Character target) => _target = target;
    }

    /// <summary>
    /// Союзник - персонаж с настраиваемым конечным автоматом
    /// </summary>
    public class NSM_NPC : NPC
    {
        private static readonly Random _random = new Random();
        public List<Vector2D> PatrolPoints { get; set; } = new List<Vector2D>();
        public Vector2D CurrentTarget { get; set; }

        private readonly State<CharacterState, GameEvent> _decisionState;
        private readonly Dictionary<CharacterState, List<Transition>> _transitions = new Dictionary<CharacterState, List<Transition>>();

        private class Transition
        {
            public CharacterState Target { get; set; }
            public double Probability { get; set; }
            public Transition(CharacterState target, double probability)
            {
                Target = target;
                Probability = probability;
            }
        }

        public NSM_NPC(GameGrid grid, Vector2D startPosition, string id = "NSM_NPC",
                    double speed = Character.DefaultSpeed, float health = 15f,
                    float strength = 10f, double visualScale = 1.0,
                    Inventory inventory = null, IDialogueProvider dialogueProvider = null,
                    string spritePath = "", SpriteInfo? spriteInfo = null)
            : base(grid, startPosition, id, speed, health, strength,
                   inventory, dialogueProvider, spritePath, visualScale, spriteInfo)
        {
            _decisionState = States[CharacterState.Decision];
            _decisionState.SetUpdate(DecisionUpdate);
            Brain = new FSM<CharacterState, GameEvent>(_decisionState) { LastState = CharacterState.Idle };
        }

        public override void ConfigureState(CharacterState state,
                                     Action onEnter = null,
                                     Action onExit = null,
                                     Action<FSM<CharacterState, GameEvent>> update = null,
                                     Action<FSM<CharacterState, GameEvent>, GameEvent> eventHandler = null)
        {
            if (state == CharacterState.Dead || state == CharacterState.Decision) return;
            base.ConfigureState(state, onEnter, onExit, update, eventHandler);
        }

        public override void SetState(CharacterState state)
        {
            if (States.TryGetValue(state, out var s))
                Brain.SetState(s);
        }

        public void AddTransition(CharacterState from, CharacterState to, double probability)
        {
            if (from == CharacterState.Dead || from == CharacterState.Decision) return;
            if (!_transitions.TryGetValue(from, out var list))
            {
                list = new List<Transition>();
                _transitions[from] = list;
            }
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].Target == to)
                    list.RemoveAt(i);
            list.Add(new Transition(to, probability));
        }

        private void DecisionUpdate(FSM<CharacterState, GameEvent> machine)
        {
            if (!_transitions.TryGetValue(Brain.LastState, out var options) || options.Count == 0)
            {
                SetState(CharacterState.Idle);
                return;
            }
            double total = 0;
            foreach (var o in options)
                total += o.Probability;
            if (total <= 0)
            {
                SetState(CharacterState.Idle);
                return;
            }
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

        public void OnItemAppeared(string itemId, Vector2D position)
        {
            CurrentTarget = position;
        }
    }
}