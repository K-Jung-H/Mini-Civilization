using System;
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
            return copy;
        }

    }
}
