using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Animal
{
    public sealed class DogEntity : global::MiniCivilization.World.Entities.AnimalEntity
    {
        private const float DecisionIntervalSeconds = 1f;
        private const float MoveDurationSeconds = 1f;

        private enum State : byte
        {
            Idle,
            Move
        }

        private static readonly WeightedState<State>[] StateWeights =
        {
            new(State.Idle, 2),
            new(State.Move, 8)
        };
        private static readonly AnimalMovementRules MovementRules =
            AnimalMovementRules.Cardinal4(1);

        private State currentState = State.Idle;
        private float decisionElapsed;

        public DogEntity(EntityData data) : base(data)
        {
        }

        public override int RenderStateKey => (int)currentState;

        protected override void UpdateState(
            EntityRuntime runtime,
            float deltaTime)
        {
            if (IsMoving)
            {
                if (AdvanceMove(
                        runtime,
                        deltaTime / MoveDurationSeconds))
                {
                    currentState = State.Idle;
                    decisionElapsed = 0f;
                }

                return;
            }

            decisionElapsed += deltaTime;
            if (decisionElapsed < DecisionIntervalSeconds)
            {
                return;
            }

            decisionElapsed %= DecisionIntervalSeconds;
            var selectedState = SelectWeightedState(StateWeights);
            if (selectedState != State.Move
                || !TrySelectMoveDestination(runtime, out var destination))
            {
                currentState = State.Idle;
                return;
            }

            currentState = State.Move;
            if (!TryBeginMove(runtime, destination))
            {
                currentState = State.Idle;
            }
        }

        protected override AnimalMovementRules ResolveMovementRules() =>
            MovementRules;
    }
}
