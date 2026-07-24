using System;
using System.IO;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Persistence
{
    public sealed class WorldPersistence : MonoBehaviour
    {
        [SerializeField] private string directoryName = "Worlds";
        [SerializeField] private string fileName = "default.mcw";
        [SerializeField] private string configuredPathOverride;
        [SerializeField] private bool overwriteExisting = true;

        private string activeSavePath;

        public string ConfiguredSavePath => ResolveConfiguredSavePath();
        public string ActiveSavePath => string.IsNullOrEmpty(activeSavePath)
            ? ConfiguredSavePath
            : activeSavePath;
        public string SavePath => ActiveSavePath;
        public bool OverwriteExisting => overwriteExisting;

        public bool SaveExists()
        {
            return File.Exists(ActiveSavePath);
        }

        public bool SaveExists(string path)
        {
            return File.Exists(NormalizeExplicitPath(path));
        }

        public void Save(WorldData world)
        {
            Save(world, ActiveSavePath);
        }

        public void Save(WorldData world, string path)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var resolvedPath = NormalizeExplicitPath(path);
            var directory = Path.GetDirectoryName(resolvedPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("The world save directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            if (File.Exists(resolvedPath) && !overwriteExisting)
            {
                throw new IOException($"A world save already exists at '{resolvedPath}'.");
            }

            var temporaryPath = resolvedPath + ".tmp";
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    WorldSaveCodec.Write(stream, world);
                    stream.Flush(true);
                }

                if (File.Exists(resolvedPath))
                {
                    File.Replace(temporaryPath, resolvedPath, null);
                }
                else
                {
                    File.Move(temporaryPath, resolvedPath);
                }

                activeSavePath = resolvedPath;
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public WorldData Load()
        {
            return Load(ActiveSavePath);
        }

        public WorldData Load(string path)
        {
            var resolvedPath = NormalizeExplicitPath(path);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException(
                    "The world save does not exist.",
                    resolvedPath);
            }

            using var stream = new FileStream(
                resolvedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var world = WorldSaveCodec.Read(stream);
            activeSavePath = resolvedPath;
            return world;
        }

        public byte[] SaveToBytes(WorldData world)
        {
            return WorldSaveCodec.ToBytes(world);
        }

        public WorldData LoadFromBytes(byte[] data)
        {
            return WorldSaveCodec.FromBytes(data);
        }

        public void Save(Stream destination, WorldData world)
        {
            WorldSaveCodec.Write(destination, world);
        }

        public WorldData Load(Stream source)
        {
            return WorldSaveCodec.Read(source);
        }

        public void Configure(
            string relativeDirectory,
            string saveFileName,
            bool allowOverwrite)
        {
            directoryName = relativeDirectory;
            fileName = saveFileName;
            overwriteExisting = allowOverwrite;
            activeSavePath = null;
        }

        public void UseConfiguredSavePath()
        {
            activeSavePath = null;
        }

        public void SetConfiguredSavePath(string path)
        {
            configuredPathOverride = NormalizeExplicitPath(path);
            activeSavePath = null;
        }

        public void ClearConfiguredSavePathOverride()
        {
            configuredPathOverride = string.Empty;
            activeSavePath = null;
        }

        private string ResolveConfiguredSavePath()
        {
            if (!string.IsNullOrWhiteSpace(configuredPathOverride))
            {
                return NormalizeExplicitPath(configuredPathOverride);
            }

            if (string.IsNullOrWhiteSpace(directoryName))
            {
                throw new InvalidOperationException("World save directory name is empty.");
            }

            if (string.IsNullOrWhiteSpace(fileName)
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || Path.GetFileName(fileName) != fileName)
            {
                throw new InvalidOperationException("World save file name is invalid.");
            }

            var root = Path.GetFullPath(Application.persistentDataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, directoryName, fileName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "World save path must remain inside Application.persistentDataPath.");
            }

            return path;
        }

        private static string NormalizeExplicitPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("World save path is empty.", nameof(path));
            }

            return Path.GetFullPath(path);
        }
    }
}
