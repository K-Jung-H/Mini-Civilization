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
        public int FlowAccumulation { get; }

        public HydrologyBasin(
            int id,
            IReadOnlyList<int> columns,
            int spillHeightUnits,
            int outletColumnIndex,
            int minimumSeaDistance,
            int maximumDepthUnits,
            int flowAccumulation)
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
            FlowAccumulation = Math.Max(1, flowAccumulation);
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
        private readonly int[] basinIds;
        private readonly int[] spillHeightUnits;
        private readonly int[] receiverColumnIndices;
        private readonly int[] flowAccumulation;
        private readonly int[] seaDistances;
        private readonly bool[] seaConnected;
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
            basinIds = new int[terrainHeights.Count];
            spillHeightUnits = new int[terrainHeights.Count];
            receiverColumnIndices = new int[terrainHeights.Count];
            flowAccumulation = new int[terrainHeights.Count];
            seaDistances = new int[terrainHeights.Count];
            seaConnected = new bool[terrainHeights.Count];
            Array.Fill(basinIds, -1);
            Array.Fill(receiverColumnIndices, -1);
            Array.Fill(seaDistances, int.MaxValue);

            for (var index = 0; index < terrainHeights.Count; index++)
            {
                var height = Math.Max(0, terrainHeights[index]);
                terrainHeightUnits[index] = height;
                filledHeightUnits[index] = height;
                spillHeightUnits[index] = height;
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
        public int GetBasinId(int index) => basinIds[ValidateIndex(index)];
        public int GetSpillHeightUnits(int index) =>
            spillHeightUnits[ValidateIndex(index)];
        public int GetReceiverColumnIndex(int index) =>
            receiverColumnIndices[ValidateIndex(index)];
        public int GetFlowAccumulation(int index) =>
            flowAccumulation[ValidateIndex(index)];
        public int GetSeaDistance(int index) =>
            seaDistances[ValidateIndex(index)];
        public bool IsSeaConnected(int index) =>
            seaConnected[ValidateIndex(index)];

        internal void SetFilledHeightUnits(int index, int value) =>
            filledHeightUnits[ValidateIndex(index)] = Math.Max(0, value);
        internal void SetBasin(int index, int basinId, int spillHeight) 
        {
            index = ValidateIndex(index);
            basinIds[index] = basinId;
            spillHeightUnits[index] = Math.Max(0, spillHeight);
        }

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
        internal void SetSeaConnected(int index, bool value) =>
            seaConnected[ValidateIndex(index)] = value;
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
        public readonly int OriginalHeightUnits;
        public readonly int TargetHeightUnits;
        public readonly int MaximumCutUnits;
        public readonly int MaximumRaiseUnits;

        public int CutUnits => Math.Max(
            0,
            OriginalHeightUnits - TargetHeightUnits);
        public int RaiseUnits => Math.Max(
            0,
            TargetHeightUnits - OriginalHeightUnits);

        public PlannedTerrainColumn(
            int x,
            int z,
            int originalHeightUnits,
            int targetHeightUnits,
            int maximumCutUnits,
            int maximumRaiseUnits)
        {
            X = x;
            Z = z;
            OriginalHeightUnits = Math.Max(0, originalHeightUnits);
            TargetHeightUnits = Math.Max(0, targetHeightUnits);
            MaximumCutUnits = Math.Max(0, maximumCutUnits);
            MaximumRaiseUnits = Math.Max(0, maximumRaiseUnits);
        }
    }

    internal readonly struct PlannedWaterCell
    {
        public readonly CellCoordinate Coordinate;
        public readonly WaterCellData Water;

        public PlannedWaterCell(
            CellCoordinate coordinate,
            WaterFlowDirectionMask direction)
        {
            Coordinate = coordinate;
            Water = new WaterCellData
            {
                Amount = WaterAmount.Full,
                Role = WaterCellRole.Source,
                Direction = direction
            };
        }
    }

    internal enum WaterPlanRepairAction : byte
    {
        None = 0,
        LowerSurface = 1,
        DeepenBed = 2,
        ExpandAllowedArea = 3,
        RaiseBankWithinLimit = 4,
        Reroute = 5,
        Reject = 6
    }

    internal readonly struct WaterPlanRepairPolicy
    {
        public readonly int MaximumAttempts;

        public static WaterPlanRepairPolicy Default => new(5);

        public WaterPlanRepairPolicy(int maximumAttempts)
        {
            MaximumAttempts = Math.Max(0, maximumAttempts);
        }

        public WaterPlanRepairAction GetAction(int attempt)
        {
            if (attempt < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt));
            }

            if (attempt >= MaximumAttempts)
            {
                return WaterPlanRepairAction.Reject;
            }

            return attempt switch
            {
                0 => WaterPlanRepairAction.LowerSurface,
                1 => WaterPlanRepairAction.DeepenBed,
                2 => WaterPlanRepairAction.ExpandAllowedArea,
                3 => WaterPlanRepairAction.RaiseBankWithinLimit,
                _ => WaterPlanRepairAction.Reroute
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
        private int repairAttempt;

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
        public WaterPlanRepairPolicy RepairPolicy { get; }
        public int RepairAttempt => repairAttempt;

        protected WaterFeaturePlan(
            int worldSize,
            int worldHeight,
            WaterPlanRepairPolicy repairPolicy)
        {
            if (worldSize <= 0 || worldHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(worldSize));
            }

            WorldSize = worldSize;
            WorldHeight = worldHeight;
            RepairPolicy = repairPolicy;
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

        public WaterPlanRepairAction GetNextRepairAction()
        {
            var action = RepairPolicy.GetAction(repairAttempt);
            if (action != WaterPlanRepairAction.Reject)
            {
                repairAttempt++;
            }

            return action;
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

    internal readonly struct ChannelSectionPlan
    {
        public readonly int ColumnIndex;
        public readonly int WidthCells;
        public readonly int CenterDepthUnits;
        public readonly int SurfaceHeightUnits;
        public readonly int FlowAccumulation;

        public ChannelSectionPlan(
            int columnIndex,
            int widthCells,
            int centerDepthUnits,
            int surfaceHeightUnits,
            int flowAccumulation)
        {
            ColumnIndex = columnIndex;
            WidthCells = Math.Max(1, widthCells);
            CenterDepthUnits = Math.Max(1, centerDepthUnits);
            SurfaceHeightUnits = Math.Max(1, surfaceHeightUnits);
            FlowAccumulation = Math.Max(1, flowAccumulation);
        }
    }

    internal enum BasinConnectionType : byte
    {
        Inlet = 0,
        Outlet = 1
    }

    internal readonly struct BasinConnectionPort
    {
        public readonly int BasinId;
        public readonly BasinConnectionType Type;
        public readonly int BasinWetColumnIndex;
        public readonly int ShoreColumnIndex;
        public readonly int ExternalColumnIndex;
        public readonly int InterfaceSurfaceHeightUnits;
        public readonly WaterFlowDirectionMask Direction;

        public BasinConnectionPort(
            int basinId,
            BasinConnectionType type,
            int basinWetColumnIndex,
            int shoreColumnIndex,
            int externalColumnIndex,
            int interfaceSurfaceHeightUnits,
            WaterFlowDirectionMask direction)
        {
            BasinId = basinId;
            Type = type;
            BasinWetColumnIndex = basinWetColumnIndex;
            ShoreColumnIndex = shoreColumnIndex;
            ExternalColumnIndex = externalColumnIndex;
            InterfaceSurfaceHeightUnits = Math.Max(
                1,
                interfaceSurfaceHeightUnits);
            Direction = direction;
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

    internal enum RiverChannelArchetype : byte
    {
        MountainDynamic = 0,
        LowlandDynamic = 1,
        SteppedDynamic = 2,
        SourceChannel = 3
    }

    internal sealed class ChannelPlan : WaterFeaturePlan
    {
        private readonly List<int> centerlineColumnIndices = new();
        private readonly Dictionary<int, ChannelSectionPlan> sections = new();
        private readonly HashSet<int> channelColumnIndices = new();
        private readonly List<BasinConnectionPort> connections = new();

        public IReadOnlyList<int> CenterlineColumnIndices =>
            centerlineColumnIndices;
        public IReadOnlyDictionary<int, ChannelSectionPlan> Sections =>
            sections;
        public IReadOnlyCollection<int> ChannelColumnIndices =>
            channelColumnIndices;
        public IReadOnlyList<BasinConnectionPort> Connections => connections;
        public RiverChannelArchetype Archetype { get; }

        public ChannelPlan(
            int worldSize,
            int worldHeight,
            RiverChannelArchetype archetype,
            WaterPlanRepairPolicy repairPolicy) : base(
                worldSize,
                worldHeight,
                repairPolicy)
        {
            Archetype = archetype;
        }

        public void AddSection(in ChannelSectionPlan section)
        {
            if ((uint)section.ColumnIndex
                >= (uint)(WorldSize * WorldSize))
            {
                throw new ArgumentOutOfRangeException(nameof(section));
            }

            if (!sections.ContainsKey(section.ColumnIndex))
            {
                centerlineColumnIndices.Add(section.ColumnIndex);
            }

            sections[section.ColumnIndex] = section;
            channelColumnIndices.Add(section.ColumnIndex);
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
        public int SpillHeightUnits { get; }
        public int WaterSurfaceHeightUnits { get; }
        public int OutletColumnIndex { get; }
        public IReadOnlyList<int> WetColumnIndices => wetColumnIndices;

        public BasinPlan(
            int worldSize,
            int worldHeight,
            int basinId,
            int spillHeightUnits,
            int waterSurfaceHeightUnits,
            int outletColumnIndex,
            WaterPlanRepairPolicy repairPolicy) : base(
                worldSize,
                worldHeight,
                repairPolicy)
        {
            BasinId = basinId;
            SpillHeightUnits = Math.Max(0, spillHeightUnits);
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
        private readonly List<BasinConnectionPort> connections = new();
        private readonly HydrologyColumnOwnership[] ownership;

        public IReadOnlyList<BasinPlan> Basins => basins;
        public IReadOnlyList<ChannelPlan> Channels => channels;
        public IReadOnlyList<BasinConnectionPort> Connections => connections;
        public IReadOnlyList<HydrologyColumnOwnership> Ownership => ownership;

        private HydrologyFeaturePlan(int worldSize, int worldHeight) : base(
            worldSize,
            worldHeight,
            WaterPlanRepairPolicy.Default)
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

                connections.Add(connection);
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
