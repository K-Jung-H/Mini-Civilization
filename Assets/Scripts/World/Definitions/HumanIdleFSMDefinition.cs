using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Human;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(fileName = "HumanIdleFSM", menuName = "Mini Civilization/Entities/FSM/Human Idle")]
    public sealed class HumanIdleFSMDefinition : EntityFSMDefinition
    {
        public override EntityFSM Create(EntityData data) =>
            new global::MiniCivilization.World.Entities.Human.HumanEntityFSM(data);
    }
}
