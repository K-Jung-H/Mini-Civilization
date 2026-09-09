using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniCivilization.World.Entities
{
    public sealed class AnimalDecisionRules
    {
        private readonly int[] behaviorWeights;
        public float DecisionInterval { get; }
        public IReadOnlyList<int> BehaviorWeights => behaviorWeights;

        public AnimalDecisionRules(
            float decisionInterval,
            int idleWeight,
            int moveWeight)
        {
            if (decisionInterval <= 0f
                || float.IsNaN(decisionInterval)
                || float.IsInfinity(decisionInterval))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(decisionInterval));
            }

            ValidateWeight(idleWeight, nameof(idleWeight));
            ValidateWeight(moveWeight, nameof(moveWeight));
            ValidateGroup(idleWeight, moveWeight);

            DecisionInterval = decisionInterval;
            behaviorWeights = new[] { idleWeight, moveWeight };
        }

        private static void ValidateWeight(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        private static void ValidateGroup(params int[] weights)
        {
            var total = 0;
            for (var index = 0; index < weights.Length; index++)
            {
                total = checked(total + weights[index]);
            }

            if (total == 0)
            {
                throw new ArgumentException(
                    "At least one state weight must be greater than zero.");
            }
        }
    }

    [CreateAssetMenu(
        fileName = "Animal Decision Profile",
        menuName = "Mini Civilization/Entities/Animal Decision Profile")]
    public sealed class AnimalDecisionProfile : ScriptableObject
    {
        [SerializeField, Min(0.01f)] private float decisionInterval = 1f;
        [SerializeField, Min(0)] private int idleWeight = 9;
        [SerializeField, Min(0)] private int moveWeight = 1;

        private AnimalDecisionRules cachedRules;

        public AnimalDecisionRules GetRuntimeRules() =>
            cachedRules ??= new AnimalDecisionRules(
                decisionInterval,
                idleWeight,
                moveWeight);

        private void OnValidate()
        {
            cachedRules = null;
        }
    }
}
