using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(
        fileName = "EntityDefinition",
        menuName = "Mini Civilization/Entities/Entity Definition")]
    public sealed class EntityDefinition : ScriptableObject
    {
        [SerializeField] private EntityController prefab;
        [SerializeField] private Sprite thumbnail;
        [SerializeField] private string displayName;

        public EntityController Prefab => prefab;
        public Sprite Thumbnail => thumbnail;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;
    }
}
