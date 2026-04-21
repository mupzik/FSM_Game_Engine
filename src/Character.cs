using System;
using System.Collections.Generic;
using System.Linq;
using GameProj.src;

namespace GameProj
{
    public abstract class GameEvent { }
    public class InteractionEvent : GameEvent
    {
        public Character Other { get; private set; }
        public InteractionEvent(Character other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            Other = other;
        }
    }

    public enum CharacterState
    {
        Idle, Patrol, Chase, Flee, Dead, Attack, GoToItem, Decision
    }

    public interface IDialogueProvider
    {
        string GetDialogueFor(Character other);
    }

    public class Character
    {
        protected const float DefaultInteractionRadius = 2.0f;
        protected const double DefaultCharacterSize = 24.0;
        protected const double DefaultSpeed = 1.0;

        public Vector2D Position { get; set; }
        public Vector2D Velocity { get; set; }
        protected double Speed { get; set; }

        public float Health { get; private set; }
        public float MaxHealth { get; private set; } // Добавлено
        protected float Strength { get; set; }
        public bool IsAlive => Health > 0;

        public Inventory Inventory { get; protected set; }
        public IDialogueProvider DialogueProvider { get; protected set; }
        public string Id { get; protected set; }
        public string SpritePath { get; protected set; }


        // Новый параметр: размер одного квадратного кадра
        public int FrameSize { get; set; } = 48;
        public virtual double Size => DefaultCharacterSize;
        protected virtual double InteractionRadius => DefaultInteractionRadius;

        public event Action<Character, string> OnItemPickedUp;

        public event Action<Character, float> OnHealthChanged;


      
        public Character(
            Vector2D startPosition,
            string id,
            double speed = DefaultSpeed,
            float health = 20f,
            int frameSize = 48,
            float strength = 1f,
            Inventory inventory = null,
            IDialogueProvider dialogueProvider = null,
            string spritePath = "")
        {
            Position = startPosition;
            Velocity = Vector2D.Zero;
            Speed = Math.Max(0, speed);
            MaxHealth = health; // Инициализация MaxHealth
            FrameSize = frameSize;
            Health = health;
            Strength = strength;

            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            Id = id;

            Inventory = inventory ?? new Inventory();
            DialogueProvider = dialogueProvider;
            SpritePath = spritePath;
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
            {
                OnHealthChanged?.Invoke(this, actualHeal); // ✅ Вызов события
            }
        }

        public virtual void TakeDamage(float damage)
        {
            if (!IsAlive || damage <= 0) return;

            float oldHealth = Health;
            Health -= damage;

            if (Health <= 0)
            {
                Health = 0;
                Die();
            }

            float actualDamage = Health - oldHealth; // Будет отрицательным
            OnHealthChanged?.Invoke(this, actualDamage); // ✅ Вызов события
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
            if (!string.IsNullOrEmpty(text))
            {
                Console.WriteLine($"{Id} говорит: \"{text}\"");
            }
        }

        public void PickupItem(string itemId)
        {
            OnItemPickedUp?.Invoke(this, itemId);
        }

        // Метод использования предмета
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
            $"[{Id}] at {Position}, HP: {Health}/{MaxHealth}, Alive: {IsAlive}";
    }

    public class Player : Character
    {
        public Player(
           Vector2D startPosition,
           string id = "Player",
           float health = 20f,
           double speed = 4.0, 
           Inventory inventory = null,
           IDialogueProvider dialogueProvider = null,
           string spritePath = "")
           : base(startPosition, id, speed, health, frameSize: 48, 1f, inventory, dialogueProvider, spritePath) { }


        public void SetPosition(double x, double y)
        {
            Position = new Vector2D(x, y);
            Velocity = Vector2D.Zero;
        }
    }

    public class NPC : Character
    {
        public FSM<CharacterState, GameEvent> Brain { get; set; }
        public NPC(GameGrid grid,
                Vector2D startPosition,
                string id = "NPC",
                double speed = Character.DefaultSpeed,
                float health = 15f,
                float strength = 1f, // <-- Добавьте этот параметр
                FSM<CharacterState, GameEvent> brain = null,
                Inventory inventory = null,
                IDialogueProvider dialogueProvider = null,
                string spritePath = "")
    : base(startPosition, id, speed, health, frameSize: 72, strength, inventory, dialogueProvider, spritePath)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            Brain = brain;
        }
        public override void Update() { base.Update(); Brain?.Update(); }
        public override void Interact(Character other) { if (!CanInteractWith(other)) return; base.Interact(other); Brain?.HandleEvent(new InteractionEvent(other)); }
    }


    public class Enemy : NPC
    {
        private FSM<CharacterState, GameEvent> _brain;
        private Character _target;

        public Enemy(GameGrid grid, Vector2D position, string id = "Dragon", string spritePath = "")
    : base(grid, position, id, speed: 0.05, health: 100f, strength: 10f, spritePath: spritePath)
        {
            SetupBrain();
            Brain = _brain;
        }

        private void SetupBrain()
        {
            var idle = new State<CharacterState, GameEvent>(CharacterState.Idle);
            var chase = new State<CharacterState, GameEvent>(CharacterState.Chase);
            var attack = new State<CharacterState, GameEvent>(CharacterState.Attack);
            var dead = new State<CharacterState, GameEvent>(CharacterState.Dead);

            idle.SetUpdate(machine =>
            {
                if (!IsAlive) { machine.SetState(dead); return; }
                Stop();
                if (_target != null && _target.IsAlive && Vector2D.Distance(Position, _target.Position) < 8.0)
                {
                    machine.SetState(chase);
                }
            });

            chase.SetUpdate(machine =>
            {
                if (!IsAlive) { machine.SetState(dead); return; }
                if (_target == null || !_target.IsAlive) { machine.SetState(idle); return; }

                var dist = Vector2D.Distance(Position, _target.Position);
                if (dist < 1.5) machine.SetState(attack);
                else
                {
                    var dir = _target.Position - Position;
                    Move(dir.Normalize());
                }
            });

            attack.SetUpdate(machine =>
            {
                if (!IsAlive) { machine.SetState(dead); return; }
                if (_target == null || !_target.IsAlive) { machine.SetState(idle); return; }

                Stop();
                var dist = Vector2D.Distance(Position, _target.Position);
                if (dist > 2.0)
                {
                    machine.SetState(chase);
                    return;
                }

                // Атака с шансом ~2% каждый кадр
                if (new Random().NextDouble() < 0.02)
                {
                    Attack(_target);
                }
            });

            dead.SetEnter(() => Stop());

            _brain = new FSM<CharacterState, GameEvent>(idle);

            idle.SetEventHandler((m, e) => {
                if (e is InteractionEvent ie)
                {
                    _target = ie.Other;
                    m.SetState(chase);
                }
            });
        }

        public void SetTarget(Character target) { _target = target; }

    }

    public class Ally : NPC
    {
        private static readonly Random _random = new Random();
        private class Transition { public CharacterState Target { get; set; } public double Probability { get; set; } public Transition(CharacterState target, double probability) { Target = target; Probability = probability; } }

        public CharacterState CurrentState { get { if (_brain == null || _brain.CurrentState == null) return CharacterState.Dead; return _brain.CurrentState.Id; } }
        public List<Vector2D> PatrolPoints { get; set; } = new List<Vector2D>();
        public Vector2D CurrentTarget { get; set; }

        private readonly Dictionary<CharacterState, State<CharacterState, GameEvent>> _states;
        private readonly State<CharacterState, GameEvent> _decisionState;
        private readonly FSM<CharacterState, GameEvent> _brain;
        private readonly Dictionary<CharacterState, List<Transition>> _transitions = new Dictionary<CharacterState, List<Transition>>();

        // Добавляем параметр frameSize со значением по умолчанию 48 (так как Orc обычно меньше Босса)
        public Ally(GameGrid grid, Vector2D startPosition, string id = "Ally", double speed = Character.DefaultSpeed, float health = 15f, Inventory inventory = null, IDialogueProvider dialogueProvider = null, string spritePath = "", int frameSize = 48)
            : base(grid, startPosition, id, speed, health, strength: 10f, brain: null, inventory, dialogueProvider, spritePath)
        {
            // Переопределяем размер кадра после вызова базового конструктора
            this.FrameSize = frameSize;

            _states = new Dictionary<CharacterState, State<CharacterState, GameEvent>>();
            foreach (CharacterState s in Enum.GetValues(typeof(CharacterState))) _states[s] = new State<CharacterState, GameEvent>(s);
            _decisionState = _states[CharacterState.Decision];
            _decisionState.SetUpdate(DecisionUpdate);
            _states[CharacterState.Dead].SetEnter(() => Stop());
            _brain = new FSM<CharacterState, GameEvent>(_decisionState);
            _brain.LastState = CharacterState.Idle;
        }

        public void SetState(CharacterState state) { if (_states.TryGetValue(state, out var s)) _brain.SetState(s); }

        public void ConfigureState(CharacterState state, Action onEnter = null, Action onExit = null, Action<FSM<CharacterState, GameEvent>> update = null)
        {
            if (state == CharacterState.Dead || state == CharacterState.Decision) return;
            if (!_states.TryGetValue(state, out var s)) return;
            if (onEnter != null) s.SetEnter(onEnter);
            if (onExit != null) s.SetExit(onExit);
            if (update != null) s.SetUpdate(update);
        }

        public void AddTransition(CharacterState from, CharacterState to, double probability)
        {
            if (from == CharacterState.Dead || from == CharacterState.Decision) return;
            if (!_transitions.TryGetValue(from, out var list)) { list = new List<Transition>(); _transitions[from] = list; }
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].Target == to) list.RemoveAt(i);
            list.Add(new Transition(to, probability));
        }

        private void DecisionUpdate(FSM<CharacterState, GameEvent> machine)
        {
            if (!_transitions.TryGetValue(_brain.LastState, out var options) || options.Count == 0) { SetState(CharacterState.Idle); return; }
            double total = 0; foreach (var o in options) total += o.Probability;
            if (total <= 0) { SetState(CharacterState.Idle); return; }
            var roll = _random.NextDouble() * total;
            var cumulative = 0.0;
            foreach (var transition in options)
            {
                cumulative += transition.Probability;
                if (roll <= cumulative) { SetState(transition.Target); return; }
            }
            SetState(options[options.Count - 1].Target);
        }

        public override void Update() { base.Update(); _brain?.Update(); }
        public override void Interact(Character other) { if (!CanInteractWith(other)) return; base.Interact(other); _brain?.HandleEvent(new InteractionEvent(other)); }
        public void OnItemAppeared(string itemId, Vector2D position) { CurrentTarget = position; }
        public void SetVelocity(Vector2D direction)
        {
            if (!IsAlive) return;
            double lenSq = direction.X * direction.X + direction.Y * direction.Y;
            Velocity = lenSq < 0.001f ? Vector2D.Zero : direction.Normalize() * Speed;
        }
    }
}