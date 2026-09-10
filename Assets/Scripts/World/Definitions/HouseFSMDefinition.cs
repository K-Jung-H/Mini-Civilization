using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Building;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(fileName = "HouseFSM", menuName = "Mini Civilization/Entities/FSM/House")]
    public sealed class HouseFSMDefinition : EntityFSMDefinition
    {
        [SerializeField] private BuildingLayoutDefinition layoutDefinition;

        public override EntityFSM Create(EntityData data)
        {
            ValidateInitialization();
            return new HouseEntityFSM(data, layoutDefinition.GetLayout());
        }

        public override void ValidateInitialization()
        {
            base.ValidateInitialization();
            if (layoutDefinition == null)
            {
                throw new InvalidOperationException($"Building FSM definition '{name}' is incomplete.");
            }
        }
    }
}
