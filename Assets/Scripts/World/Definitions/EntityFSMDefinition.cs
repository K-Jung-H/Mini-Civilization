using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    public abstract class EntityFSMDefinition : ScriptableObject
    {
        public abstract EntityFSM Create(EntityData data);

        public virtual void ValidateInitialization() { }
    }
}
