using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Animal
{
    public sealed class DogEntity : global::MiniCivilization.World.Entities.AnimalEntity
    {
        private const float DecisionIntervalSeconds = 1f;
        private const float MoveDurationSeconds = 1f;

        private enum BehaviorState : byte
        {
            Idle,
            Move
        }

        private enum RenderState : byte
        {
            IdleStill,
            IdleCircle,
            IdleWander,
            Move
        }

        private static readonly WeightedState<BehaviorState>[] StateWeights =
        {
            new(BehaviorState.Idle, 9),
            new(BehaviorState.Move, 1)
        };
        private static readonly WeightedState<RenderState>[] IdleStateWeights =
        {
            new(RenderState.IdleStill, 1),
            new(RenderState.IdleCircle, 1),
            new(RenderState.IdleWander, 1)
        };
        private static readonly AnimalMovementRules MovementRules =
            AnimalMovementRules.Cardinal4(1);

        private BehaviorState currentState = BehaviorState.Idle;
        private RenderState currentRenderState = RenderState.IdleStill;
        private float decisionElapsed;

        public DogEntity(EntityData data) : base(data)
        {
        }

        public override int RenderStateKey => (int)currentRenderState;

        protected override void UpdateState(
            EntityRuntime runtime,
            float deltaTime)
        {
            if (currentState == BehaviorState.Move)
            {
                if (!IsMoving)
                {
                    EnterIdle();
                    return;
                }

                if (AdvanceMove(
                        runtime,
                        deltaTime / MoveDurationSeconds))
                {
                    EnterIdle();
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
            if (selectedState != BehaviorState.Move)
            {
                EnterIdle();
                return;
            }

            if (!TrySelectMoveDestination(runtime, out var destination))
            {
                EnterIdle();
                return;
            }

            currentState = BehaviorState.Move;
            currentRenderState = RenderState.Move;
            if (!TryBeginMove(runtime, destination))
            {
                EnterIdle();
            }
        }

        protected override AnimalMovementRules ResolveMovementRules() =>
            MovementRules;

        private void EnterIdle()
        {
            currentState = BehaviorState.Idle;
            currentRenderState = SelectWeightedState(IdleStateWeights);
        }
    }
}
