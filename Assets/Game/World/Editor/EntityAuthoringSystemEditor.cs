using System;
using System.Collections.Generic;
using MiniCivilization.World.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(EntityAuthoringSystem))]
    public sealed class EntityAuthoringSystemEditor : UnityEditor.Editor
    {
        private SerializedProperty cellBoxPrefab;
        private SerializedProperty gridSize;

        private void OnEnable()
        {
            cellBoxPrefab = serializedObject.FindProperty("cellBoxPrefab");
            gridSize = serializedObject.FindProperty("gridSize");

            Synchronize((EntityAuthoringSystem)target);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(cellBoxPrefab);
            EditorGUILayout.PropertyField(gridSize);
            var changed = EditorGUI.EndChangeCheck();

            serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                Synchronize((EntityAuthoringSystem)target);
            }
        }

        private static void Synchronize(EntityAuthoringSystem system)
        {
            if (system == null
                || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorUtility.IsPersistent(system))
            {
                return;
            }

            var changed = system.NormalizeSettings();
            changed |= RemoveMissingPoolEntries(system);

            if (system.PooledPrefab != system.CellBoxPrefab)
            {
                changed |= ClearPool(system);
                system.PooledPrefab = system.CellBoxPrefab;
                changed = true;
            }

            if (system.CellBoxPrefab == null)
            {
                changed |= SetPoolActiveCount(system, 0);
            }
            else
            {
                changed |= SynchronizeCells(system);
            }

            if (changed)
            {
                MarkChanged(system);
            }
        }

        private static bool RemoveMissingPoolEntries(
            EntityAuthoringSystem system)
        {
            var changed = false;
            var cells = system.PooledCells;
            for (var index = cells.Count - 1; index >= 0; index--)
            {
                if (cells[index] != null)
                {
                    continue;
                }

                cells.RemoveAt(index);
                changed = true;
            }

            return changed;
        }

        private static bool ClearPool(EntityAuthoringSystem system)
        {
            var cells = system.PooledCells;
            if (cells.Count == 0)
            {
                return false;
            }

            for (var index = cells.Count - 1; index >= 0; index--)
            {
                var cell = cells[index];
                if (cell != null)
                {
                    DestroyImmediate(cell.gameObject);
                }
            }

            cells.Clear();
            return true;
        }

        private static bool SynchronizeCells(EntityAuthoringSystem system)
        {
            var desiredOffsets = CreateDesiredOffsets(system.GridSize);
            var desiredOffsetSet = new HashSet<Vector3Int>(desiredOffsets);
            var reservedByOffset = new Dictionary<
                Vector3Int,
                EntityAuthoringCellBox>();
            var reservedCells = new HashSet<EntityAuthoringCellBox>();
            var pool = system.PooledCells;

            for (var index = 0; index < pool.Count; index++)
            {
                var cell = pool[index];
                if (!desiredOffsetSet.Contains(cell.LocalOffset)
                    || reservedByOffset.ContainsKey(cell.LocalOffset))
                {
                    continue;
                }

                reservedByOffset.Add(cell.LocalOffset, cell);
                reservedCells.Add(cell);
            }

            var changed = false;
            var usedCells = new HashSet<EntityAuthoringCellBox>();
            var orderedPool = new List<EntityAuthoringCellBox>(
                Math.Max(pool.Count, desiredOffsets.Count));

            for (var index = 0; index < desiredOffsets.Count; index++)
            {
                var offset = desiredOffsets[index];
                if (!reservedByOffset.TryGetValue(offset, out var cell))
                {
                    cell = FindReusableCell(pool, usedCells, reservedCells)
                        ?? InstantiateCell(system);
                    changed = true;
                }

                usedCells.Add(cell);
                orderedPool.Add(cell);
                changed |= ConfigureCell(
                    system,
                    cell,
                    offset);
                changed |= SetCellActive(cell, true);
            }

            for (var index = 0; index < pool.Count; index++)
            {
                var cell = pool[index];
                if (usedCells.Contains(cell))
                {
                    continue;
                }

                changed |= SetCellActive(cell, false);
                orderedPool.Add(cell);
            }

            if (!HasSameOrder(pool, orderedPool))
            {
                pool.Clear();
                pool.AddRange(orderedPool);
                changed = true;
            }

            return changed;
        }

        private static List<Vector3Int> CreateDesiredOffsets(Vector3Int size)
        {
            var required = (long)size.x * size.y * size.z;
            if (required > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Entity Authoring Grid contains too many Cells.");
            }

            var offsets = new List<Vector3Int>((int)required);
            var minimum = new Vector3Int(
                -(size.x / 2),
                -(size.y / 2),
                -(size.z / 2));

            for (var yIndex = 0; yIndex < size.y; yIndex++)
            {
                for (var zIndex = 0; zIndex < size.z; zIndex++)
                {
                    for (var xIndex = 0; xIndex < size.x; xIndex++)
                    {
                        offsets.Add(new Vector3Int(
                            minimum.x + xIndex,
                            minimum.y + yIndex,
                            minimum.z + zIndex));
                    }
                }
            }

            return offsets;
        }

        private static EntityAuthoringCellBox FindReusableCell(
            IReadOnlyList<EntityAuthoringCellBox> pool,
            ISet<EntityAuthoringCellBox> usedCells,
            ISet<EntityAuthoringCellBox> reservedCells)
        {
            for (var index = 0; index < pool.Count; index++)
            {
                var cell = pool[index];
                if (!usedCells.Contains(cell) && !reservedCells.Contains(cell))
                {
                    return cell;
                }
            }

            return null;
        }

        private static EntityAuthoringCellBox InstantiateCell(
            EntityAuthoringSystem system)
        {
            var instance = PrefabUtility.InstantiatePrefab(
                system.CellBoxPrefab.gameObject,
                system.transform) as GameObject;
            if (instance != null
                && instance.TryGetComponent<EntityAuthoringCellBox>(
                    out var cell))
            {
                return cell;
            }

            if (instance != null)
            {
                DestroyImmediate(instance);
            }

            throw new InvalidOperationException(
                "Cell Box Prefab root requires EntityAuthoringCellBox.");
        }

        private static bool ConfigureCell(
            EntityAuthoringSystem system,
            EntityAuthoringCellBox cell,
            Vector3Int offset)
        {
            var changed = false;
            var expectedName = $"Cell ({offset.x}, {offset.y}, {offset.z})";
            if (cell.name != expectedName)
            {
                cell.name = expectedName;
                EditorUtility.SetDirty(cell.gameObject);
                changed = true;
            }

            var coordinateChanged = cell.AuthoringSystem != system
                || cell.LocalOffset != offset;
            if (cell.SetAuthoringContext(system, offset))
            {
                EditorUtility.SetDirty(cell);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cell);
                changed = true;
            }

            if (coordinateChanged
                && cell.SetTerrainHeight(system.CellBoxPrefab.TerrainHeight))
            {
                EditorUtility.SetDirty(cell);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cell);
                changed = true;
            }

            var cellTransform = cell.transform;
            if (cellTransform.localPosition != (Vector3)offset
                || cellTransform.localRotation != Quaternion.identity
                || cellTransform.localScale != Vector3.one)
            {
                cellTransform.SetLocalPositionAndRotation(
                    offset,
                    Quaternion.identity);
                cellTransform.localScale = Vector3.one;
                EditorUtility.SetDirty(cellTransform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    cellTransform);
                changed = true;
            }

            return changed;
        }

        private static bool SetPoolActiveCount(
            EntityAuthoringSystem system,
            int activeCount)
        {
            var changed = false;
            var cells = system.PooledCells;
            for (var index = 0; index < cells.Count; index++)
            {
                changed |= SetCellActive(cells[index], index < activeCount);
            }

            return changed;
        }

        private static bool SetCellActive(
            EntityAuthoringCellBox cell,
            bool active)
        {
            if (cell.gameObject.activeSelf == active)
            {
                return false;
            }

            cell.gameObject.SetActive(active);
            EditorUtility.SetDirty(cell.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(
                cell.gameObject);
            return true;
        }

        private static bool HasSameOrder(
            IReadOnlyList<EntityAuthoringCellBox> current,
            IReadOnlyList<EntityAuthoringCellBox> expected)
        {
            if (current.Count != expected.Count)
            {
                return false;
            }

            for (var index = 0; index < current.Count; index++)
            {
                if (current[index] != expected[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static void MarkChanged(EntityAuthoringSystem system)
        {
            EditorUtility.SetDirty(system);
            if (system.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(system.gameObject.scene);
            }
        }
    }
}
