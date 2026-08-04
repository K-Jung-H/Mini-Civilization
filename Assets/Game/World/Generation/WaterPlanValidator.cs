using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    internal enum WaterPlanTerrainViolationType : byte
    {
        None = 0,
        StaleOriginalHeight = 1,
        CutLimitExceeded = 2,
        RaiseLimitExceeded = 3,
        HeightOutsideWorld = 4,
        SourceBlockedBySolid = 5
    }

    internal readonly struct WaterPlanTerrainViolation
    {
        public readonly int ColumnIndex;
        public readonly WaterPlanTerrainViolationType Type;
        public readonly int ActualHeightUnits;
        public readonly int PlannedHeightUnits;

        public WaterPlanTerrainViolation(
            int columnIndex,
            WaterPlanTerrainViolationType type,
            int actualHeightUnits,
            int plannedHeightUnits)
        {
            ColumnIndex = columnIndex;
            Type = type;
            ActualHeightUnits = actualHeightUnits;
            PlannedHeightUnits = plannedHeightUnits;
        }
    }

    internal sealed class WaterPlanValidationResult
    {
        public bool Stabilized { get; }
        public int CompletedWaveCount { get; }
        public IReadOnlyList<int> LeakedCellIndices { get; }
        public IReadOnlyList<int> MissingRequiredCellIndices { get; }
        public IReadOnlyList<WaterPlanTerrainViolation>
            TerrainViolations { get; }
        public WaterPlanRepairAction RecommendedRepairAction { get; }
        public bool IsValid =>
            Stabilized
            && LeakedCellIndices.Count == 0
            && MissingRequiredCellIndices.Count == 0
            && TerrainViolations.Count == 0;

        public WaterPlanValidationResult(
            bool stabilized,
            int completedWaveCount,
            IReadOnlyList<int> leakedCellIndices,
            IReadOnlyList<int> missingRequiredCellIndices,
            IReadOnlyList<WaterPlanTerrainViolation> terrainViolations,
            WaterPlanRepairAction recommendedRepairAction)
        {
            Stabilized = stabilized;
            CompletedWaveCount = completedWaveCount;
            LeakedCellIndices = leakedCellIndices ?? Array.Empty<int>();
            MissingRequiredCellIndices =
                missingRequiredCellIndices ?? Array.Empty<int>();
            TerrainViolations =
                terrainViolations
                ?? Array.Empty<WaterPlanTerrainViolation>();
            RecommendedRepairAction = recommendedRepairAction;
        }
    }

    /// <summary>
    /// Applies a water feature plan to an isolated WorldData clone and runs the
    /// production wave resolver to completion. The source world and plan remain
    /// untouched.
    /// </summary>
    internal static class WaterPlanValidator
    {
        public static WaterPlanValidationResult Validate(
            WorldData sourceWorld,
            WaterFeaturePlan plan,
            int maximumWaves = 0)
        {
            if (sourceWorld == null)
            {
                throw new ArgumentNullException(nameof(sourceWorld));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.WorldSize != sourceWorld.Size
                || plan.WorldHeight != sourceWorld.Height)
            {
                throw new ArgumentException(
                    "The water plan dimensions do not match the world.",
                    nameof(plan));
            }

            maximumWaves = maximumWaves > 0
                ? maximumWaves
                : Math.Max(
                    32,
                    checked((sourceWorld.Size + sourceWorld.Height) * 4));

            var baselinePreview = CloneWorld(sourceWorld);
            var baselineStabilized = RunToStability(
                baselinePreview,
                maximumWaves,
                out _);
            var baselineWetCells = CaptureWetCells(baselinePreview);
            var preview = CloneWorld(sourceWorld);
            var terrainViolations = new List<WaterPlanTerrainViolation>();
            ApplyTerrainPlan(
                sourceWorld,
                preview,
                plan,
                terrainViolations);
            ApplySources(preview, plan, terrainViolations);
            var planStabilized = RunToStability(
                preview,
                maximumWaves,
                out var completedWaveCount);
            var stabilized = baselineStabilized && planStabilized;
            var leaked = FindLeakedCells(
                preview,
                plan.AllowedWetCellIndices,
                baselineWetCells);
            var missing = FindMissingRequiredCells(
                preview,
                plan.RequiredWetCellIndices);
            terrainViolations.Sort(CompareTerrainViolations);
            var recommendedAction = stabilized
                && leaked.Count == 0
                && missing.Count == 0
                && terrainViolations.Count == 0
                    ? WaterPlanRepairAction.None
                    : plan.RepairPolicy.GetAction(plan.RepairAttempt);
            return new WaterPlanValidationResult(
                stabilized,
                completedWaveCount,
                leaked,
                missing,
                terrainViolations,
                recommendedAction);
        }

        private static WorldData CloneWorld(WorldData source)
        {
            var clone = new WorldData(
                source.Size,
                source.Height,
                source.ChunkSizeX,
                source.ChunkSizeY,
                source.ChunkSizeZ,
                source.Seed);
            clone.ConfigureWaterFlow(source.WaterFlowRules);
            for (var y = 0; y < source.Height; y++)
            for (var z = 0; z < source.Size; z++)
            for (var x = 0; x < source.Size; x++)
            {
                clone.SetCellBulk(x, y, z, source.GetCell(x, y, z));
            }

            clone.RebuildAllSurfaceColumns();
            return clone;
        }

        private static bool RunToStability(
            WorldData preview,
            int maximumWaves,
            out int completedWaveCount)
        {
            preview.RebuildAllSurfaceColumns();
            preview.WaterSources.InitializeFromGeneratedWorld(preview);
            WaterFlowSolver.PrepareGeneratedWorld(preview);
            var flowState = new WaterFlowState(
                preview,
                Array.Empty<WaterBody>());
            var resolver = new WaterFlowResolver(flowState.CellCount);
            resolver.RestoreFrontier(
                preview,
                flowState,
                preview.WaterFlowSchedule.FrontierCellIndices);
            var parameters = new WaterFlowParameters(preview.WaterFlowRules);
            completedWaveCount = 0;
            while (resolver.HasWork && completedWaveCount < maximumWaves)
            {
                resolver.Step(
                    preview,
                    flowState,
                    parameters,
                    flowState.CellCount,
                    out _);
                completedWaveCount++;
            }

            return !resolver.HasWork;
        }

        private static HashSet<int> CaptureWetCells(WorldData world)
        {
            var result = new HashSet<int>();
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                if (world.GetCell(x, y, z).HasWater)
                {
                    result.Add(WorldIndex.EncodeCell(world, x, y, z));
                }
            }

            return result;
        }

        private static void ApplyTerrainPlan(
            WorldData source,
            WorldData preview,
            WaterFeaturePlan plan,
            List<WaterPlanTerrainViolation> violations)
        {
            var maximumWorldHeight =
                source.Height * WorldGrid.HeightStepsPerCell;
            foreach (var pair in plan.TerrainColumns)
            {
                var column = pair.Value;
                var actualHeight = ResolveSolidHeight(
                    source,
                    column.X,
                    column.Z);
                if (actualHeight != column.OriginalHeightUnits)
                {
                    violations.Add(new WaterPlanTerrainViolation(
                        pair.Key,
                        WaterPlanTerrainViolationType.StaleOriginalHeight,
                        actualHeight,
                        column.OriginalHeightUnits));
                }

                if (column.CutUnits > column.MaximumCutUnits)
                {
                    violations.Add(new WaterPlanTerrainViolation(
                        pair.Key,
                        WaterPlanTerrainViolationType.CutLimitExceeded,
                        actualHeight,
                        column.TargetHeightUnits));
                }

                if (column.RaiseUnits > column.MaximumRaiseUnits)
                {
                    violations.Add(new WaterPlanTerrainViolation(
                        pair.Key,
                        WaterPlanTerrainViolationType.RaiseLimitExceeded,
                        actualHeight,
                        column.TargetHeightUnits));
                }

                if (column.TargetHeightUnits > maximumWorldHeight)
                {
                    violations.Add(new WaterPlanTerrainViolation(
                        pair.Key,
                        WaterPlanTerrainViolationType.HeightOutsideWorld,
                        actualHeight,
                        column.TargetHeightUnits));
                }

                ApplyColumnHeight(
                    preview,
                    column.X,
                    column.Z,
                    Math.Clamp(
                        column.TargetHeightUnits,
                        0,
                        maximumWorldHeight));
            }
        }

        private static void ApplySources(
            WorldData preview,
            WaterFeaturePlan plan,
            List<WaterPlanTerrainViolation> violations)
        {
            foreach (var pair in plan.SourceCells)
            {
                var source = pair.Value;
                var coordinate = source.Coordinate;
                var cell = preview.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (cell.SolidFill >= WorldGrid.HeightStepsPerCell)
                {
                    violations.Add(new WaterPlanTerrainViolation(
                        coordinate.X + preview.Size * coordinate.Z,
                        WaterPlanTerrainViolationType.SourceBlockedBySolid,
                        ResolveSolidHeight(
                            preview,
                            coordinate.X,
                            coordinate.Z),
                        (coordinate.Y + 1)
                        * WorldGrid.HeightStepsPerCell));
                    continue;
                }

                cell.Water = source.Water;
                cell.Normalize();
                preview.SetCellBulk(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    cell);
            }
        }

        private static void ApplyColumnHeight(
            WorldData world,
            int x,
            int z,
            int heightUnits)
        {
            for (var y = 0; y < world.Height; y++)
            {
                var cell = world.GetCell(x, y, z);
                cell.SolidFill = (byte)Math.Clamp(
                    heightUnits - y * WorldGrid.HeightStepsPerCell,
                    0,
                    WorldGrid.HeightStepsPerCell);
                cell.Normalize();
                world.SetCellBulk(x, y, z, cell);
            }
        }

        private static int ResolveSolidHeight(
            WorldData world,
            int x,
            int z)
        {
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var fill = world.GetCell(x, y, z).SolidFill;
                if (fill > 0)
                {
                    return y * WorldGrid.HeightStepsPerCell + fill;
                }
            }

            return 0;
        }

        private static List<int> FindLeakedCells(
            WorldData preview,
            IReadOnlyCollection<int> allowedCells,
            HashSet<int> baselineWetCells)
        {
            var allowed = allowedCells as HashSet<int>
                ?? new HashSet<int>(allowedCells);
            var result = new List<int>();
            for (var y = 0; y < preview.Height; y++)
            for (var z = 0; z < preview.Size; z++)
            for (var x = 0; x < preview.Size; x++)
            {
                if (!preview.GetCell(x, y, z).HasWater)
                {
                    continue;
                }

                var index = WorldIndex.EncodeCell(preview, x, y, z);
                if (!baselineWetCells.Contains(index)
                    && !allowed.Contains(index))
                {
                    result.Add(index);
                }
            }

            result.Sort();
            return result;
        }

        private static List<int> FindMissingRequiredCells(
            WorldData preview,
            IReadOnlyCollection<int> requiredCells)
        {
            var result = new List<int>();
            foreach (var index in requiredCells)
            {
                var coordinate = WorldIndex.DecodeCell(preview, index);
                if (!preview.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z)
                    .HasWater)
                {
                    result.Add(index);
                }
            }

            result.Sort();
            return result;
        }

        private static int CompareTerrainViolations(
            WaterPlanTerrainViolation left,
            WaterPlanTerrainViolation right)
        {
            var columnComparison = left.ColumnIndex.CompareTo(right.ColumnIndex);
            return columnComparison != 0
                ? columnComparison
                : left.Type.CompareTo(right.Type);
        }
    }
}
