using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class WaterBrushFactory
    {
        private readonly HydrologyFeatureSettingsData settings;
        private readonly IClimatePatternMapReader climate;
        private readonly int basinSeed;
        private readonly int riverSeed;

        public WaterBrushFactory(HydrologyFeatureSettingsData settings, IClimatePatternMapReader climate = null)
        {
            this.climate = climate;
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            basinSeed = WaterMapDrawingMath.DeriveSeed(
                settings.World.Seed,
                "water-map-basin");
            riverSeed = WaterMapDrawingMath.DeriveSeed(
                settings.World.Seed,
                "water-map-river");
        }

        public int BasinPaddingCells => checked(
            settings.Basins.MaximumReachCells
            + settings.Basins.ShoreTransitionCells);

        public int BasinCandidateSpacingCells =>
            settings.Basins.CandidateLatticeSpacingCells;

        public int RiverPaddingCells => checked((int)MathF.Ceiling(
            settings.River.AnchorJitterCells
            + GetMaximumRiverTreeReachCells()
            + RiverWaterBrush.InfluenceRadius(settings.River.Width.Maximum)));

        public int RiverCandidateSpacingCells =>
            settings.River.CandidateLatticeSpacingCells;

        public bool IsBasinCandidate(int gridX, int gridZ)
        {
            var chance = WaterMapDrawingMath.Value01(gridX, gridZ, basinSeed);
            return chance < settings.Basins.Occurrence
                && chance < settings.Basins.Occurrence * BasinRule(gridX, gridZ).BasinOccurrence;
        }

        public bool IsRiverCandidate(int gridX, int gridZ)
        {
            var chance = WaterMapDrawingMath.Value01(gridX, gridZ, riverSeed);
            return chance < settings.River.Occurrence
                && chance < settings.River.Occurrence * (climate?.GetHydrologyRule(
                    checked(gridX * RiverCandidateSpacingCells), checked(gridZ * RiverCandidateSpacingCells)).RiverOccurrence ?? 1f);
        }

        public HydrologyFeatureKey GetBasinKey(int gridX, int gridZ)
        {
            var area = ResolveBasinArea(gridX, gridZ);
            var kind = area <= settings.Basins.PondMaximumAreaCells
                ? HydrologyFeatureKind.Pond
                : HydrologyFeatureKind.Lake;
            return WaterMapDrawingMath.CreateKey(
                kind,
                checked(gridX * settings.Basins.CandidateLatticeSpacingCells),
                checked(gridZ * settings.Basins.CandidateLatticeSpacingCells),
                basinSeed);
        }

        public BasinWaterBrush CreateBasin(
            int gridX,
            int gridZ,
            ITerrainPatternMapReader terrain)
        {
            var basin = settings.Basins;
            var area = ResolveBasinArea(gridX, gridZ);
            var waterType = area <= basin.PondMaximumAreaCells
                ? WaterType.Pond
                : WaterType.Lake;
            var ownerX = checked(gridX * basin.CandidateLatticeSpacingCells);
            var ownerZ = checked(gridZ * basin.CandidateLatticeSpacingCells);
            var maximumDepth = WaterMapDrawingMath.Lerp(
                basin.MaximumDepth.Minimum,
                basin.MaximumDepth.Maximum,
                WaterMapDrawingMath.Value01(
                    gridX,
                    gridZ,
                    WaterMapDrawingMath.DeriveSeed(basinSeed, "maximum-depth")));
            var bedAmplitude = WaterMapDrawingMath.Lerp(
                basin.BedAmplitude.Minimum,
                basin.BedAmplitude.Maximum,
                WaterMapDrawingMath.Value01(
                    gridX,
                    gridZ,
                    WaterMapDrawingMath.DeriveSeed(basinSeed, "bed-amplitude")));
            var key = GetBasinKey(gridX, gridZ);
            var geometry = BuildBasinGeometry(
                ownerX,
                ownerZ,
                area,
                terrain);
            return new BasinWaterBrush(
                key,
                waterType,
                geometry,
                maximumDepth,
                bedAmplitude,
                basin);
        }

        public HydrologyFeatureKey GetRiverKey(int gridX, int gridZ) =>
            WaterMapDrawingMath.CreateKey(
                HydrologyFeatureKind.River,
                checked(gridX * settings.River.CandidateLatticeSpacingCells),
                checked(gridZ * settings.River.CandidateLatticeSpacingCells),
                riverSeed);

        public RiverWaterBrush CreateRiver(
            int gridX,
            int gridZ,
            ITerrainPatternMapReader terrain)
        {
            var river = settings.River;
            var key = GetRiverKey(gridX, gridZ);
            var featureSeed = GetRiverStrokeSeed(gridX, gridZ);
            var anchor = GetRiverAnchor(gridX, gridZ, featureSeed);
            var nodeCount = ResolveRiverNodeCount(
                gridX,
                gridZ,
                featureSeed);
            var boneTree = BuildRiverBoneTree(
                anchor,
                nodeCount,
                featureSeed,
                terrain);
            return new RiverWaterBrush(
                key,
                boneTree,
                river,
                settings.Sea.SurfaceHeight,
                terrain);
        }

        // A child has at most its parent's remaining nodes. Branching therefore
        // never increases the maximum root-to-tip step count.
        private float GetMaximumRiverTreeReachCells() =>
            settings.River.MaximumNodeCount - 1f;

        public bool CanBasinAffect(int gridX, int gridZ, PatternTileBounds bounds)
        {
            var radius = Math.Min(
                settings.Basins.MaximumReachCells,
                ResolveUnscaledBasinArea(gridX, gridZ) - 1)
                + settings.Basins.ShoreTransitionCells;
            return CanReach(
                (long)gridX * BasinCandidateSpacingCells,
                (long)gridZ * BasinCandidateSpacingCells,
                radius,
                bounds);
        }

        public bool CanRiverAffect(int gridX, int gridZ, PatternTileBounds bounds)
        {
            var featureSeed = GetRiverStrokeSeed(gridX, gridZ);
            var anchor = GetRiverAnchor(gridX, gridZ, featureSeed);
            var radius = checked((int)MathF.Ceiling(
                ResolveRiverNodeCount(gridX, gridZ, featureSeed) - 1f
                + RiverWaterBrush.InfluenceRadius(settings.River.Width.Maximum)));
            return CanReach(
                RoundToMapCell(anchor.X), RoundToMapCell(anchor.Z), radius, bounds);
        }

        private int GetRiverStrokeSeed(int gridX, int gridZ) =>
            unchecked((int)PatternNoise.Hash(
                gridX, gridZ, WaterMapDrawingMath.DeriveSeed(riverSeed, "stroke")));

        private WaterMapPoint GetRiverAnchor(int gridX, int gridZ, int featureSeed)
        {
            var river = settings.River;
            var ownerX = checked(gridX * river.CandidateLatticeSpacingCells);
            var ownerZ = checked(gridZ * river.CandidateLatticeSpacingCells);
            return new WaterMapPoint(
                ownerX + WaterMapDrawingMath.SignedValue01(
                    gridX, gridZ,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "anchor-x"))
                    * river.AnchorJitterCells,
                ownerZ + WaterMapDrawingMath.SignedValue01(
                    gridX, gridZ,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "anchor-z"))
                    * river.AnchorJitterCells);
        }

        private static bool CanReach(
            long x, long z, int radius, PatternTileBounds bounds) =>
            x + radius >= bounds.MinimumX && x - radius < bounds.MaximumXExclusive
            && z + radius >= bounds.MinimumZ && z - radius < bounds.MaximumZExclusive;

        private int ResolveRiverNodeCount(
            int gridX,
            int gridZ,
            int featureSeed)
        {
            var river = settings.River;
            if (river.MinimumNodeCount == river.MaximumNodeCount
                || river.AverageNodeCount == river.MinimumNodeCount)
            {
                return river.MinimumNodeCount;
            }

            if (river.AverageNodeCount == river.MaximumNodeCount)
            {
                return river.MaximumNodeCount;
            }

            var average = (river.AverageNodeCount - river.MinimumNodeCount)
                / (float)(river.MaximumNodeCount - river.MinimumNodeCount);
            var distributionExponent = (1f - average) / average;
            var random = WaterMapDrawingMath.Value01(
                gridX,
                gridZ,
                WaterMapDrawingMath.DeriveSeed(featureSeed, "node-count"));
            return Math.Clamp(
                (int)MathF.Round(WaterMapDrawingMath.Lerp(
                    river.MinimumNodeCount,
                    river.MaximumNodeCount,
                    MathF.Pow(random, distributionExponent))),
                river.MinimumNodeCount,
                river.MaximumNodeCount);
        }

        private RiverBoneTree BuildRiverBoneTree(
            WaterMapPoint anchor,
            int nodeCount,
            int featureSeed,
            ITerrainPatternMapReader terrain)
        {
            var river = settings.River;
            var strokes = new List<RiverBoneStroke>();
            var rootBone = BuildRiverBone(
                anchor,
                CreateInitialMainDirection(featureSeed),
                nodeCount,
                featureSeed,
                terrain);
            strokes.Add(new RiverBoneStroke(
                rootBone,
                featureSeed,
                -1,
                -1,
                1f,
                1f));

            var pendingParents = new Queue<int>();
            pendingParents.Enqueue(0);
            var descendantBranchCount = 0;
            while (pendingParents.Count > 0
                && descendantBranchCount < river.MaximumDescendantBranchCount)
            {
                var parentStrokeIndex = pendingParents.Dequeue();
                var parentStroke = strokes[parentStrokeIndex];
                for (var parentNodeIndex = 0;
                     parentNodeIndex < parentStroke.Nodes.Length - 1
                        && descendantBranchCount
                            < river.MaximumDescendantBranchCount;
                     parentNodeIndex++)
                {
                    var remainingNodeCount = parentStroke.Nodes.Length
                        - parentNodeIndex;
                    if (remainingNodeCount < river.MinimumBranchNodeCount)
                    {
                        continue;
                    }

                    var branchPoint = parentStroke.Nodes[parentNodeIndex].Point;
                    var branchSeed = unchecked((int)PatternNoise.Hash(
                        RoundToMapCell(branchPoint.X), RoundToMapCell(branchPoint.Z),
                        WaterMapDrawingMath.DeriveSeed(
                            parentStroke.FeatureSeed, $"branch-{parentNodeIndex}")));
                    if (WaterMapDrawingMath.Value01(
                            branchSeed,
                            0,
                            WaterMapDrawingMath.DeriveSeed(
                                branchSeed,
                                "occurrence"))
                        >= river.BranchOccurrencePerNode)
                    {
                        continue;
                    }

                    var branchNodeCount = ResolveBranchNodeCount(
                        remainingNodeCount,
                        branchSeed);
                    if (branchNodeCount < river.MinimumBranchNodeCount)
                    {
                        continue;
                    }

                    var branchDirection = CreateBranchDirection(
                        parentStroke.Nodes,
                        parentNodeIndex,
                        branchSeed);
                    var branchBone = BuildRiverBone(
                        parentStroke.Nodes[parentNodeIndex].Point,
                        branchDirection,
                        branchNodeCount,
                        branchSeed,
                        terrain,
                        parentStroke.Nodes[parentNodeIndex].Direction);
                    var branchStroke = new RiverBoneStroke(
                        branchBone,
                        branchSeed,
                        parentStrokeIndex,
                        parentNodeIndex,
                        SampleDistribution(
                            river.BranchWidthRatio,
                            branchSeed,
                            "width-ratio"),
                        SampleDistribution(
                            river.BranchDepthRatio,
                            branchSeed,
                            "depth-ratio"));
                    strokes.Add(branchStroke);
                    pendingParents.Enqueue(strokes.Count - 1);
                    descendantBranchCount++;
                }
            }

            return new RiverBoneTree(strokes.ToArray());
        }

        private int ResolveBranchNodeCount(
            int parentRemainingNodeCount,
            int branchSeed) => Math.Clamp(
            (int)MathF.Round(
                parentRemainingNodeCount * SampleDistribution(
                    settings.River.BranchNodeCountRatio,
                    branchSeed,
                    "node-count-ratio")),
            2,
            parentRemainingNodeCount);

        private RiverBoneNode[] BuildRiverBone(
            WaterMapPoint anchor,
            WaterMapPoint initialDirection,
            int nodeCount,
            int featureSeed,
            ITerrainPatternMapReader terrain,
            WaterMapPoint? parentDirection = null)
        {
            var river = settings.River;
            var result = new RiverBoneNode[nodeCount];
            var point = anchor;
            var direction = initialDirection;
            var turnRadians = WaterMapDrawingMath.Lerp(
                    river.NodeTurnDegrees.Minimum,
                    river.NodeTurnDegrees.Maximum,
                    WaterMapDrawingMath.Value01(
                        featureSeed,
                        0,
                        WaterMapDrawingMath.DeriveSeed(
                            featureSeed,
                            "turn-amplitude")))
                * MathF.PI / 180f;
            var distance = 0f;
            var curvatureSeed = WaterMapDrawingMath.DeriveSeed(featureSeed, "curvature");
            // Advance bend phase by travelled distance, so returning to the same
            // noise region cannot lock the stroke into endlessly turning one way.
            var waveNumber = MathF.PI * river.CurvatureField.Scale;
            var phase = WaterMapDrawingMath.Value01(featureSeed, 0, curvatureSeed) * 2f * MathF.PI;
            var excursion = MathF.Atan(turnRadians / waveNumber);
            var previousStep = 1f;
            result[0] = new RiverBoneNode(point, distance, direction);
            for (var index = 1; index < nodeCount; index++)
            {
                var spatial = WaterMapDrawingMath.SampleSigned(
                    point.X,
                    point.Z,
                    river.CurvatureField,
                    curvatureSeed);
                // Integrate a smooth curvature lobe over the previous raster step.
                // Each half-wave turns less than pi; spatial variation changes its
                // strength without extending its sign indefinitely.
                var bend = excursion * (.75f + .25f * ((spatial + 1f) * .5f))
                    * (MathF.Cos(phase + (distance - previousStep) * waveNumber)
                        - MathF.Cos(phase + distance * waveNumber));
                var previous = point;
                var referenceDirection = index == 1 && parentDirection.HasValue
                    ? parentDirection.Value
                    : direction;
                direction = SelectTerrainAwareDirection(
                    point,
                    direction,
                    bend,
                    referenceDirection,
                    terrain);
                // Keep the continuous heading: raster rounding must not erase curvature.
                point = MoveAlongCenterline(point, direction);
                previousStep = WaterMapDrawingMath.Distance(previous, point);
                distance += previousStep;
                result[index] = new RiverBoneNode(point, distance, direction);
            }

            return result;
        }

        private WaterMapPoint CreateBranchDirection(
            IReadOnlyList<RiverBoneNode> parentNodes,
            int parentNodeIndex,
            int branchSeed)
        {
            var parentDirection = parentNodes[parentNodeIndex].Direction;
            var sign = WaterMapDrawingMath.Value01(
                branchSeed,
                0,
                WaterMapDrawingMath.DeriveSeed(branchSeed, "angle-sign"))
                < 0.5f ? -1f : 1f;
            var angleRadians = SampleDistribution(
                    settings.River.BranchOpeningAngleDegrees,
                    branchSeed,
                    "opening-angle")
                * MathF.PI / 180f;
            return ConstrainForward(
                parentDirection, TurnForward(parentDirection, sign * angleRadians));
        }

        // The previous heading's dominant X/Z component defines the allowed half-plane.
        private static WaterMapPoint ConstrainForward(
            WaterMapPoint reference, WaterMapPoint candidate)
        {
            if (MathF.Abs(reference.X) >= MathF.Abs(reference.Z))
            {
                return candidate.X * reference.X < 0f
                    ? candidate.Z == 0f ? reference
                        : new WaterMapPoint(0f, MathF.Sign(candidate.Z))
                    : candidate;
            }

            return candidate.Z * reference.Z < 0f
                ? candidate.X == 0f ? reference
                    : new WaterMapPoint(MathF.Sign(candidate.X), 0f)
                : candidate;
        }

        private WaterMapPoint SelectTerrainAwareDirection(
            WaterMapPoint point,
            WaterMapPoint previousDirection,
            float bend,
            WaterMapPoint referenceDirection,
            ITerrainPatternMapReader terrain)
        {
            var currentHeight = terrain.GetCell(
                RoundToMapCell(point.X),
                RoundToMapCell(point.Z)).SurfaceHeight;
            var bestDirection = referenceDirection;
            var bestCost = float.PositiveInfinity;
            for (var directionOffset = -1;
                 directionOffset <= 1;
                 directionOffset++)
            {
                // Terrain may soften a bend, but cannot reverse or intensify its
                // scheduled curvature and introduce an independent cumulative turn.
                var factor = directionOffset == 0 ? 1f : directionOffset < 0 ? .5f : 0f;
                var candidateDirection = TurnForward(previousDirection, bend * factor);
                candidateDirection = ConstrainForward(referenceDirection, candidateDirection);
                var candidatePoint = MoveAlongCenterline(point, candidateDirection);
                var nextHeight = terrain.GetCell(
                    RoundToMapCell(candidatePoint.X),
                    RoundToMapCell(candidatePoint.Z)).SurfaceHeight;
                var heightChange = MathF.Abs(nextHeight - currentHeight)
                    / (settings.River.TerrainHeightChangeReferenceCells
                        * WorldGrid.HeightStepsPerCell);
                var cost = MathF.Abs(directionOffset)
                    + heightChange * settings.River.TerrainAvoidanceStrength;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestDirection = candidateDirection;
                }
            }

            return bestDirection;
        }

        private static float SampleDistribution(
            RiverDistributionData distribution,
            int seed,
            string channel)
        {
            if (distribution.Minimum == distribution.Maximum
                || distribution.Average == distribution.Minimum)
            {
                return distribution.Minimum;
            }

            if (distribution.Average == distribution.Maximum)
            {
                return distribution.Maximum;
            }

            var average = (distribution.Average - distribution.Minimum)
                / (distribution.Maximum - distribution.Minimum);
            var exponent = (1f - average) / average;
            var random = WaterMapDrawingMath.Value01(
                seed,
                0,
                WaterMapDrawingMath.DeriveSeed(seed, channel));
            return WaterMapDrawingMath.Lerp(
                distribution.Minimum,
                distribution.Maximum,
                MathF.Pow(random, exponent));
        }

        private static WaterMapPoint CreateInitialMainDirection(int featureSeed)
        {
            var angle = WaterMapDrawingMath.Value01(
                    featureSeed,
                    0,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "direction"))
                * MathF.PI * 2f;
            return EmphasizeDominantAxis(new WaterMapPoint(
                MathF.Cos(angle),
                MathF.Sin(angle)));
        }

        private static WaterMapPoint TurnForward(
            WaterMapPoint direction,
            float turnRadians)
        {
            var amount = MathF.Tan(turnRadians);
            return Normalize(new WaterMapPoint(
                direction.X - direction.Z * amount,
                direction.Z + direction.X * amount));
        }

        private static WaterMapPoint MoveAlongCenterline(
            WaterMapPoint point,
            WaterMapPoint direction) => new(
            point.X + direction.X,
            point.Z + direction.Z);

        private static WaterMapPoint EmphasizeDominantAxis(
            WaterMapPoint direction) => Normalize(new WaterMapPoint(
            direction.X * MathF.Abs(direction.X),
            direction.Z * MathF.Abs(direction.Z)));

        private static WaterMapPoint Normalize(WaterMapPoint value)
        {
            var length = MathF.Sqrt(value.X * value.X + value.Z * value.Z);
            return new WaterMapPoint(value.X / length, value.Z / length);
        }

        internal static int RoundToMapCell(float value) => checked((int)MathF.Round(
            value,
            MidpointRounding.AwayFromZero));

        private BiomeHydrologyRule BasinRule(int x, int z) => climate?.GetHydrologyRule(
            checked(x * BasinCandidateSpacingCells), checked(z * BasinCandidateSpacingCells))
            ?? new BiomeHydrologyRule { BasinOccurrence = 1, BasinArea = 1, RiverOccurrence = 1 };

        private int ResolveBasinArea(int gridX, int gridZ) => Math.Clamp(
            (int)MathF.Round(ResolveUnscaledBasinArea(gridX, gridZ) * BasinRule(gridX, gridZ).BasinArea),
            1, checked((int)MathF.Ceiling(settings.Basins.Area.Maximum)));

        private int ResolveUnscaledBasinArea(int gridX, int gridZ) => Math.Clamp(
            (int)MathF.Round(WaterMapDrawingMath.Lerp(settings.Basins.Area.Minimum,
                settings.Basins.Area.Maximum, WaterMapDrawingMath.Value01(gridX, gridZ,
                    WaterMapDrawingMath.DeriveSeed(basinSeed, "area")))),
            1, checked((int)MathF.Ceiling(settings.Basins.Area.Maximum)));

        private BasinDrawingGeometry BuildBasinGeometry(
            int ownerX,
            int ownerZ,
            int targetArea,
            ITerrainPatternMapReader terrain)
        {
            var start = CoordinateKey(ownerX, ownerZ);
            var sampled = new Dictionary<long, TerrainPatternCell>();
            var first = SampleTerrain(ownerX, ownerZ);
            if (HasSeaInfluence(first))
            {
                return BasinDrawingGeometry.Empty;
            }

            var costs = new Dictionary<long, float>
            {
                { start, 0f }
            };
            var footprint = new Dictionary<long, TerrainPatternCell>();
            var frontier = new BasinCostHeap();
            frontier.Push(start, 0f);
            while (frontier.Count > 0 && footprint.Count < targetArea)
            {
                var current = frontier.Pop();
                if (!costs.TryGetValue(current.Key, out var currentCost)
                    || current.Cost != currentCost)
                {
                    continue;
                }

                if (!sampled.TryGetValue(current.Key, out var currentCell))
                {
                    var currentX = (int)(current.Key >> 32);
                    var currentZ = (int)current.Key;
                    currentCell = SampleTerrain(currentX, currentZ);
                }

                if (HasSeaInfluence(currentCell))
                {
                    continue;
                }

                footprint.Add(current.Key, currentCell);
                var x = (int)(current.Key >> 32);
                var z = (int)current.Key;
                AddNeighbor(x - 1, z, 1f);
                AddNeighbor(x + 1, z, 1f);
                AddNeighbor(x, z - 1, 1f);
                AddNeighbor(x, z + 1, 1f);
                AddNeighbor(x - 1, z - 1, MathF.Sqrt(2f));
                AddNeighbor(x + 1, z - 1, MathF.Sqrt(2f));
                AddNeighbor(x - 1, z + 1, MathF.Sqrt(2f));
                AddNeighbor(x + 1, z + 1, MathF.Sqrt(2f));

                void AddNeighbor(int nextX, int nextZ, float distance)
                {
                    if (Math.Max(
                            Math.Abs(nextX - ownerX),
                            Math.Abs(nextZ - ownerZ))
                        > settings.Basins.MaximumReachCells)
                    {
                        return;
                    }

                    var next = CoordinateKey(nextX, nextZ);
                    if (footprint.ContainsKey(next))
                    {
                        return;
                    }

                    var nextCell = SampleTerrain(nextX, nextZ);
                    if (HasSeaInfluence(nextCell))
                    {
                        return;
                    }

                    var terrainDelta = MathF.Abs(
                        nextCell.SurfaceHeight - currentCell.SurfaceHeight)
                        / WorldGrid.HeightStepsPerCell;
                    var potential = settings.Basins.PotentialResponse.Evaluate(
                        WaterMapDrawingMath.SampleNormalized(
                            nextX,
                            nextZ,
                            settings.Basins.PotentialField,
                            WaterMapDrawingMath.DeriveSeed(
                                basinSeed,
                                "potential")));
                    var cost = current.Cost + distance + distance * (
                        potential * settings.Basins.PotentialCost
                        + terrainDelta * settings.Basins.TerrainDeformationCost
                        + terrainDelta / distance * settings.Basins.SlopeCost);
                    if (costs.TryGetValue(next, out var previous)
                        && previous <= cost)
                    {
                        return;
                    }

                    costs[next] = cost;
                    frontier.Push(next, cost);
                }
            }

            if (footprint.Count != targetArea)
            {
                return BasinDrawingGeometry.Empty;
            }

            var surfaceHeight = SelectBasinSurface(footprint);
            if (surfaceHeight < 1f) return BasinDrawingGeometry.Empty;
            var boundary = FindBasinBoundary(footprint);
            return new BasinDrawingGeometry(
                surfaceHeight,
                BuildInteriorProgress(footprint, boundary),
                BuildShoreMembership(footprint, boundary));

            TerrainPatternCell SampleTerrain(int x, int z)
            {
                var coordinate = CoordinateKey(x, z);
                if (!sampled.TryGetValue(coordinate, out var cell))
                {
                    cell = terrain.GetCell(x, z);
                    sampled.Add(coordinate, cell);
                }

                return cell;
            }
        }

        private float SelectBasinSurface(
            IReadOnlyDictionary<long, TerrainPatternCell> cells)
        {
            // Minimize the piecewise-linear cut/fill/rim objective at its breakpoints.
            // The rim's target is S+1 (the first exterior Filled), not the water plane S.
            var events = new List<(double Height, double Weight)>();
            double derivative = 0;
            double terrainSum = 0;
            var ordered = new List<long>(cells.Keys);
            ordered.Sort();
            foreach (var coordinate in ordered)
            {
                var pair = new KeyValuePair<long, TerrainPatternCell>(coordinate, cells[coordinate]);
                var h = pair.Value.SurfaceHeight;
                terrainSum += h;
                events.Add((h, (double)settings.Basins.CutCost + settings.Basins.FillCost));
                derivative -= settings.Basins.CutCost;
                if (IsBasinBoundary(cells, (int)(pair.Key >> 32), (int)pair.Key))
                {
                    events.Add((h - 1d, 2d * settings.Basins.RimCost));
                    derivative -= settings.Basins.RimCost;
                }
            }
            events.Sort((a, b) => a.Height.CompareTo(b.Height));
            var optimum = events[0].Height;
            foreach (var item in events)
            {
                optimum = item.Height;
                derivative += item.Weight;
                if (derivative >= 0d) break;
            }
            // Never raise the water above the original footprint's mean to accommodate
            // desired depth. The world floor bounds the cavity instead.
            var minimum = 1;
            var maximum = Math.Min(checked((int)Math.Floor(terrainSum / cells.Count)),
                checked(settings.World.WorldHeight * WorldGrid.HeightStepsPerCell - 1));
            if (maximum < minimum) return 0f; // No representable wet volume below the upper bound.
            var lower = checked((int)Math.Max(minimum, Math.Min(maximum, Math.Floor(optimum))));
            var upper = checked((int)Math.Max(minimum, Math.Min(maximum, Math.Ceiling(optimum))));
            return Cost(lower) <= Cost(upper) ? lower : upper;

            double Cost(int surface)
            {
                double total = 0;
                foreach (var coordinate in ordered)
                {
                    var pair = new KeyValuePair<long, TerrainPatternCell>(coordinate, cells[coordinate]);
                    var delta = pair.Value.SurfaceHeight - (double)surface;
                    total += delta >= 0 ? delta * settings.Basins.CutCost : -delta * settings.Basins.FillCost;
                    if (IsBasinBoundary(cells, (int)(pair.Key >> 32), (int)pair.Key))
                        total += Math.Abs(delta - 1d) * settings.Basins.RimCost;
                }
                return total;
            }
        }
        private static List<long> FindBasinBoundary(
            IReadOnlyDictionary<long, TerrainPatternCell> cells)
        {
            var result = new List<long>();
            foreach (var pair in cells)
            {
                var x = (int)(pair.Key >> 32);
                var z = (int)pair.Key;
                if (IsBasinBoundary(cells, x, z))
                {
                    result.Add(pair.Key);
                }
            }

            return result;
        }

        private static Dictionary<long, float> BuildInteriorProgress(
            IReadOnlyDictionary<long, TerrainPatternCell> cells,
            IReadOnlyList<long> boundary)
        {
            var distances = new Dictionary<long, int>(cells.Count);
            var queue = new Queue<long>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distances.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            var maximumDistance = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var distance = distances[current];
                var x = (int)(current >> 32);
                var z = (int)current;
                AddInteriorNeighbor(x - 1, z, distance);
                AddInteriorNeighbor(x + 1, z, distance);
                AddInteriorNeighbor(x, z - 1, distance);
                AddInteriorNeighbor(x, z + 1, distance);
            }

            var progress = new Dictionary<long, float>(distances.Count);
            foreach (var pair in distances)
            {
                progress.Add(
                    pair.Key,
                    maximumDistance > 0
                        ? pair.Value / (float)maximumDistance
                        : 1f);
            }

            return progress;

            void AddInteriorNeighbor(int x, int z, int distance)
            {
                var next = CoordinateKey(x, z);
                if (!cells.ContainsKey(next) || distances.ContainsKey(next))
                {
                    return;
                }

                var nextDistance = distance + 1;
                distances.Add(next, nextDistance);
                maximumDistance = Math.Max(maximumDistance, nextDistance);
                queue.Enqueue(next);
            }
        }

        private Dictionary<long, float> BuildShoreMembership(
            IReadOnlyDictionary<long, TerrainPatternCell> cells,
            IReadOnlyList<long> boundary)
        {
            var distances = new Dictionary<long, int>();
            var queue = new Queue<long>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distances.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            var membership = new Dictionary<long, float>();
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var distance = distances[current];
                if (distance >= settings.Basins.ShoreTransitionCells)
                {
                    continue;
                }

                var x = (int)(current >> 32);
                var z = (int)current;
                AddShoreNeighbor(x - 1, z, distance);
                AddShoreNeighbor(x + 1, z, distance);
                AddShoreNeighbor(x, z - 1, distance);
                AddShoreNeighbor(x, z + 1, distance);
            }

            return membership;

            void AddShoreNeighbor(int x, int z, int distance)
            {
                var next = CoordinateKey(x, z);
                if (cells.ContainsKey(next) || distances.ContainsKey(next))
                {
                    return;
                }

                var nextDistance = distance + 1;
                distances.Add(next, nextDistance);
                queue.Enqueue(next);
                membership.Add(
                    next,
                    settings.Basins.ShoreTransition.Evaluate(
                        1f - (nextDistance - 1f) / Math.Max(1, settings.Basins.ShoreTransitionCells - 1)));
            }
        }

        private static bool IsBasinBoundary(
            IReadOnlyDictionary<long, TerrainPatternCell> cells,
            int x,
            int z) => !cells.ContainsKey(CoordinateKey(x - 1, z))
            || !cells.ContainsKey(CoordinateKey(x + 1, z))
            || !cells.ContainsKey(CoordinateKey(x, z - 1))
            || !cells.ContainsKey(CoordinateKey(x, z + 1));

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;

        private static bool HasSeaInfluence(TerrainPatternCell cell) =>
            cell.HasSeaPattern || cell.HasSecondarySeaPattern;

        private readonly struct BasinCostEntry
        {
            public BasinCostEntry(long key, float cost)
            {
                Key = key;
                Cost = cost;
            }

            public long Key { get; }
            public float Cost { get; }
        }

        private sealed class BasinCostHeap
        {
            private readonly List<BasinCostEntry> entries = new();

            public int Count => entries.Count;

            public void Push(long key, float cost)
            {
                entries.Add(new BasinCostEntry(key, cost));
                var index = entries.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (Compare(entries[parent], entries[index]) <= 0)
                    {
                        break;
                    }

                    (entries[parent], entries[index]) = (entries[index], entries[parent]);
                    index = parent;
                }
            }

            public BasinCostEntry Pop()
            {
                var result = entries[0];
                var lastIndex = entries.Count - 1;
                var last = entries[lastIndex];
                entries.RemoveAt(lastIndex);
                if (entries.Count == 0)
                {
                    return result;
                }

                entries[0] = last;
                var index = 0;
                while (true)
                {
                    var left = index * 2 + 1;
                    if (left >= entries.Count)
                    {
                        break;
                    }

                    var right = left + 1;
                    var child = right < entries.Count
                        && Compare(entries[right], entries[left]) < 0
                        ? right
                        : left;
                    if (Compare(entries[index], entries[child]) <= 0)
                    {
                        break;
                    }

                    (entries[index], entries[child]) = (entries[child], entries[index]);
                    index = child;
                }

                return result;
            }

            private static int Compare(BasinCostEntry left, BasinCostEntry right)
            {
                var cost = left.Cost.CompareTo(right.Cost);
                return cost != 0 ? cost : left.Key.CompareTo(right.Key);
            }
        }

    }

}
