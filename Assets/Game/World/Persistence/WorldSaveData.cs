using System;
using MiniCivilization.World.Generation.Patterns;

namespace MiniCivilization.World.Persistence
{
    internal sealed class WorldSaveData
    {
        public WorldSaveData(
            Guid worldId,
            string saveName,
            WorldGenerationConfiguration generation,
            WorldSaveMetadata progress)
        {
            if (worldId == Guid.Empty)
            {
                throw new ArgumentException(
                    "World save ID cannot be empty.",
                    nameof(worldId));
            }

            if (string.IsNullOrWhiteSpace(saveName))
            {
                throw new ArgumentException(
                    "World save name cannot be empty.",
                    nameof(saveName));
            }

            WorldId = worldId;
            SaveName = saveName;
            Generation = generation ?? throw new ArgumentNullException(
                nameof(generation));
            Progress = progress ?? throw new ArgumentNullException(
                nameof(progress));
        }

        public Guid WorldId { get; }
        public string SaveName { get; }
        public WorldGenerationConfiguration Generation { get; }
        public WorldSaveMetadata Progress { get; }

        public WorldSaveData WithProgress(WorldSaveMetadata progress) => new(
            WorldId,
            SaveName,
            Generation,
            progress);

        public WorldSaveData WithSaveName(string value) => new(
            WorldId,
            value,
            Generation,
            Progress);

        public WorldSaveData WithWorldId(Guid value) => new(
            value,
            SaveName,
            Generation,
            Progress);
    }
}
