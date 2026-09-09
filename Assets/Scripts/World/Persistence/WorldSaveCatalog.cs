using System;
using System.Collections.Generic;
using System.IO;

namespace MiniCivilization.World.Persistence
{
    internal readonly struct WorldSaveDescriptor
    {
        public WorldSaveDescriptor(
            string worldFolderName,
            string saveName,
            Guid worldId)
        {
            WorldFolderName = !string.IsNullOrWhiteSpace(worldFolderName)
                ? worldFolderName
                : throw new ArgumentException(
                    "World folder name cannot be empty.",
                    nameof(worldFolderName));
            SaveName = !string.IsNullOrWhiteSpace(saveName)
                ? saveName
                : throw new ArgumentException(
                    "World save name cannot be empty.",
                    nameof(saveName));
            WorldId = worldId != Guid.Empty
                ? worldId
                : throw new ArgumentException(
                    "World ID cannot be empty.",
                    nameof(worldId));
        }

        public string WorldFolderName { get; }
        public string SaveName { get; }
        public Guid WorldId { get; }
    }

    internal static class WorldSaveCatalog
    {
        public static IReadOnlyList<WorldSaveDescriptor> GetWorlds()
        {
            var result = new List<WorldSaveDescriptor>();
            var saveDirectoryPath = WorldSaveStorage.WorldSavesDirectoryPath;
            if (!Directory.Exists(saveDirectoryPath))
            {
                return result;
            }

            foreach (var directoryPath in Directory.GetDirectories(saveDirectoryPath))
            {
                var worldFolderName = Path.GetFileName(directoryPath);
                if (!WorldSaveLocation.TryCreate(
                        worldFolderName,
                        out var location))
                {
                    continue;
                }

                try
                {
                    var repository = new WorldSaveRepository(location);
                    if (repository.TryReadDescriptor(out var descriptor))
                    {
                        result.Add(descriptor);
                    }
                }
                catch (Exception exception) when (exception is IOException
                                                   || exception is UnauthorizedAccessException
                                                   || exception is InvalidDataException)
                {
                    // A non-world or incomplete directory is not a selectable world.
                }
            }

            result.Sort(CompareDescriptors);
            return result;
        }

        private static int CompareDescriptors(
            WorldSaveDescriptor left,
            WorldSaveDescriptor right)
        {
            var result = string.Compare(
                left.SaveName,
                right.SaveName,
                StringComparison.OrdinalIgnoreCase);
            return result != 0
                ? result
                : string.Compare(
                    left.WorldFolderName,
                    right.WorldFolderName,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
