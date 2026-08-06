using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal static class InlandLakePlanner
    {
        public static IReadOnlyList<BasinPlan> BuildPlans(
            WorldData validationWorld,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            int worldSeed,
            WaterPlanValidationContext validationContext)
        {
            if (validationWorld == null)
            {
                throw new ArgumentNullException(nameof(validationWorld));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            if (hydrology.Size != validationWorld.Size)
            {
                throw new ArgumentException(
                    "Hydrology and validation world sizes do not match.",
                    nameof(hydrology));
            }

            if (settings.LakeCount <= 0)
            {
                return Array.Empty<BasinPlan>();
            }

            var seed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "inland-basin-lakes");
            var candidates = CreateCandidates(
                validationWorld,
                settings,
                hydrology,
                seed);

            candidates.Sort(CompareCandidates);
            var accepted = new List<BasinPlan>(settings.LakeCount);
            for (var index = 0;
                 index < candidates.Count
                 && accepted.Count < settings.LakeCount;
                 index++)
            {
                var plan = candidates[index].Plan;
                var candidatePlans = new List<BasinPlan>(accepted)
                {
                    plan
                };
                if (!HydrologyFeaturePlan.TryCreate(
                        validationWorld.Size,
                        validationWorld.Height,
                        candidatePlans,
                        Array.Empty<ChannelPlan>(),
                        out var candidateFeaturePlan)
                    || !WaterPlanValidator.Validate(
                        validationWorld,
                        candidateFeaturePlan,
                        validationContext).IsValid)
                {
                    continue;
                }

                accepted.Add(plan);
            }

            return accepted;
        }

        private static List<LakeCandidate> CreateCandidates(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            int seed)
        {
            var candidateByBasin = new LakeCandidate[hydrology.Basins.Count];
            var hasCandidate = new bool[hydrology.Basins.Count];
            Parallel.For(0, hydrology.Basins.Count, basinIndex =>
            {
                if (TryCreateCandidate(
                        world,
                        settings,
                        hydrology,
                        hydrology.Basins[basinIndex],
                        seed,
                        out var candidate))
                {
                    candidateByBasin[basinIndex] = candidate;
                    hasCandidate[basinIndex] = true;
                }
            });

            var candidates = new List<LakeCandidate>();
            for (var basinIndex = 0;
                 basinIndex < candidateByBasin.Length;
                 basinIndex++)
            {
                if (hasCandidate[basinIndex])
                {
                    candidates.Add(candidateByBasin[basinIndex]);
                }
            }

            return candidates;
        }

        public static void ApplyPlans(
            IReadOnlyList<BasinPlan> plans,
            int size,
            int[] waterSurfaces,
            WaterRole[] waterRoles,
            WaterType[] waterTypes,
            SurfaceType[] waterBedSurfaces)
        {
            if (plans == null)
            {
                throw new ArgumentNullException(nameof(plans));
            }

            for (var planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                var plan = plans[planIndex];
                for (var wetIndex = 0;
                     wetIndex < plan.WetColumnIndices.Count;
                     wetIndex++)
                {
                    var columnIndex = plan.WetColumnIndices[wetIndex];
                    if ((uint)columnIndex >= (uint)(size * size))
                    {
                        throw new InvalidOperationException(
                            "A lake plan contains an invalid column index.");
                    }

                    waterSurfaces[columnIndex] = Math.Max(
                        waterSurfaces[columnIndex],
                        plan.WaterSurfaceHeightUnits);
                    waterRoles[columnIndex] = WaterRole.Source;
                    waterTypes[columnIndex] = plan.Type;
                    waterBedSurfaces[columnIndex] = SurfaceType.Lakebed;
                }
            }
        }

        private static bool TryCreateCandidate(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            HydrologyBasin basin,
            int seed,
            out LakeCandidate candidate)
        {
            candidate = default;
            if (basin.MinimumSeaDistance
                    < settings.MinimumInlandLakeDistance
                || basin.MaximumDepthUnits
                    < settings.MinimumInlandLakeDepthSteps)
            {
                return false;
            }

            var heightStep = WorldGrid.HeightStepsPerCell;
            var waterSurface = basin.SpillHeightUnits
                / heightStep
                * heightStep;
            var maximumWorldHeight = world.Height * heightStep;
            if (waterSurface <= 0 || waterSurface >= maximumWorldHeight)
            {
                return false;
            }

            var wetColumns = new List<int>(basin.ColumnIndices.Count);
            var maximumDepth = 0;
            var accumulation = 1;
            for (var index = 0;
                 index < basin.ColumnIndices.Count;
                 index++)
            {
                var columnIndex = basin.ColumnIndices[index];
                var terrainHeight =
                    hydrology.GetTerrainHeightUnits(columnIndex);
                if (terrainHeight >= waterSurface)
                {
                    continue;
                }

                wetColumns.Add(columnIndex);
                maximumDepth = Math.Max(
                    maximumDepth,
                    waterSurface - terrainHeight);
                accumulation = Math.Max(
                    accumulation,
                    hydrology.GetFlowAccumulation(columnIndex));
            }

            if (wetColumns.Count < settings.MinimumInlandLakeArea
                || maximumDepth < settings.MinimumInlandLakeDepthSteps)
            {
                return false;
            }

            var plan = new BasinPlan(
                world.Size,
                world.Height,
                basin.Id,
                waterSurface,
                basin.OutletColumnIndex,
                wetColumns.Count <= settings.PondMaximumArea
                    ? WaterType.Pond
                    : WaterType.Lake);
            for (var index = 0; index < wetColumns.Count; index++)
            {
                var columnIndex = wetColumns[index];
                plan.AddWetColumn(columnIndex);
                AddSourceCells(
                    plan,
                    world.Size,
                    columnIndex,
                    hydrology.GetTerrainHeightUnits(columnIndex),
                    waterSurface);
            }

            var representative = wetColumns[0];
            var x = representative % world.Size;
            var z = representative / world.Size;
            var score = wetColumns.Count * 32f
                + maximumDepth * 12f
                + (float)Math.Log(accumulation + 1f, 2d) * 8f
                + basin.MinimumSeaDistance * 2f
                + DeterministicNoise.Value01(x, z, seed);
            candidate = new LakeCandidate(plan, score);
            return true;
        }

        private static void AddSourceCells(
            BasinPlan plan,
            int size,
            int columnIndex,
            int terrainHeight,
            int waterSurface)
        {
            var x = columnIndex % size;
            var z = columnIndex / size;
            var firstY = Math.Max(
                0,
                terrainHeight / WorldGrid.HeightStepsPerCell);
            var lastY = Math.Min(
                plan.WorldHeight - 1,
                (waterSurface - 1) / WorldGrid.HeightStepsPerCell);
            for (var y = firstY; y <= lastY; y++)
            {
                if (terrainHeight
                    >= (y + 1) * WorldGrid.HeightStepsPerCell)
                {
                    continue;
                }

                plan.AddSourceCell(new PlannedWaterCell(
                    new CellCoordinate(x, y, z),
                    FlowDirection.None,
                    plan.Type));
            }
        }

        private static int CompareCandidates(
            LakeCandidate left,
            LakeCandidate right)
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Plan.BasinId.CompareTo(right.Plan.BasinId);
        }

        private readonly struct LakeCandidate
        {
            public readonly BasinPlan Plan;
            public readonly float Score;

            public LakeCandidate(BasinPlan plan, float score)
            {
                Plan = plan;
                Score = score;
            }
        }
    }
}
