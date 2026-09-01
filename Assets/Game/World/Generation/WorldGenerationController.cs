using System;
using MiniCivilization.World.Domain;
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

        public WorldData Generate()
        {
            var input = CreateBuildInput();
            return WorldGenerationPipeline.Build(input);
        }

        public WorldBuildInput CreateBuildInput()
        {
            if (settings == null)
            {
                throw new InvalidOperationException(
                    "World generation settings are not assigned.");
            }

            return WorldBuildInput.Create(settings, seed);
        }

        public WorldDataAsset GenerateDataAsset()
        {
            var state = Generate();
            var asset = ScriptableObject.CreateInstance<WorldDataAsset>();
            asset.name = $"World {state.Seed}";
            asset.hideFlags = HideFlags.DontSave;
            asset.Initialize(state);
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
