using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    internal sealed class WaterPlanValidationResult
    {
        public IReadOnlyList<CellCoordinate> LeakedCells { get; }
        public bool IsValid { get; }

        public WaterPlanValidationResult(
            bool stabilized,
            IReadOnlyList<CellCoordinate> leakedCells,
            IReadOnlyList<CellCoordinate> missingRequiredCells)
        {
            LeakedCells = leakedCells ?? Array.Empty<CellCoordinate>();
            IsValid = stabilized
                && LeakedCells.Count == 0
                && (missingRequiredCells?.Count ?? 0) == 0;
        }
    }

    internal sealed class WaterPlanValidationContext
    {
        internal WorldData SourceWorld { get; }
        internal int MaximumWaves { get; }
        internal HashSet<CellCoordinate> BaselineWetCells { get; }

        internal WaterPlanValidationContext(
            WorldData sourceWorld,
            int maximumWaves,
            HashSet<CellCoordinate> baselineWetCells)
        {
            SourceWorld = sourceWorld;
            MaximumWaves = maximumWaves;
            BaselineWetCells = baselineWetCells;
        }
    }

    internal static class WaterPlanValidator
    {
        public static WaterPlanValidationContext CreateContext(
            WorldBuildData build) => CreateContext(CreateSourceWorld(build));

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
            var baselinePreview = WaterPreview.Create(sourceWorld);
            if (!RunToStability(baselinePreview, maximumWaves))
            {
                throw new InvalidOperationException(
                    "Base water state did not stabilize during global validation.");
            }

            return new WaterPlanValidationContext(
                sourceWorld,
                maximumWaves,
                CaptureWetCells(baselinePreview.Data));
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

            var preview = WaterPreview.Create(sourceWorld);
            ApplyTerrainPlan(preview, plan);
            ApplySources(preview, plan);
            var planMaximumWaves = Math.Max(
                context.MaximumWaves,
                checked(
                    plan.AllowedWetCells.Count
                    + sourceWorld.Height * 2));
            var stabilized = RunToStability(
                preview,
                planMaximumWaves);
            var leaked = FindLeakedCells(
                preview.Data,
                plan.AllowedWetCells,
                context.BaselineWetCells);
            var missing = FindMissingRequiredCells(
                preview.Data,
                plan.RequiredWetCells);
            return new WaterPlanValidationResult(
                stabilized,
                leaked,
                missing);
        }

        private static bool RunToStability(
            WaterPreview preview,
            int maximumWaves)
        {
            var world = preview.Data;
            preview.SurfaceCache.RebuildAll();
            WaterFlowSolver.PrepareGeneratedWorld(world);
            var flowState = new WaterFlowState(
                world,
                WaterBodyResolver.Resolve(world, preview.SurfaceCache));
            var resolver = new WaterFlowResolver(flowState.CellCount);
            resolver.RestoreFrontier(
                world,
                flowState,
                world.WaterFlowSchedule.FrontierCells);
            var parameters = new WaterFlowParameters(world.WaterFlowRules);
            var completedWaveCount = 0;
            while (resolver.HasWork && completedWaveCount < maximumWaves)
            {
                resolver.Step(
                    world,
                    flowState,
                    parameters,
                    flowState.CellCount,
                    out _);
                completedWaveCount++;
            }

            return !resolver.HasWork;
        }

        private static HashSet<CellCoordinate> CaptureWetCells(WorldData world)
        {
            var result = new HashSet<CellCoordinate>();
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                if (world.GetCell(x, y, z).HasWater)
                {
                    result.Add(new CellCoordinate(x, y, z));
                }
            }

            return result;
        }

        private static void ApplyTerrainPlan(
            WaterPreview preview,
            WaterFeaturePlan plan)
        {
            var maximumWorldHeight =
                preview.Data.Height * WorldGrid.HeightStepsPerCell;
            foreach (var pair in plan.TerrainColumns)
            {
                var column = pair.Value;
                ApplyColumnHeight(
                    preview.Data,
                    column.X,
                    column.Z,
                    Math.Clamp(
                        column.TargetHeightUnits,
                        0,
                        maximumWorldHeight));
            }
        }

        private static void ApplySources(
            WaterPreview preview,
            WaterFeaturePlan plan)
        {
            var world = preview.Data;
            foreach (var pair in plan.SourceCells)
            {
                var source = pair.Value;
                var coordinate = source.Coordinate;
                var cell = world.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (cell.Terrain.SolidHeight >= WorldGrid.HeightStepsPerCell)
                {
                    continue;
                }

                cell.Water = source.Water;
                cell.Normalize();
                world.SetCellBulk(
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

        private static List<CellCoordinate> FindLeakedCells(
            WorldData preview,
            IReadOnlyCollection<CellCoordinate> allowedCells,
            HashSet<CellCoordinate> baselineWetCells)
        {
            var allowed = allowedCells as HashSet<CellCoordinate>
                ?? new HashSet<CellCoordinate>(allowedCells);
            var result = new List<CellCoordinate>();
            for (var y = 0; y < preview.Height; y++)
            for (var z = 0; z < preview.Size; z++)
            for (var x = 0; x < preview.Size; x++)
            {
                if (!preview.GetCell(x, y, z).HasWater)
                {
                    continue;
                }

                var coordinate = new CellCoordinate(x, y, z);
                if (!baselineWetCells.Contains(coordinate)
                    && !allowed.Contains(coordinate))
                {
                    result.Add(coordinate);
                }
            }

            result.Sort();
            return result;
        }

        private static List<CellCoordinate> FindMissingRequiredCells(
            WorldData preview,
            IReadOnlyCollection<CellCoordinate> requiredCells)
        {
            var result = new List<CellCoordinate>();
            foreach (var coordinate in requiredCells)
            {
                if (!preview.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z)
                    .HasWater)
                {
                    result.Add(coordinate);
                }
            }

            result.Sort();
            return result;
        }

        private static WorldData CreateSourceWorld(WorldBuildData build)
        {
            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            var input = build.Input;
            var world = new WorldData(input.Settings);

            for (var z = 0; z < build.Size; z++)
            for (var x = 0; x < build.Size; x++)
            {
                var index = build.ToColumnIndex(x, z);
                WriteSourceColumn(
                    world,
                    x,
                    z,
                    build.SolidHeights[index],
                    build.WaterSurfaces[index],
                    build.WaterRoles[index],
                    build.WaterTypes[index]);
            }

            return world;
        }

        private static void WriteSourceColumn(
            WorldData world,
            int x,
            int z,
            int solidHeightUnits,
            int waterSurfaceUnits,
            WaterRole waterRole,
            WaterType waterType)
        {
            solidHeightUnits = Math.Clamp(
                solidHeightUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);
            waterSurfaceUnits = Math.Clamp(
                waterSurfaceUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);
            for (var y = 0; y < world.Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var solidFill = (byte)Math.Clamp(
                    solidHeightUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var waterFill = (byte)Math.Clamp(
                    waterSurfaceUnits - baseUnits - solidFill,
                    0,
                    WorldGrid.HeightStepsPerCell - solidFill);
                var cell = new CellData
                {
                    Terrain = new TerrainData
                    {
                        SolidHeight = solidFill
                    }
                };
                if (waterFill > 0 && waterRole == WaterRole.Source)
                {
                    cell.Water = new WaterData
                    {
                        Amount = WaterAmount.FromRenderFill(
                            waterFill,
                            WorldGrid.HeightStepsPerCell - solidFill),
                        Role = waterRole,
                        Type = waterType
                    };
                }

                world.SetCellBulk(x, y, z, cell);
            }
        }

        private sealed class WaterPreview
        {
            private WaterPreview(WorldData data, SurfaceCache surfaceCache)
            {
                Data = data;
                SurfaceCache = surfaceCache;
            }

            public WorldData Data { get; }
            public SurfaceCache SurfaceCache { get; }

            public static WaterPreview Create(WorldData source)
            {
                var data = new WorldData(source.Settings);
                foreach (var column in source.EnumerateLoadedColumns())
                {
                    data.EnsureColumnLoaded(column.Coordinate);
                }

                for (var y = 0; y < source.Height; y++)
                for (var z = 0; z < source.Size; z++)
                for (var x = 0; x < source.Size; x++)
                {
                    var sourceCell = source.GetCell(x, y, z);
                    if (!sourceCell.HasTerrain && !sourceCell.HasWater)
                    {
                        continue;
                    }

                    data.SetCellBulk(x, y, z, new CellData
                    {
                        Terrain = new TerrainData
                        {
                            SolidHeight = sourceCell.Terrain.SolidHeight
                        },
                        Water = sourceCell.Water
                    });
                }

                var surfaceCache = new SurfaceCache(data);
                return new WaterPreview(data, surfaceCache);
            }
        }

    }
}
