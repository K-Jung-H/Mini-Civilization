using System;
using System.IO;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Persistence
{
    internal readonly struct WorldSaveLocation
    {
        private const string DataFileName = "world.worldsave";
        private const string RegionsDirectoryName = "regions";

        private WorldSaveLocation(
            string worldDirectoryPath,
            string worldFolderName)
        {
            WorldDirectoryPath = worldDirectoryPath;
            WorldFolderName = worldFolderName;
        }

        public string WorldDirectoryPath { get; }
        public string WorldFolderName { get; }
        public string DataPath => Path.Combine(
            WorldDirectoryPath,
            DataFileName);
        public string RegionsPath => Path.Combine(
            WorldDirectoryPath,
            RegionsDirectoryName);

        public static bool TryCreate(
            string worldFolderName,
            out WorldSaveLocation location) => TryCreateUnderRoot(
            WorldSaveStorage.WorldSavesDirectoryPath,
            worldFolderName,
            out location);

        internal static bool TryCreateTemporary(
            string rootPath,
            string worldFolderName,
            out WorldSaveLocation location) => TryCreateUnderRoot(
            rootPath,
            worldFolderName,
            out location);

        private static bool TryCreateUnderRoot(
            string rootPath,
            string worldFolderName,
            out WorldSaveLocation location)
        {
            location = default;
            if (string.IsNullOrWhiteSpace(rootPath)
                || !IsValidFolderName(worldFolderName))
            {
                return false;
            }

            try
            {
                var fullRootPath = Path.GetFullPath(rootPath);
                var worldDirectoryPath = Path.GetFullPath(Path.Combine(
                    fullRootPath,
                    worldFolderName.Trim()));
                if (!WorldSaveStorage.IsPathWithinRoot(
                        fullRootPath,
                        worldDirectoryPath))
                {
                    return false;
                }

                location = new WorldSaveLocation(
                    worldDirectoryPath,
                    worldFolderName.Trim());
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException
                                             || exception is NotSupportedException
                                             || exception is PathTooLongException)
            {
                return false;
            }
        }

        private static bool IsValidFolderName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value != value.Trim()
                || value == "."
                || value == "..")
            {
                return false;
            }

            return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && value.IndexOf(Path.DirectorySeparatorChar) < 0
                && value.IndexOf(Path.AltDirectorySeparatorChar) < 0;
        }
    }

    internal static class WorldSaveStorage
    {
        public const string WorldSavesDirectoryName = "WorldSaves";

        public static string HostRootPath => Path.GetFullPath(Path.Combine(
            Application.dataPath,
            ".."));

        public static string WorldSavesDirectoryPath => Path.Combine(
            HostRootPath,
            WorldSavesDirectoryName);

        internal static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(
                    file,
                    Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyDirectory(
                    directory,
                    Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        public static bool IsPathWithinRoot(string rootPath, string candidatePath)
        {
            var normalizedRoot = TrimEndingDirectorySeparators(
                Path.GetFullPath(rootPath));
            var normalizedCandidate = Path.GetFullPath(candidatePath);
            if (string.Equals(
                    normalizedRoot,
                    TrimEndingDirectorySeparators(normalizedCandidate),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimEndingDirectorySeparators(string value)
        {
            var rootLength = Path.GetPathRoot(value)?.Length ?? 0;
            var end = value.Length;
            while (end > rootLength
                   && (value[end - 1] == Path.DirectorySeparatorChar
                       || value[end - 1] == Path.AltDirectorySeparatorChar))
            {
                end--;
            }

            return end == value.Length ? value : value.Substring(0, end);
        }
    }

    internal sealed class WorldSaveRepository
    {
        private Guid worldId;

        public WorldSaveRepository(WorldSaveLocation location)
        {
            Location = location;
        }

        public WorldSaveLocation Location { get; }
        public bool Exists => File.Exists(Location.DataPath);

        public static WorldSaveRepository CreateTemporary()
        {
            var rootPath = Path.Combine(
                Application.temporaryCachePath,
                "MiniCivilization",
                "WorldSaves");
            var worldFolderName = Guid.NewGuid().ToString("N");
            if (!WorldSaveLocation.TryCreateTemporary(
                    rootPath,
                    worldFolderName,
                    out var location))
            {
                throw new InvalidOperationException(
                    "Temporary World Save location is invalid.");
            }

            return new WorldSaveRepository(location);
        }

        public static WorldSaveRepository CreateUnique(string saveName)
        {
            for (var suffix = 0; suffix < int.MaxValue; suffix++)
            {
                var worldFolderName = suffix == 0
                    ? saveName
                    : saveName + " " + suffix;
                if (!WorldSaveLocation.TryCreate(
                        worldFolderName,
                        out var location))
                {
                    throw new ArgumentException(
                        "Save Name is invalid.");
                }

                if (!Directory.Exists(location.WorldDirectoryPath)
                    && !File.Exists(location.WorldDirectoryPath))
                {
                    return new WorldSaveRepository(location);
                }
            }

            throw new InvalidOperationException(
                "No available World Save folder name remains.");
        }

        public bool TryReadSaveData(out WorldSaveData saveData)
        {
            if (!File.Exists(Location.DataPath))
            {
                saveData = null;
                return false;
            }

            using var stream = File.Open(
                Location.DataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            saveData = WorldSaveCodec.ReadSaveData(stream);
            worldId = saveData.WorldId;
            return true;
        }

        public bool TryReadDescriptor(out WorldSaveDescriptor descriptor)
        {
            descriptor = default;
            if (!File.Exists(Location.DataPath))
            {
                return false;
            }

            using var stream = File.Open(
                Location.DataPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            WorldSaveCodec.ReadSaveIdentity(
                stream,
                out var savedWorldId,
                out var savedSaveName);
            descriptor = new WorldSaveDescriptor(
                Location.WorldFolderName,
                savedSaveName,
                savedWorldId);
            return true;
        }

        public bool TryReadChunk(
            ChunkCoordinate coordinate,
            out WorldChunkSnapshot snapshot)
        {
            var regionPath = GetRegionPath(coordinate);
            if (!File.Exists(regionPath))
            {
                snapshot = null;
                return false;
            }

            return WorldRegionFile.TryReadChunk(
                regionPath,
                RequireWorldId(),
                coordinate,
                out snapshot);
        }

        public void WriteSaveData(WorldSaveData saveData)
        {
            if (saveData == null)
            {
                throw new ArgumentNullException(nameof(saveData));
            }

            WriteAtomically(
                Location.DataPath,
                stream => WorldSaveCodec.WriteSaveData(stream, saveData));
            worldId = saveData.WorldId;
        }

        public void WriteChunk(WorldChunkSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Directory.CreateDirectory(Location.RegionsPath);
            WorldRegionFile.WriteChunk(
                GetRegionPath(snapshot.Coordinate),
                RequireWorldId(),
                snapshot);
        }

        public void CopyPackageTo(WorldSaveRepository destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (!Exists)
            {
                throw new InvalidOperationException(
                    "World Save Data must exist before it can be promoted.");
            }

            if (Directory.Exists(destination.Location.WorldDirectoryPath)
                || File.Exists(destination.Location.WorldDirectoryPath))
            {
                throw new InvalidOperationException(
                    "Destination World Save location is already in use.");
            }

            try
            {
                WorldSaveStorage.CopyDirectory(
                    Location.WorldDirectoryPath,
                    destination.Location.WorldDirectoryPath);
                destination.worldId = RequireWorldId();
            }
            catch
            {
                destination.DeletePackage();
                throw;
            }
        }

        public void RekeyWorldId(Guid newWorldId)
        {
            if (newWorldId == Guid.Empty)
            {
                throw new ArgumentException(
                    "World save ID cannot be empty.",
                    nameof(newWorldId));
            }

            var previousWorldId = RequireWorldId();
            if (previousWorldId == newWorldId)
            {
                return;
            }

            if (Directory.Exists(Location.RegionsPath))
            {
                foreach (var regionPath in Directory.GetFiles(
                             Location.RegionsPath,
                             "*.region",
                             SearchOption.TopDirectoryOnly))
                {
                    WorldRegionFile.ReplaceWorldId(
                        regionPath,
                        previousWorldId,
                        newWorldId);
                }
            }

            worldId = newWorldId;
        }

        public void DeletePackage()
        {
            if (Directory.Exists(Location.WorldDirectoryPath))
            {
                Directory.Delete(Location.WorldDirectoryPath, true);
            }
        }

        private string GetRegionPath(ChunkCoordinate coordinate)
        {
            WorldRegionFile.GetRegionCoordinate(
                coordinate,
                out var regionX,
                out var regionZ,
                out _);
            return Path.Combine(
                Location.RegionsPath,
                $"r.{regionX}.{regionZ}.region");
        }

        private Guid RequireWorldId()
        {
            if (worldId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Load or write World Save Data before accessing Region data.");
            }

            return worldId;
        }

        private static void WriteAtomically(string path, Action<Stream> write)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    "World Save Data has no parent directory.");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp";
            try
            {
                using (var stream = File.Open(
                           temporaryPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    write(stream);
                    stream.Flush();
                }

                if (File.Exists(path))
                {
                    File.Replace(temporaryPath, path, null);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

    }
}
