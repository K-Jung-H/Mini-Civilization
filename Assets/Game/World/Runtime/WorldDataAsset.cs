using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Persistence;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    [CreateAssetMenu(
        fileName = "WorldData",
        menuName = "Mini Civilization/World Data")]
    public sealed class WorldDataAsset : ScriptableObject
    {
        private const int CurrentPreparedMeshSchema = 3;

        [SerializeField, HideInInspector] private byte[] serializedWorld =
            Array.Empty<byte>();
        [SerializeField, HideInInspector] private bool hasPreparedRenderCache;
        [SerializeField, HideInInspector] private int preparedPatchSize;
        [SerializeField, HideInInspector] private int preparedPatchCount;
        [SerializeField, HideInInspector] private int preparedMeshSchema;
        [SerializeField, HideInInspector] private List<Mesh> preparedMeshes = new();

        [NonSerialized] private WorldData runtimeData;

        public bool HasData => runtimeData != null
            || (serializedWorld != null && serializedWorld.Length > 0);
        public int Seed => HasData ? Data.Seed : 0;
        public int WorldSize => HasData ? Data.Size : 0;
        public int WorldHeight => HasData ? Data.Height : 0;
        public float CellSize => HasData ? Data.CellSize : 0f;
        public int ChunkSizeX => HasData ? Data.ChunkSizeX : 0;
        public int ChunkSizeY => HasData ? Data.ChunkSizeY : 0;
        public int ChunkSizeZ => HasData ? Data.ChunkSizeZ : 0;
        public int SerializedByteCount => serializedWorld?.Length ?? 0;
        public bool HasPreparedRenderCache => hasPreparedRenderCache
            && preparedMeshSchema == CurrentPreparedMeshSchema;
        public int PreparedPatchSize => preparedPatchSize;
        public int PreparedPatchCount => preparedPatchCount;
        public IReadOnlyList<Mesh> PreparedMeshes => preparedMeshes;

        public WorldData Data
        {
            get
            {
                if (runtimeData == null)
                {
                    if (serializedWorld == null || serializedWorld.Length == 0)
                    {
                        throw new InvalidOperationException(
                            $"WorldDataAsset '{name}' does not contain world data.");
                    }

                    runtimeData = WorldSaveCodec.FromBytes(serializedWorld);
                }

                return runtimeData;
            }
        }

        public void Initialize(WorldData world, bool captureSerializedData = true)
        {
            runtimeData = world ?? throw new ArgumentNullException(nameof(world));
            if (captureSerializedData)
            {
                CaptureSerializedData();
            }
        }

        public void InitializeFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException(
                    "Serialized world data is empty.",
                    nameof(bytes));
            }

            serializedWorld = (byte[])bytes.Clone();
            runtimeData = WorldSaveCodec.FromBytes(serializedWorld);
            ClearPreparedRenderCache();
        }

        public byte[] ExportBytes()
        {
            return WorldSaveCodec.ToBytes(Data);
        }

        public void CaptureSerializedData()
        {
            serializedWorld = WorldSaveCodec.ToBytes(Data);
        }

        public WorldDataAsset CreateRuntimeWorkingCopy()
        {
            var copy = CreateInstance<WorldDataAsset>();
            copy.name = name + " (Runtime)";
            copy.hideFlags = HideFlags.DontSave;
            copy.InitializeFromBytes(ExportBytes());
            copy.hasPreparedRenderCache = hasPreparedRenderCache;
            copy.preparedPatchSize = preparedPatchSize;
            copy.preparedPatchCount = preparedPatchCount;
            copy.preparedMeshSchema = preparedMeshSchema;
            copy.preparedMeshes = new List<Mesh>(preparedMeshes);
            return copy;
        }

        public void SetPreparedRenderCache(
            int patchSize,
            int patchCount,
            IEnumerable<Mesh> meshes)
        {
            preparedPatchSize = Mathf.Max(0, patchSize);
            preparedPatchCount = Mathf.Max(0, patchCount);
            preparedMeshSchema = CurrentPreparedMeshSchema;
            preparedMeshes.Clear();
            if (meshes != null)
            {
                foreach (var mesh in meshes)
                {
                    if (mesh != null && !preparedMeshes.Contains(mesh))
                    {
                        preparedMeshes.Add(mesh);
                    }
                }
            }

            hasPreparedRenderCache = preparedPatchSize > 0
                && preparedPatchCount > 0
                && preparedMeshes.Count > 0;
        }

        public void ClearPreparedRenderCache()
        {
            hasPreparedRenderCache = false;
            preparedPatchSize = 0;
            preparedPatchCount = 0;
            preparedMeshSchema = 0;
            preparedMeshes.Clear();
        }

    }
}
