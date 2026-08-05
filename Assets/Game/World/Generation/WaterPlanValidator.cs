using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    internal sealed class WaterPlanValidationResult
    {
        public IReadOnlyList<int> LeakedCellIndices { get; }
        public bool IsValid { get; }

        public WaterPlanValidationResult(
            bool stabilized,
            IReadOnlyList<int> leakedCellIndices,
            IReadOnlyList<int> missingRequiredCellIndices)
        {
            LeakedCellIndices = leakedCellIndices ?? Array.Empty<int>();
            IsValid = stabilized
                && LeakedCellIndices.Count == 0
                && (missingRequiredCellIndices?.Count ?? 0) == 0;
        }
    }

    internal sealed class WaterPlanValidationContext
    {
        internal WorldData SourceWorld { get; }
        internal int MaximumWaves { get; }
        internal HashSet<int> BaselineWetCells { get; }

        internal WaterPlanValidationContext(
            WorldData sourceWorld,
            int maximumWaves,
            HashSet<int> baselineWetCells)
        {
            SourceWorld = sourceWorld;
            MaximumWaves = maximumWaves;
            BaselineWetCells = baselineWetCells;
        }
    }

    /// <summary>
    /// Applies a water feature plan to an isolated WorldData clone and runs the
    /// production wave resolver to completion. The source world and plan remain
    /// untouched.
    /// </summary>
    internal static class WaterPlanValidator
    {
        public static WaterPlanValidationContext CreateContext(
            WorldData sourceWorld)
        {
            if (sourceWorld == null)
            {
                throw new ArgumentNullException(nameof(sourceWorld));
            }

            var maximumWaves = Math.Max(
                32,
                checked((sourceWorld.Size + sourceWorld.Height) * 4));
            var baselinePreview = CloneWorld(sourceWorld);
            if (!RunToStability(baselinePreview, maximumWaves))
            {
                throw new InvalidOperationException(
                    "Base water state did not stabilize during global validation.");
            }

            return new WaterPlanValidationContext(
                sourceWorld,
                maximumWaves,
                CaptureWetCells(baselinePreview));
        }

        public static WaterPlanValidationResult Validate(
            WorldData sourceWorld,
            WaterFeaturePlan plan,
            WaterPlanValidationContext context)
        {
            if (sourceWorld == null)
            {
                throw new ArgumentNullException(nameof(sourceWorld));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (context == null
                || !ReferenceEquals(context.SourceWorld, sourceWorld))
            {
                throw new ArgumentException(
                    "The validation context belongs to another world.",
                    nameof(context));
            }

            if (plan.WorldSize != sourceWorld.Size
                || plan.WorldHeight != sourceWorld.Height)
            {
                throw new ArgumentException(
                    "The water plan dimensions do not match the world.",
                    nameof(plan));
            }

            var preview = CloneWorld(sourceWorld);
            ApplyTerrainPlan(preview, plan);
            ApplySources(preview, plan);
            var planMaximumWaves = Math.Max(
                context.MaximumWaves,
                checked(
                    plan.AllowedWetCellIndices.Count
                    + sourceWorld.Height * 2));
            var stabilized = RunToStability(
                preview,
                planMaximumWaves);
            var leaked = FindLeakedCells(
                preview,
                plan.AllowedWetCellIndices,
                context.BaselineWetCells);
            var missing = FindMissingRequiredCells(
                preview,
                plan.RequiredWetCellIndices);
            return new WaterPlanValidationResult(
                stabilized,
                leaked,
                missing);
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

            clone.Cache.RebuildAllSurfaceHeights();
            return clone;
        }

        private static bool RunToStability(
            WorldData preview,
            int maximumWaves)
        {
            preview.Cache.RebuildAllSurfaceHeights();
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
            var completedWaveCount = 0;
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
            WorldData preview,
            WaterFeaturePlan plan)
        {
            var maximumWorldHeight =
                preview.Height * WorldGrid.HeightStepsPerCell;
            foreach (var pair in plan.TerrainColumns)
            {
                var column = pair.Value;
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
            WaterFeaturePlan plan)
        {
            foreach (var pair in plan.SourceCells)
            {
                var source = pair.Value;
                var coordinate = source.Coordinate;
                var cell = preview.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (cell.Terrain.SolidHeight >= WorldGrid.HeightStepsPerCell)
                {
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
                cell.Terrain.SolidHeight = (byte)Math.Clamp(
                    heightUnits - y * WorldGrid.HeightStepsPerCell,
                    0,
                    WorldGrid.HeightStepsPerCell);
                cell.Normalize();
                world.SetCellBulk(x, y, z, cell);
            }
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

    }
}
