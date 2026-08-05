using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal sealed class HydrologyBasin
    {
        private readonly int[] columnIndices;

        public int Id { get; }
        public IReadOnlyList<int> ColumnIndices => columnIndices;
        public int SpillHeightUnits { get; }
        public int OutletColumnIndex { get; }
        public int MinimumSeaDistance { get; }
        public int MaximumDepthUnits { get; }

        public HydrologyBasin(
            int id,
            IReadOnlyList<int> columns,
            int spillHeightUnits,
            int outletColumnIndex,
            int minimumSeaDistance,
            int maximumDepthUnits)
        {
            if (columns == null || columns.Count == 0)
            {
                throw new ArgumentException(
                    "A hydrology basin must contain at least one column.",
                    nameof(columns));
            }

            Id = id;
            columnIndices = new int[columns.Count];
            for (var index = 0; index < columns.Count; index++)
            {
                columnIndices[index] = columns[index];
            }

            Array.Sort(columnIndices);
            SpillHeightUnits = Math.Max(0, spillHeightUnits);
            OutletColumnIndex = outletColumnIndex;
            MinimumSeaDistance = Math.Max(0, minimumSeaDistance);
            MaximumDepthUnits = Math.Max(0, maximumDepthUnits);
        }
    }

    /// <summary>
    /// Read-only-to-consumers hydrology analysis storage. It contains terrain
    /// facts and drainage results only; it never places water in WorldData.
    /// </summary>
    internal sealed class HydrologyMap
    {
        private readonly int[] terrainHeightUnits;
        private readonly int[] filledHeightUnits;
        private readonly int[] receiverColumnIndices;
        private readonly int[] flowAccumulation;
        private readonly int[] seaDistances;
        private readonly List<HydrologyBasin> basins = new();

        public int Size { get; }
        public int ColumnCount => terrainHeightUnits.Length;
        public IReadOnlyList<HydrologyBasin> Basins => basins;

        public HydrologyMap(int size, IReadOnlyList<int> terrainHeights)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (terrainHeights == null
                || terrainHeights.Count != checked(size * size))
            {
                throw new ArgumentException(
                    "Hydrology terrain heights must cover the entire map.",
                    nameof(terrainHeights));
            }

            Size = size;
            terrainHeightUnits = new int[terrainHeights.Count];
            filledHeightUnits = new int[terrainHeights.Count];
            receiverColumnIndices = new int[terrainHeights.Count];
            flowAccumulation = new int[terrainHeights.Count];
            seaDistances = new int[terrainHeights.Count];
            Array.Fill(receiverColumnIndices, -1);
            Array.Fill(seaDistances, int.MaxValue);

            for (var index = 0; index < terrainHeights.Count; index++)
            {
                var height = Math.Max(0, terrainHeights[index]);
                terrainHeightUnits[index] = height;
                filledHeightUnits[index] = height;
                flowAccumulation[index] = 1;
            }
        }

        public bool Contains(int x, int z) =>
            (uint)x < Size && (uint)z < Size;

        public int ToIndex(int x, int z)
        {
            if (!Contains(x, z))
            {
                throw new ArgumentOutOfRangeException(
                    $"Hydrology column ({x}, {z}) is outside the map.");
            }

            return x + Size * z;
        }

        public int GetTerrainHeightUnits(int index) =>
            terrainHeightUnits[ValidateIndex(index)];
        public int GetFilledHeightUnits(int index) =>
            filledHeightUnits[ValidateIndex(index)];
        public int GetReceiverColumnIndex(int index) =>
            receiverColumnIndices[ValidateIndex(index)];
        public int GetFlowAccumulation(int index) =>
            flowAccumulation[ValidateIndex(index)];
        public int GetSeaDistance(int index) =>
            seaDistances[ValidateIndex(index)];

        internal void SetFilledHeightUnits(int index, int value) =>
            filledHeightUnits[ValidateIndex(index)] = Math.Max(0, value);

        internal void SetReceiverColumnIndex(int index, int receiverIndex)
        {
            index = ValidateIndex(index);
            if (receiverIndex < -1 || receiverIndex >= ColumnCount)
            {
                throw new ArgumentOutOfRangeException(nameof(receiverIndex));
            }

            receiverColumnIndices[index] = receiverIndex;
        }

        internal void SetFlowAccumulation(int index, int value) =>
            flowAccumulation[ValidateIndex(index)] = Math.Max(1, value);
        internal void SetSeaDistance(int index, int value) =>
            seaDistances[ValidateIndex(index)] = Math.Max(0, value);
        internal void AddBasin(HydrologyBasin basin)
        {
            if (basin == null)
            {
                throw new ArgumentNullException(nameof(basin));
            }

            basins.Add(basin);
        }

        private int ValidateIndex(int index)
        {
            if ((uint)index >= ColumnCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return index;
        }
    }

    internal readonly struct PlannedTerrainColumn
    {
        public readonly int X;
        public readonly int Z;
        public readonly int TargetHeightUnits;

        public PlannedTerrainColumn(
            int x,
            int z,
            int targetHeightUnits)
        {
            X = x;
            Z = z;
            TargetHeightUnits = Math.Max(0, targetHeightUnits);
        }
    }

    internal readonly struct PlannedWaterCell
    {
        public readonly CellCoordinate Coordinate;
        public readonly WaterData Water;

        public PlannedWaterCell(
            CellCoordinate coordinate,
            FlowDirection direction,
            WaterType type)
        {
            Coordinate = coordinate;
            Water = new WaterData
            {
                Amount = WaterAmount.Full,
                Role = WaterRole.Source,
                Type = type,
                Flow = direction
            };
        }
    }

    internal class WaterFeaturePlan
    {
        private readonly Dictionary<int, PlannedTerrainColumn>
            terrainColumns = new();
        private readonly Dictionary<int, PlannedWaterCell> sourceCells = new();
        private readonly HashSet<int> allowedWetCellIndices = new();
        private readonly HashSet<int> requiredWetCellIndices = new();

        public int WorldSize { get; }
        public int WorldHeight { get; }
        public IReadOnlyDictionary<int, PlannedTerrainColumn> TerrainColumns =>
            terrainColumns;
        public IReadOnlyDictionary<int, PlannedWaterCell> SourceCells =>
            sourceCells;
        public IReadOnlyCollection<int> AllowedWetCellIndices =>
            allowedWetCellIndices;
        public IReadOnlyCollection<int> RequiredWetCellIndices =>
            requiredWetCellIndices;

        protected WaterFeaturePlan(
            int worldSize,
            int worldHeight)
        {
            if (worldSize <= 0 || worldHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldSize));
            }

            WorldSize = worldSize;
            WorldHeight = worldHeight;
        }

        public int EncodeColumn(int x, int z)
        {
            ValidateColumn(x, z);
            return x + WorldSize * z;
        }

        public int EncodeCell(CellCoordinate coordinate)
        {
            ValidateCell(coordinate);
            return coordinate.X
                + WorldSize * (coordinate.Z + WorldSize * coordinate.Y);
        }

        public void SetTerrainColumn(in PlannedTerrainColumn column)
        {
            var index = EncodeColumn(column.X, column.Z);
            terrainColumns[index] = column;
        }

        public void AddSourceCell(in PlannedWaterCell source)
        {
            var index = EncodeCell(source.Coordinate);
            sourceCells[index] = source;
            allowedWetCellIndices.Add(index);
            requiredWetCellIndices.Add(index);
        }

        public void AddAllowedWetCell(CellCoordinate coordinate) =>
            allowedWetCellIndices.Add(EncodeCell(coordinate));

        public void AddRequiredWetCell(CellCoordinate coordinate)
        {
            var index = EncodeCell(coordinate);
            requiredWetCellIndices.Add(index);
            allowedWetCellIndices.Add(index);
        }

        private void ValidateColumn(int x, int z)
        {
            if ((uint)x >= WorldSize || (uint)z >= WorldSize)
            {
                throw new ArgumentOutOfRangeException(
                    $"Planned column ({x}, {z}) is outside the world.");
            }
        }

        private void ValidateCell(CellCoordinate coordinate)
        {
            ValidateColumn(coordinate.X, coordinate.Z);
            if ((uint)coordinate.Y >= WorldHeight)
            {
                throw new ArgumentOutOfRangeException(
                    $"Planned Cell {coordinate} is outside the world.");
            }
        }
    }

    internal readonly struct BasinConnectionPort
    {
        public readonly int BasinWetColumnIndex;
        public readonly int ShoreColumnIndex;
        public readonly int InterfaceSurfaceHeightUnits;

        public BasinConnectionPort(
            int basinWetColumnIndex,
            int shoreColumnIndex,
            int interfaceSurfaceHeightUnits)
        {
            BasinWetColumnIndex = basinWetColumnIndex;
            ShoreColumnIndex = shoreColumnIndex;
            InterfaceSurfaceHeightUnits = Math.Max(
                1,
                interfaceSurfaceHeightUnits);
        }
    }

    internal enum HydrologyColumnOwnership : byte
    {
        None = 0,
        LakeInterior = 1,
        LakeShore = 2,
        RiverChannel = 3,
        ConnectionPort = 4
    }

    internal enum RiverWaterMode : byte
    {
        Dynamic = 0,
        Source = 1
    }

    internal enum RiverTerrainStyle : byte
    {
        Mountain = 0,
        Lowland = 1,
        Stepped = 2
    }

    internal sealed class ChannelPlan : WaterFeaturePlan
    {
        private readonly HashSet<int> channelColumnIndices = new();
        private readonly List<BasinConnectionPort> connections = new();

        public IReadOnlyCollection<int> ChannelColumnIndices =>
            channelColumnIndices;
        public IReadOnlyList<BasinConnectionPort> Connections => connections;

        public ChannelPlan(
            int worldSize,
            int worldHeight) : base(
                worldSize,
                worldHeight)
        {
        }

        public void AddChannelColumn(int columnIndex)
        {
            if ((uint)columnIndex >= (uint)(WorldSize * WorldSize))
            {
                throw new ArgumentOutOfRangeException(nameof(columnIndex));
            }

            channelColumnIndices.Add(columnIndex);
        }

        public void AddConnection(in BasinConnectionPort connection) =>
            connections.Add(connection);
    }

    internal sealed class BasinPlan : WaterFeaturePlan
    {
        private readonly List<int> wetColumnIndices = new();

        public int BasinId { get; }
        public int WaterSurfaceHeightUnits { get; }
        public int OutletColumnIndex { get; }
        public WaterType Type { get; }
        public IReadOnlyList<int> WetColumnIndices => wetColumnIndices;

        public BasinPlan(
            int worldSize,
            int worldHeight,
            int basinId,
            int waterSurfaceHeightUnits,
            int outletColumnIndex,
            WaterType type) : base(
                worldSize,
                worldHeight)
        {
            BasinId = basinId;
            Type = type is WaterType.Pond or WaterType.Lake
                ? type
                : throw new ArgumentOutOfRangeException(nameof(type));
            WaterSurfaceHeightUnits = Math.Max(
                0,
                waterSurfaceHeightUnits);
            if (outletColumnIndex < -1
                || outletColumnIndex >= worldSize * worldSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outletColumnIndex));
            }

            OutletColumnIndex = outletColumnIndex;
        }

        public void AddWetColumn(int columnIndex)
        {
            if ((uint)columnIndex >= (uint)(WorldSize * WorldSize))
            {
                throw new ArgumentOutOfRangeException(nameof(columnIndex));
            }

            wetColumnIndices.Add(columnIndex);
        }
    }

    internal sealed class HydrologyFeaturePlan : WaterFeaturePlan
    {
        private readonly List<BasinPlan> basins = new();
        private readonly List<ChannelPlan> channels = new();
        private readonly HydrologyColumnOwnership[] ownership;

        public IReadOnlyList<BasinPlan> Basins => basins;
        public IReadOnlyList<ChannelPlan> Channels => channels;

        private HydrologyFeaturePlan(int worldSize, int worldHeight) : base(
            worldSize,
            worldHeight)
        {
            ownership = new HydrologyColumnOwnership[
                checked(worldSize * worldSize)];
        }

        public static bool TryCreate(
            int worldSize,
            int worldHeight,
            IReadOnlyList<BasinPlan> basinPlans,
            IReadOnlyList<ChannelPlan> channelPlans,
            out HydrologyFeaturePlan featurePlan)
        {
            featurePlan = new HydrologyFeaturePlan(worldSize, worldHeight);
            for (var index = 0; index < basinPlans.Count; index++)
            {
                if (!featurePlan.TryMergeBasin(basinPlans[index]))
                {
                    featurePlan = null;
                    return false;
                }
            }

            for (var index = 0; index < channelPlans.Count; index++)
            {
                if (!featurePlan.TryMergeChannel(channelPlans[index]))
                {
                    featurePlan = null;
                    return false;
                }
            }

            return true;
        }

        private bool TryMergeBasin(BasinPlan basin)
        {
            if (!HasMatchingDimensions(basin) || !TryMergePlan(basin))
            {
                return false;
            }

            for (var index = 0; index < basin.WetColumnIndices.Count; index++)
            {
                var columnIndex = basin.WetColumnIndices[index];
                if (ownership[columnIndex] != HydrologyColumnOwnership.None)
                {
                    return false;
                }

                ownership[columnIndex] =
                    HydrologyColumnOwnership.LakeInterior;
            }

            for (var index = 0; index < basin.WetColumnIndices.Count; index++)
            {
                var columnIndex = basin.WetColumnIndices[index];
                var x = columnIndex % WorldSize;
                var z = columnIndex / WorldSize;
                MarkShore(x + 1, z);
                MarkShore(x - 1, z);
                MarkShore(x, z + 1);
                MarkShore(x, z - 1);
            }

            basins.Add(basin);
            return true;

            void MarkShore(int x, int z)
            {
                if ((uint)x >= WorldSize || (uint)z >= WorldSize)
                {
                    return;
                }

                var shoreIndex = x + WorldSize * z;
                if (ownership[shoreIndex] == HydrologyColumnOwnership.None)
                {
                    ownership[shoreIndex] =
                        HydrologyColumnOwnership.LakeShore;
                }
            }
        }

        private bool TryMergeChannel(ChannelPlan channel)
        {
            if (!HasMatchingDimensions(channel))
            {
                return false;
            }

            var connectionWetColumns = new HashSet<int>();
            var connectionShoreColumns = new HashSet<int>();
            for (var index = 0; index < channel.Connections.Count; index++)
            {
                var connection = channel.Connections[index];
                connectionWetColumns.Add(connection.BasinWetColumnIndex);
                connectionShoreColumns.Add(connection.ShoreColumnIndex);
            }

            foreach (var columnIndex in channel.ChannelColumnIndices)
            {
                var existing = ownership[columnIndex];
                if (existing == HydrologyColumnOwnership.LakeInterior
                    && !connectionWetColumns.Contains(columnIndex))
                {
                    return false;
                }


                if (existing == HydrologyColumnOwnership.LakeShore
                    && !connectionShoreColumns.Contains(columnIndex))
                {
                    return false;
                }

                if (existing == HydrologyColumnOwnership.RiverChannel
                    || existing == HydrologyColumnOwnership.ConnectionPort)
                {
                    return false;
                }
            }

            if (!TryMergePlan(channel))
            {
                return false;
            }

            foreach (var columnIndex in channel.ChannelColumnIndices)
            {
                ownership[columnIndex] =
                    connectionShoreColumns.Contains(columnIndex)
                    ? HydrologyColumnOwnership.ConnectionPort
                    : HydrologyColumnOwnership.RiverChannel;
            }

            for (var index = 0; index < channel.Connections.Count; index++)
            {
                var connection = channel.Connections[index];
                if (ownership[connection.BasinWetColumnIndex]
                    != HydrologyColumnOwnership.LakeInterior)
                {
                    return false;
                }

            }

            channels.Add(channel);
            return true;
        }

        private bool TryMergePlan(WaterFeaturePlan source)
        {
            foreach (var pair in source.TerrainColumns)
            {
                if (TerrainColumns.TryGetValue(pair.Key, out var existing)
                    && existing.TargetHeightUnits
                        != pair.Value.TargetHeightUnits)
                {
                    return false;
                }

                SetTerrainColumn(pair.Value);
            }

            foreach (var pair in source.SourceCells)
            {
                if (SourceCells.TryGetValue(pair.Key, out var existing)
                    && !existing.Water.Equals(pair.Value.Water))
                {
                    return false;
                }

                AddSourceCell(pair.Value);
            }

            foreach (var cellIndex in source.AllowedWetCellIndices)
            {
                AddAllowedWetCell(DecodeCell(cellIndex));
            }

            foreach (var cellIndex in source.RequiredWetCellIndices)
            {
                AddRequiredWetCell(DecodeCell(cellIndex));
            }

            return true;
        }

        private bool HasMatchingDimensions(WaterFeaturePlan plan) =>
            plan != null
            && plan.WorldSize == WorldSize
            && plan.WorldHeight == WorldHeight;

        private CellCoordinate DecodeCell(int index)
        {
            var y = index / (WorldSize * WorldSize);
            var remainder = index - y * WorldSize * WorldSize;
            var z = remainder / WorldSize;
            var x = remainder - z * WorldSize;
            return new CellCoordinate(x, y, z);
        }
    }
}
