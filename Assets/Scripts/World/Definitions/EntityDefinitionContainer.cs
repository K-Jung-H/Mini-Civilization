using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    public abstract class EntityDefinitionContainer : ScriptableObject
    {
        [SerializeField] private List<EntityDefinition> definitions = new();

        public abstract EntityCategory Category { get; }
        public IReadOnlyList<EntityDefinition> Definitions => definitions;
    }
}
