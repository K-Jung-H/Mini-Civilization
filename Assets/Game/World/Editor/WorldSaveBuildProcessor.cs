using System;
using System.Collections.Generic;
using System.IO;
using MiniCivilization.World.Persistence;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace MiniCivilization.World.Editor
{
    public sealed class WorldSaveBuildProcessor :
        IPreprocessBuildWithReport,
        IProcessSceneWithReport,
        IPostprocessBuildWithReport
    {
        private static readonly List<WorldSaveLocation> initialWorldLocations = new();
        private static readonly HashSet<string> initialWorldDirectories = new(
            StringComparer.OrdinalIgnoreCase);

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            initialWorldLocations.Clear();
            initialWorldDirectories.Clear();
        }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var manager in root.GetComponentsInChildren<SaveLoadManager>(true))
                {
                    if (string.IsNullOrWhiteSpace(manager.InitialWorldFolderName))
                    {
                        continue;
                    }

                    if (!manager.TryGetInitialSaveLocation(out var location))
                    {
                        throw new BuildFailedException(
                            "Initial World Save location is invalid.");
                    }

                    if (!initialWorldDirectories.Add(location.WorldDirectoryPath))
                    {
                        continue;
                    }

                    initialWorldLocations.Add(location);
                }
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            try
            {
                if (initialWorldLocations.Count == 0)
                {
                    return;
                }

                var playerRoot = Path.GetDirectoryName(Path.GetFullPath(
                    report.summary.outputPath));
                if (string.IsNullOrEmpty(playerRoot))
                {
                    throw new BuildFailedException(
                        "Unable to resolve the Player output directory for World Saves.");
                }

                foreach (var location in initialWorldLocations)
                {
                    if (!Directory.Exists(location.WorldDirectoryPath)
                        || !File.Exists(location.DataPath))
                    {
                        throw new BuildFailedException(
                            $"Initial World Save '{location.WorldFolderName}' does not exist.");
                    }

                    ReplaceDirectory(
                        location.WorldDirectoryPath,
                        Path.Combine(
                            playerRoot,
                            WorldSaveStorage.WorldSavesDirectoryName,
                            location.WorldFolderName));
                }
            }
            finally
            {
                initialWorldLocations.Clear();
                initialWorldDirectories.Clear();
            }
        }

        private static void ReplaceDirectory(string source, string destination)
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            CopyDirectory(source, destination);
        }

        private static void CopyDirectory(string source, string destination)
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
    }
}
