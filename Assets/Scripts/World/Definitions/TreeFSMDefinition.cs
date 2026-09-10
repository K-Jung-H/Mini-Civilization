using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Nature;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(fileName = "TreeFSM", menuName = "Mini Civilization/Entities/FSM/Tree")]
    public sealed class TreeFSMDefinition : EntityFSMDefinition
    {
        public override EntityFSM Create(EntityData data) =>
            new TreeEntityFSM(data);
    }
}
