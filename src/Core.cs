using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GameProj.GameManager;
using static System.Windows.Forms.AxHost;

namespace GameProj.src
{
    /// <summary>
    /// Двумерный вектор для представления позиций, скоростей и направлений.
    /// </summary>
    public class Vector2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Vector2D(double x = 0, double y = 0)
        {
            X = x;
            Y = y;
        }

        public static Vector2D operator -(Vector2D a, Vector2D b) => new Vector2D(a.X - b.X, a.Y - b.Y);
        public static Vector2D operator +(Vector2D a, Vector2D b) => new Vector2D(a.X + b.X, a.Y + b.Y);
        public static Vector2D operator *(Vector2D v, double k) => new Vector2D(v.X * k, v.Y * k);

        public static Vector2D operator /(Vector2D v, double k)
        {
            if (k == 0) throw new DivideByZeroException();
            return new Vector2D(v.X / k, v.Y / k);
        }

        public double Length() => Math.Sqrt(X * X + Y * Y);

        public Vector2D Normalize()
        {
            double len = Length();
            return len == 0 ? new Vector2D(0, 0) : this / len;
        }

        public static double DistanceSquared(Vector2D a, Vector2D b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static double Distance(Vector2D a, Vector2D b) => Math.Sqrt(DistanceSquared(a, b));

        public static Vector2D Zero => new Vector2D(0, 0);

        public override string ToString() => $"({X:F2}, {Y:F2})";

        public override bool Equals(object obj) => obj is Vector2D v && Math.Abs(X - v.X) < 1e-6 && Math.Abs(Y - v.Y) < 1e-6;

        public override int GetHashCode() => (X.GetHashCode() * 17) ^ Y.GetHashCode();
    }

    /// <summary>
    /// Конечный автомат (Finite State Machine).
    /// </summary>
    public class FSM<TState, TEvent> 
    {
        public State<TState, TEvent> CurrentState { get; private set; }
        public TState LastState { get; set; }

        public FSM(State<TState, TEvent> initialState)
        {
            CurrentState = initialState;
            CurrentState?.Enter();
        }

        public void SetState(State<TState, TEvent> newState)
        {

            LastState = CurrentState.Id;

            CurrentState?.Exit();
            CurrentState = newState;
            CurrentState?.Enter();
        }

        public void HandleEvent(TEvent _event)
        {
            CurrentState?.HandleEvent(this, _event);
        }

        public void Update()
        {
            CurrentState?.Update(this);
        }
    }

    /// <summary>
    /// Состояние конечного автомата.
    /// </summary>
    public class State<TState, TEvent>
    {
        public TState Id { get; }

        protected Action onEnter;
        protected Action onExit;
        private Action<FSM<TState, TEvent>, TEvent> eventHandler;
        private Action<FSM<TState, TEvent>> updateHandler;

        public State(TState id) => Id = id;

        public virtual void SetEnter(Action act) => onEnter = act;
        public virtual void SetExit(Action act) => onExit = act;
        public virtual void SetEventHandler(Action<FSM<TState, TEvent>, TEvent> handler) => eventHandler = handler;
        public virtual void SetUpdate(Action<FSM<TState, TEvent>> handler) => updateHandler = handler;

        public virtual void Enter() => onEnter?.Invoke();
        public virtual void Exit() => onExit?.Invoke();
        public virtual void HandleEvent(FSM<TState, TEvent> machine, TEvent _event) => eventHandler?.Invoke(machine, _event);
        public virtual void Update(FSM<TState, TEvent> machine) => updateHandler?.Invoke(machine);
    }



    /// <summary>
    /// Составное состояние для иерархического конечного автомата.
    /// </summary>
    public class CompositeState<TState, TEvent> : State<TState, TEvent>
    {
        private readonly List<State<TState, TEvent>> subStates = new List<State<TState, TEvent>>();
        private State<TState, TEvent> currentSubState;
        private TState historyState; // сохраняем ID последнего активного подсостояния
        private readonly TState initialState; // начальное подсостояние при первом входе

        // Конструктор: принимает ID состояния и ID начального подсостояния
        public CompositeState(TState id, TState initialState) : base(id)
        {
            this.initialState = initialState;
        }

        public void AddSubState(State<TState, TEvent> state)
        {
            subStates.Add(state);
        }

        public void SetHistoryState(TState state)
        {
            historyState = state;
        }

        /// <summary>
        /// Возвращает тип текущего активного подсостояния.
        /// Если подсостояния нет, возвращает default(TState).
        /// </summary>
        public TState GetCurrentSubStateId()
        {
            if (currentSubState == null)
            {
                return default(TState);
            }
            return currentSubState.Id;
        }

        public State<TState, TEvent> GetCurrentSubState() => currentSubState;

        public void SwitchToSubState(TState targetId)
        {
            var targetState = subStates.FirstOrDefault(s => s.Id.Equals(targetId));
            if (targetState == null)
                throw new ArgumentException($"SubState with ID {targetId} not found");

            currentSubState?.Exit();
            currentSubState = targetState;
            currentSubState?.Enter();
        }

        public override void Enter()
        {
            base.Enter();

            // Определяем, в какое подсостояние переходить
            TState targetId;
            if (historyState != null && !historyState.Equals(initialState))
            {
                // Если есть история — восстанавливаем последнее активное подсостояние
                targetId = historyState;
            }
            else
            {
                // Иначе — начальное состояние
                targetId = initialState;
            }

            // Находим подсостояние по ID
            currentSubState = subStates.FirstOrDefault(s => s.Id.Equals(targetId));

            if (currentSubState == null && subStates.Count > 0)
            {
                // Если подсостояние не найдено, берём первое
                currentSubState = subStates.First();
            }

            currentSubState?.Enter();
        }

        public override void Exit()
        {
            // Сохраняем текущее подсостояние в историю
            if (currentSubState != null)
            {
                historyState = currentSubState.Id;
            }

            currentSubState?.Exit();
            base.Exit();
        }

        public override void HandleEvent(FSM<TState, TEvent> machine, TEvent _event)
        {
            // Сначала пробуем обработать событие в текущем подсостоянии
            if (currentSubState != null)
            {
                currentSubState.HandleEvent(machine, _event);
            }
        }

        public override void Update(FSM<TState, TEvent> machine)
        {
            // Обновляем текущее подсостояние
            currentSubState?.Update(machine);
        }
    }
}