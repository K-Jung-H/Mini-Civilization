using System;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Generation
{
    public sealed class WorldGenerationController : MonoBehaviour
    {
        [SerializeField] private WorldGenerationSettings settings;
        [SerializeField] private int seed = 12345;

        public WorldGenerationSettings Settings => settings;
        public int Seed => seed;

        public WorldState Generate()
        {
            if (settings == null)
            {
                throw new InvalidOperationException(
                    "World generation settings are not assigned.");
            }

            return WorldGenerator.Generate(settings, seed);
        }

        public WorldDataAsset GenerateDataAsset()
        {
            var state = Generate();
            var asset = ScriptableObject.CreateInstance<WorldDataAsset>();
            asset.name = $"World {state.Data.Seed}";
            asset.hideFlags = HideFlags.DontSave;
            asset.Initialize(state.Data);
            return asset;
        }

        public void SetSettings(WorldGenerationSettings value)
        {
            settings = value;
        }

        public void SetSeed(int value)
        {
            seed = value;
        }

        public int RandomizeSeed()
        {
            seed = unchecked((int)DateTime.UtcNow.Ticks);
            return seed;
        }
    }
}
