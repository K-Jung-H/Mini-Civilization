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
        [SerializeField, HideInInspector] private byte[] serializedWorld =
            Array.Empty<byte>();
        [SerializeField, HideInInspector] private int seed;
        [SerializeField, HideInInspector] private int worldSize;
        [SerializeField, HideInInspector] private int worldHeight;
        [SerializeField, HideInInspector] private int chunkSizeX;
        [SerializeField, HideInInspector] private int chunkSizeY;
        [SerializeField, HideInInspector] private int chunkSizeZ;
        [SerializeField, HideInInspector] private bool hasPreparedRenderCache;
        [SerializeField, HideInInspector] private int preparedPatchSize;
        [SerializeField, HideInInspector] private int preparedPatchCount;
        [SerializeField, HideInInspector] private List<Mesh> preparedMeshes = new();

        [NonSerialized] private WorldData runtimeData;

        public bool HasData => runtimeData != null
            || (serializedWorld != null && serializedWorld.Length > 0);
        public int Seed => runtimeData?.Seed ?? seed;
        public int WorldSize => runtimeData?.Size ?? worldSize;
        public int WorldHeight => runtimeData?.Height ?? worldHeight;
        public int ChunkSizeX => runtimeData?.ChunkSizeX ?? chunkSizeX;
        public int ChunkSizeY => runtimeData?.ChunkSizeY ?? chunkSizeY;
        public int ChunkSizeZ => runtimeData?.ChunkSizeZ ?? chunkSizeZ;
        public int SerializedByteCount => serializedWorld?.Length ?? 0;
        public bool HasPreparedRenderCache => hasPreparedRenderCache;
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
                    UpdateMetadata(runtimeData);
                }

                return runtimeData;
            }
        }

        public void Initialize(WorldData world, bool captureSerializedData = true)
        {
            runtimeData = world ?? throw new ArgumentNullException(nameof(world));
            UpdateMetadata(world);
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
            UpdateMetadata(runtimeData);
            ClearPreparedRenderCache();
        }

        public byte[] ExportBytes()
        {
            return WorldSaveCodec.ToBytes(Data);
        }

        public void CaptureSerializedData()
        {
            serializedWorld = WorldSaveCodec.ToBytes(Data);
            UpdateMetadata(runtimeData);
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
            preparedMeshes.Clear();
        }

        private void UpdateMetadata(WorldData world)
        {
            seed = world.Seed;
            worldSize = world.Size;
            worldHeight = world.Height;
            chunkSizeX = world.ChunkSizeX;
            chunkSizeY = world.ChunkSizeY;
            chunkSizeZ = world.ChunkSizeZ;
        }
    }
}
