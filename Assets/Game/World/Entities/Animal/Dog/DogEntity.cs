using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities.Animal
{
    public sealed class DogEntity : global::MiniCivilization.World.Entities.AnimalEntity
    {
        private enum BehaviorState : byte
        {
            Idle,
            Move
        }

        private static readonly EntityActivityId IdleActivity = new("Idle");
        private static readonly EntityActivityId MoveActivity = new("Move");

        private readonly AnimalDecisionRules decisionRules;
        private BehaviorState currentState = BehaviorState.Idle;
        private float decisionElapsed;

        public DogEntity(
            EntityData data,
            EntityCellMovementRules movementRules,
            AnimalDecisionRules decisionRules) : base(data, movementRules)
        {
            this.decisionRules = decisionRules
                ?? throw new System.ArgumentNullException(
                    nameof(decisionRules));
        }

        public override EntityActivityId Activity => currentState switch
        {
            BehaviorState.Idle => IdleActivity,
            BehaviorState.Move => MoveActivity,
            _ => throw new System.ArgumentOutOfRangeException()
        };

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

                var moveDuration = MovementRules.GetDuration(MoveType);
                if (AdvanceMove(
                        runtime,
                        deltaTime / moveDuration))
                {
                    EnterIdle();
                    decisionElapsed = 0f;
                }

                return;
            }

            decisionElapsed += deltaTime;
            if (decisionElapsed < decisionRules.DecisionInterval)
            {
                return;
            }

            decisionElapsed %= decisionRules.DecisionInterval;
            var selectedState = (BehaviorState)SelectWeightedIndex(
                decisionRules.BehaviorWeights);
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
            if (!TryBeginMove(runtime, destination))
            {
                EnterIdle();
            }
        }

        private void EnterIdle()
        {
            currentState = BehaviorState.Idle;
        }
    }
}
