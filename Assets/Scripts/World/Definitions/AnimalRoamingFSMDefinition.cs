using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Animal;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(fileName = "AnimalRoamingFSM", menuName = "Mini Civilization/Entities/FSM/Animal Roaming")]
    public sealed class AnimalRoamingFSMDefinition : EntityFSMDefinition
    {
        [SerializeField] private EntityCellMovementProfile cellMovementProfile;
        [SerializeField] private AnimalDecisionProfile decisionProfile;

        public override EntityFSM Create(EntityData data)
        {
            ValidateInitialization();
            return new AnimalRoamingFSM(
                data,
                cellMovementProfile.GetRuntimeRules(),
                decisionProfile.GetRuntimeRules());
        }

        public override void ValidateInitialization()
        {
            base.ValidateInitialization();
            if (cellMovementProfile == null
                || decisionProfile == null)
            {
                throw new InvalidOperationException($"Animal FSM definition '{name}' is incomplete.");
            }
        }
    }
}
