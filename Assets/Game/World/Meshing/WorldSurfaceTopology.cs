using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Meshing
{
    internal readonly struct SurfaceBoundaryProfile
    {
        public readonly float StartHeightUnits;
        public readonly float ShoulderStartHeightUnits;
        public readonly float ShoulderEndHeightUnits;
        public readonly float EndHeightUnits;

        public SurfaceBoundaryProfile(
            float startHeightUnits,
            float shoulderStartHeightUnits,
            float shoulderEndHeightUnits,
            float endHeightUnits)
        {
            StartHeightUnits = startHeightUnits;
            ShoulderStartHeightUnits = shoulderStartHeightUnits;
            ShoulderEndHeightUnits = shoulderEndHeightUnits;
            EndHeightUnits = endHeightUnits;
        }

        public float GetHeight(float position)
        {
            position = Math.Clamp(position, 0f, 1f);
            if (position <= 0.2f)
            {
                return Lerp(
                    StartHeightUnits,
                    ShoulderStartHeightUnits,
                    position / 0.2f);
            }

            if (position >= 0.8f)
            {
                return Lerp(
                    ShoulderEndHeightUnits,
                    EndHeightUnits,
                    (position - 0.8f) / 0.2f);
            }

            return Lerp(
                ShoulderStartHeightUnits,
                ShoulderEndHeightUnits,
                (position - 0.2f) / 0.6f);
        }

        private static float Lerp(float a, float b, float t) =>
            a + (b - a) * t;
    }

    internal readonly struct SurfaceCornerClosureProfile
    {
        public readonly float XCornerHeightUnits;
        public readonly float XShoulderHeightUnits;
        public readonly float ZShoulderHeightUnits;
        public readonly float ZCornerHeightUnits;

        public SurfaceCornerClosureProfile(
            float xCornerHeightUnits,
            float xShoulderHeightUnits,
            float zShoulderHeightUnits,
            float zCornerHeightUnits)
        {
            XCornerHeightUnits = xCornerHeightUnits;
            XShoulderHeightUnits = xShoulderHeightUnits;
            ZShoulderHeightUnits = zShoulderHeightUnits;
            ZCornerHeightUnits = zCornerHeightUnits;
        }
    }

    internal static class SurfaceBoundaryClosure
    {
        private const float Epsilon = 0.0001f;

        public static bool TryResolve(
            in SurfaceBoundaryProfile xBottomBoundary,
            in SurfaceBoundaryProfile zBottomBoundary,
            in SurfaceBoundaryProfile xTopBoundary,
            in SurfaceBoundaryProfile zTopBoundary,
            int directionX,
            int directionZ,
            float shoulder,
            float minimumHeightUnits,
            float maximumHeightUnits,
            out SurfaceCornerClosureProfile closure)
        {
            closure = default;
            var cornerX = directionX < 0 ? 0f : 1f;
            var cornerZ = directionZ < 0 ? 0f : 1f;
            var shoulderX = directionX < 0 ? shoulder : 1f - shoulder;
            var shoulderZ = directionZ < 0 ? shoulder : 1f - shoulder;

            var xTopCorner = xTopBoundary.GetHeight(cornerZ);
            var xTopShoulder = xTopBoundary.GetHeight(shoulderZ);
            var zTopCorner = zTopBoundary.GetHeight(cornerX);
            var zTopShoulder = zTopBoundary.GetHeight(shoulderX);
            var xCorner = ClampBottom(
                xBottomBoundary.GetHeight(cornerZ),
                xTopCorner,
                minimumHeightUnits,
                maximumHeightUnits);
            var xShoulder = ClampBottom(
                xBottomBoundary.GetHeight(shoulderZ),
                xTopShoulder,
                minimumHeightUnits,
                maximumHeightUnits);
            var zCorner = ClampBottom(
                zBottomBoundary.GetHeight(cornerX),
                zTopCorner,
                minimumHeightUnits,
                maximumHeightUnits);
            var zShoulder = ClampBottom(
                zBottomBoundary.GetHeight(shoulderX),
                zTopShoulder,
                minimumHeightUnits,
                maximumHeightUnits);

            var xSideExposed = xCorner < xTopCorner - Epsilon
                || xShoulder < xTopShoulder - Epsilon;
            var zSideExposed = zCorner < zTopCorner - Epsilon
                || zShoulder < zTopShoulder - Epsilon;
            if (!xSideExposed || !zSideExposed)
            {
                return false;
            }

            if (ApproximatelyEqual(xCorner, xShoulder)
                && ApproximatelyEqual(xCorner, zCorner)
                && ApproximatelyEqual(xCorner, zShoulder))
            {
                return false;
            }

            closure = new SurfaceCornerClosureProfile(
                xCorner,
                xShoulder,
                zShoulder,
                zCorner);
            return true;
        }

        private static float ClampBottom(
            float bottom,
            float top,
            float minimum,
            float maximum) =>
            Math.Min(Math.Clamp(bottom, minimum, maximum), top);

        private static bool ApproximatelyEqual(float a, float b) =>
            Math.Abs(a - b) <= Epsilon;
    }

    internal readonly struct SolidSurfaceProfile
    {
        public readonly int CenterHeightUnits;
        public readonly SurfaceBoundaryProfile NegativeXBoundary;
        public readonly SurfaceBoundaryProfile PositiveXBoundary;
        public readonly SurfaceBoundaryProfile NegativeZBoundary;
        public readonly SurfaceBoundaryProfile PositiveZBoundary;

        public SolidSurfaceProfile(
            int centerHeightUnits,
            in SurfaceBoundaryProfile negativeXBoundary,
            in SurfaceBoundaryProfile positiveXBoundary,
            in SurfaceBoundaryProfile negativeZBoundary,
            in SurfaceBoundaryProfile positiveZBoundary)
        {
            CenterHeightUnits = centerHeightUnits;
            NegativeXBoundary = negativeXBoundary;
            PositiveXBoundary = positiveXBoundary;
            NegativeZBoundary = negativeZBoundary;
            PositiveZBoundary = positiveZBoundary;
        }

        public SurfaceBoundaryProfile GetBoundary(
            int directionX,
            int directionZ)
        {
            if (directionX < 0) return NegativeXBoundary;
            if (directionX > 0) return PositiveXBoundary;
            if (directionZ < 0) return NegativeZBoundary;
            return PositiveZBoundary;
        }

        public float GetBoundaryHeight(
            int directionX,
            int directionZ,
            float position) =>
            GetBoundary(directionX, directionZ).GetHeight(position);

        public float GetCornerHeight(float cornerX, float cornerZ)
        {
            if (cornerX <= 0f)
            {
                return cornerZ <= 0f
                    ? NegativeXBoundary.StartHeightUnits
                    : NegativeXBoundary.EndHeightUnits;
            }

            return cornerZ <= 0f
                ? PositiveXBoundary.StartHeightUnits
                : PositiveXBoundary.EndHeightUnits;
        }

    }

    internal readonly struct WaterSurfaceProfile
    {
        public readonly FlowDirection Direction;
        public readonly HeightInterval Interval;
        public readonly SurfaceBoundaryProfile NegativeXBoundary;
        public readonly SurfaceBoundaryProfile PositiveXBoundary;
        public readonly SurfaceBoundaryProfile NegativeZBoundary;
        public readonly SurfaceBoundaryProfile PositiveZBoundary;
        public readonly bool TopExposed;
        public readonly bool ConnectsFromAbove;

        public WaterSurfaceProfile(
            FlowDirection direction,
            in HeightInterval interval,
            in SurfaceBoundaryProfile negativeXBoundary,
            in SurfaceBoundaryProfile positiveXBoundary,
            in SurfaceBoundaryProfile negativeZBoundary,
            in SurfaceBoundaryProfile positiveZBoundary,
            bool topExposed,
            bool connectsFromAbove)
        {
            Direction = direction;
            Interval = interval;
            NegativeXBoundary = negativeXBoundary;
            PositiveXBoundary = positiveXBoundary;
            NegativeZBoundary = negativeZBoundary;
            PositiveZBoundary = positiveZBoundary;
            TopExposed = topExposed;
            ConnectsFromAbove = connectsFromAbove;
        }

        public bool Falls =>
            (Direction & FlowDirection.Down) != 0;

        public SurfaceBoundaryProfile GetBoundary(
            int directionX,
            int directionZ)
        {
            if (directionX < 0) return NegativeXBoundary;
            if (directionX > 0) return PositiveXBoundary;
            if (directionZ < 0) return NegativeZBoundary;
            return PositiveZBoundary;
        }

        public float GetBoundaryHeight(
            int directionX,
            int directionZ,
            float position) =>
            GetBoundary(directionX, directionZ).GetHeight(position);

        public float GetVerticalBoundaryHeight(
            int directionX,
            int directionZ,
            float position) =>
            GetVerticalBoundary(directionX, directionZ).GetHeight(position);

        public SurfaceBoundaryProfile GetVerticalBoundary(
            int directionX,
            int directionZ) =>
            ConnectsFromAbove
                ? new SurfaceBoundaryProfile(
                    Interval.TopUnits,
                    Interval.TopUnits,
                    Interval.TopUnits,
                    Interval.TopUnits)
                : GetBoundary(directionX, directionZ);

    }

    internal sealed partial class WorldSurfaceQuery
    {
        private const int HorizontalDependencyRadius = 1;

        private readonly struct WaterCornerKey : IEquatable<WaterCornerKey>
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Z;

            public WaterCornerKey(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public bool Equals(WaterCornerKey other) =>
                X == other.X && Y == other.Y && Z == other.Z;

            public override bool Equals(object obj) =>
                obj is WaterCornerKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        private readonly WorldData world;
        private readonly Func<int, int, bool> canCacheColumn;
        private WaterFlowState flowState;
        private readonly Dictionary<CellCoordinate, SolidSurfaceProfile> solidProfiles =
            new();
        private readonly Dictionary<CellCoordinate, WaterSurfaceProfile> waterProfiles =
            new();
        private readonly Dictionary<WaterCornerKey, float>
            waterCornerHeights =
            new();

        public WorldSurfaceQuery(
            WorldData world,
            WaterFlowState flowState = null,
            Func<int, int, bool> canCacheColumn = null)
        {
            this.world = world
                ?? throw new ArgumentNullException(nameof(world));
            this.flowState = flowState;
            this.canCacheColumn = canCacheColumn;
        }

        public void SetWaterFlowState(WaterFlowState value)
        {
            if (ReferenceEquals(flowState, value))
            {
                return;
            }

            flowState = value;
            InvalidateAll();
        }

        public void InvalidateAll()
        {
            solidProfiles.Clear();
            waterProfiles.Clear();
            waterCornerHeights.Clear();
        }

        public void InvalidateRegion(in CellBounds changedBounds)
        {
            var minimumX = Math.Max(
                0,
                changedBounds.Minimum.X - HorizontalDependencyRadius);
            var maximumX = Math.Min(
                world.Size - 1,
                changedBounds.Maximum.X + HorizontalDependencyRadius);
            var minimumZ = Math.Max(
                0,
                changedBounds.Minimum.Z - HorizontalDependencyRadius);
            var maximumZ = Math.Min(
                world.Size - 1,
                changedBounds.Maximum.Z + HorizontalDependencyRadius);
            if (minimumX > maximumX || minimumZ > maximumZ)
            {
                return;
            }

            for (var y = 0; y < world.Height; y++)
            for (var z = minimumZ; z <= maximumZ; z++)
            for (var x = minimumX; x <= maximumX; x++)
            {
                var cell = new CellCoordinate(x, y, z);
                solidProfiles.Remove(cell);
                waterProfiles.Remove(cell);
            }

            for (var y = 0; y < world.Height; y++)
            for (var z = minimumZ; z <= maximumZ + 1; z++)
            for (var x = minimumX; x <= maximumX + 1; x++)
            {
                waterCornerHeights.Remove(new WaterCornerKey(x, y, z));
            }
        }

        public void InvalidateChunk(
            ChunkCoordinate coordinate,
            int chunkSizeXZ)
        {
            if (chunkSizeXZ <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeXZ));
            }

            var startX = coordinate.X * chunkSizeXZ;
            var startZ = coordinate.Z * chunkSizeXZ;
            var endX = Math.Min(startX + chunkSizeXZ, world.Size) - 1;
            var endZ = Math.Min(startZ + chunkSizeXZ, world.Size) - 1;
            if (startX > endX || startZ > endZ)
            {
                return;
            }

            InvalidateRegion(new CellBounds(
                new CellCoordinate(startX, 0, startZ),
                new CellCoordinate(endX, world.Height - 1, endZ)));
        }

        public SolidSurfaceProfile ResolveSolid(int x, int y, int z)
        {
            var coordinate = new CellCoordinate(x, y, z);
            if (solidProfiles.TryGetValue(coordinate, out var cached))
            {
                return cached;
            }

            var currentHeight = GetSolidTop(x, y, z);
            SolidSurfaceProfile profile;
            if (ShouldPinSolidTopToWaterBoundary(x, y, z))
            {
                profile = CreateFlatSolidProfile(currentHeight);
            }
            else
            {
                var cellBottom = y * WorldGrid.HeightStepsPerCell;
                var cellCeiling = (y + 1) * WorldGrid.HeightStepsPerCell;
                var negativeXNegativeZ = Math.Clamp(
                    ResolveSolidCornerHeight(
                        x, y, z, -1, -1, currentHeight),
                    cellBottom,
                    cellCeiling);
                var positiveXNegativeZ = Math.Clamp(
                    ResolveSolidCornerHeight(
                        x, y, z, 1, -1, currentHeight),
                    cellBottom,
                    cellCeiling);
                var negativeXPositiveZ = Math.Clamp(
                    ResolveSolidCornerHeight(
                        x, y, z, -1, 1, currentHeight),
                    cellBottom,
                    cellCeiling);
                var positiveXPositiveZ = Math.Clamp(
                    ResolveSolidCornerHeight(
                        x, y, z, 1, 1, currentHeight),
                    cellBottom,
                    cellCeiling);

                var negativeX = ResolveSolidBoundary(
                    x, y, z, -1, 0, currentHeight,
                    negativeXNegativeZ, negativeXPositiveZ,
                    cellBottom, cellCeiling);
                var positiveX = ResolveSolidBoundary(
                    x, y, z, 1, 0, currentHeight,
                    positiveXNegativeZ, positiveXPositiveZ,
                    cellBottom, cellCeiling);
                var negativeZ = ResolveSolidBoundary(
                    x, y, z, 0, -1, currentHeight,
                    negativeXNegativeZ, positiveXNegativeZ,
                    cellBottom, cellCeiling);
                var positiveZ = ResolveSolidBoundary(
                    x, y, z, 0, 1, currentHeight,
                    negativeXPositiveZ, positiveXPositiveZ,
                    cellBottom, cellCeiling);
                profile = new SolidSurfaceProfile(
                    currentHeight,
                    negativeX,
                    positiveX,
                    negativeZ,
                    positiveZ);
            }

            if (CanCacheColumn(x, z))
            {
                solidProfiles[coordinate] = profile;
            }

            return profile;
        }

        public bool TryResolveSolidAtHeight(
            int x,
            int z,
            int heightUnits,
            out SolidSurfaceProfile profile)
        {
            profile = default;
            if (!world.ContainsColumn(x, z) || heightUnits <= 0)
            {
                return false;
            }

            var y = (heightUnits - 1) / WorldGrid.HeightStepsPerCell;
            if (!world.TryGetCell(x, y, z, out var cell)
                || !cell.HasTerrain
                || GetSolidTop(y, cell) != heightUnits
                || !CellOccupancyResolver.IsSolidTopExposed(
                    world, x, y, z, cell))
            {
                return false;
            }

            profile = ResolveSolid(x, y, z);
            return true;
        }

        public bool TryResolveWater(
            int x,
            int y,
            int z,
            out WaterSurfaceProfile profile)
        {
            profile = default;
            if (!world.TryGetCell(x, y, z, out var cell)
                || !cell.HasWater)
            {
                return false;
            }

            var coordinate = new CellCoordinate(x, y, z);
            if (waterProfiles.TryGetValue(coordinate, out profile))
            {
                return true;
            }

            var direction = ResolveWaterDirection(x, y, z, cell);
            var interval = ResolveWaterInterval(x, y, z, cell, direction);
            var negativeX = ResolveWaterBoundary(x, y, z, -1, 0);
            var positiveX = ResolveWaterBoundary(x, y, z, 1, 0);
            var negativeZ = ResolveWaterBoundary(x, y, z, 0, -1);
            var positiveZ = ResolveWaterBoundary(x, y, z, 0, 1);
            profile = new WaterSurfaceProfile(
                direction,
                interval,
                negativeX,
                positiveX,
                negativeZ,
                positiveZ,
                IsWaterTopExposed(x, y, z, interval),
                FallsWater(x, y + 1, z));
            if (CanCacheColumn(x, z))
            {
                waterProfiles[coordinate] = profile;
            }

            return true;
        }

        public bool TryResolveWaterHorizontalBoundary(
            int x,
            int y,
            int z,
            int directionXFromCurrent,
            int directionZFromCurrent,
            out SurfaceBoundaryProfile boundary)
        {
            boundary = default;
            if (!TryResolveWater(x, y, z, out var profile)
                || !profile.TopExposed
                || profile.Falls)
            {
                return false;
            }

            boundary = profile.GetBoundary(
                -directionXFromCurrent,
                -directionZFromCurrent);
            return true;
        }

        public float ResolveWaterNeighborCoverage(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            float edgePosition)
        {
            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var neighborX = x + directionX;
            var neighborZ = z + directionZ;
            if (!world.TryGetCell(neighborX, y, neighborZ, out var neighbor))
            {
                return cellBottom;
            }

            var coveredTop = (float)cellBottom;
            if (neighbor.HasTerrain)
            {
                coveredTop = cellBottom + neighbor.Terrain.SolidHeight;
                if (CellOccupancyResolver.IsSolidTopExposed(
                        world,
                        neighborX,
                        y,
                        neighborZ,
                        neighbor))
                {
                    var solid = ResolveSolid(neighborX, y, neighborZ);
                    coveredTop = solid.GetBoundaryHeight(
                        -directionX,
                        -directionZ,
                        edgePosition);
                }
            }

            if (neighbor.HasWater
                && TryResolveWater(neighborX, y, neighborZ, out var water))
            {
                coveredTop = Math.Max(
                    coveredTop,
                    water.GetVerticalBoundaryHeight(
                        -directionX,
                        -directionZ,
                        edgePosition));
            }

            return coveredTop;
        }

        public bool IsWaterBottomExposed(
            int x,
            int y,
            int z,
            in CellData cell,
            in WaterSurfaceProfile profile)
        {
            if (cell.HasTerrain || y <= 0)
            {
                return false;
            }

            if (!world.TryGetCell(x, y - 1, z, out var below))
            {
                return true;
            }

            var coveredTop = (y - 1) * WorldGrid.HeightStepsPerCell
                + below.Terrain.SolidHeight;
            if (below.HasWater
                && TryResolveWater(x, y - 1, z, out var belowProfile))
            {
                coveredTop = Math.Max(
                    coveredTop,
                    belowProfile.Interval.TopUnits);
            }

            return coveredTop < profile.Interval.BottomUnits;
        }

        private SurfaceBoundaryProfile ResolveSolidBoundary(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            int currentHeight,
            float startCornerHeight,
            float endCornerHeight,
            int cellBottom,
            int cellCeiling) =>
            new(
                startCornerHeight,
                Math.Clamp(
                    ResolveSolidBoundaryHeight(
                        x, y, z, directionX, directionZ,
                        currentHeight, 0.2f),
                    cellBottom,
                    cellCeiling),
                Math.Clamp(
                    ResolveSolidBoundaryHeight(
                        x, y, z, directionX, directionZ,
                        currentHeight, 0.8f),
                    cellBottom,
                    cellCeiling),
                endCornerHeight);

        private float ResolveSolidBoundaryHeight(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            int currentHeight,
            float edgePosition)
        {
            var exists = TryGetConnectedSolidSurfaceHeight(
                x,
                z,
                x + directionX,
                y,
                z + directionZ,
                out var solidHeight);
            var neighborHeight = exists
                ? (float)solidHeight
                : float.MinValue;

            if (TryResolveWaterHorizontalBoundary(
                    x + directionX,
                    y,
                    z + directionZ,
                    directionX,
                    directionZ,
                    out var waterBoundary))
            {
                exists = true;
                neighborHeight = Math.Max(
                    neighborHeight,
                    waterBoundary.GetHeight(edgePosition));
            }

            if (!exists || neighborHeight >= currentHeight)
            {
                return currentHeight;
            }

            return Math.Max(currentHeight - 1f, neighborHeight);
        }

        private int ResolveSolidCornerHeight(
            int sourceX,
            int sourceY,
            int sourceZ,
            int directionX,
            int directionZ,
            int currentHeight)
        {
            var cornerX = sourceX + (directionX > 0 ? 1 : 0);
            var cornerZ = sourceZ + (directionZ > 0 ? 1 : 0);
            var occupancy = CellOccupancyResolver.ResolveSolidCornerOccupancy(
                world,
                cornerX,
                currentHeight,
                cornerZ);
            var sourceQuadrant = SolidCornerOccupancy.GetQuadrantIndex(
                cornerX,
                cornerZ,
                sourceX,
                sourceZ);
            var sourceBit = (byte)(1 << sourceQuadrant);
            if ((occupancy.TopMask & sourceBit) == 0)
            {
                return ResolveLocalSolidCornerFallback(
                    sourceX,
                    sourceY,
                    sourceZ,
                    directionX,
                    directionZ,
                    currentHeight);
            }

            var connectedTopMask = ExpandConnected(
                sourceBit,
                occupancy.TopMask);

            var resolvedHeight = currentHeight - 1;
            var exteriorBoundaryCount = 0;
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var bit = (byte)(1 << quadrant);
                if ((connectedTopMask & bit) == 0)
                {
                    continue;
                }

                occupancy.GetCellCoordinate(
                    quadrant,
                    out var cellX,
                    out var cellZ);
                var cellY = (currentHeight - 1)
                    / WorldGrid.HeightStepsPerCell;
                var cornerDirectionX = cellX < cornerX ? 1 : -1;
                var cornerDirectionZ = cellZ < cornerZ ? 1 : -1;

                var acrossXBit = (byte)(1 << (quadrant ^ 1));
                if ((connectedTopMask & acrossXBit) == 0)
                {
                    exteriorBoundaryCount++;
                    resolvedHeight = Math.Max(
                        resolvedHeight,
                        RoundHeightUnit(ResolveSolidBoundaryHeight(
                            cellX,
                            cellY,
                            cellZ,
                            cornerDirectionX,
                            0,
                            currentHeight,
                            cornerDirectionZ > 0 ? 1f : 0f)));
                }

                var acrossZBit = (byte)(1 << (quadrant ^ 2));
                if ((connectedTopMask & acrossZBit) == 0)
                {
                    exteriorBoundaryCount++;
                    resolvedHeight = Math.Max(
                        resolvedHeight,
                        RoundHeightUnit(ResolveSolidBoundaryHeight(
                            cellX,
                            cellY,
                            cellZ,
                            0,
                            cornerDirectionZ,
                            currentHeight,
                            cornerDirectionX > 0 ? 1f : 0f)));
                }
            }

            return exteriorBoundaryCount == 0
                ? currentHeight
                : resolvedHeight;
        }

        private int ResolveLocalSolidCornerFallback(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            int currentHeight)
        {
            var xEdgeHeight = RoundHeightUnit(
                ResolveSolidBoundaryHeight(
                    x,
                    y,
                    z,
                    directionX,
                    0,
                    currentHeight,
                    directionZ > 0 ? 1f : 0f));
            var zEdgeHeight = RoundHeightUnit(
                ResolveSolidBoundaryHeight(
                    x,
                    y,
                    z,
                    0,
                    directionZ,
                    currentHeight,
                    directionX > 0 ? 1f : 0f));
            return Math.Max(xEdgeHeight, zEdgeHeight);
        }

        private SurfaceBoundaryProfile ResolveWaterBoundary(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ)
        {
            GetWaterBoundaryCorners(
                x,
                y,
                z,
                directionX,
                directionZ,
                out var startCorner,
                out var endCorner);
            var startHeight = ResolveWaterCornerHeight(startCorner);
            var endHeight = ResolveWaterCornerHeight(endCorner);
            return new SurfaceBoundaryProfile(
                startHeight,
                Lerp(startHeight, endHeight, 0.2f),
                Lerp(startHeight, endHeight, 0.8f),
                endHeight);
        }

        private float ResolveWaterCornerHeight(in WaterCornerKey key)
        {
            if (waterCornerHeights.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var cellBottom = key.Y * WorldGrid.HeightStepsPerCell;
            var cellCeiling = cellBottom + WorldGrid.HeightStepsPerCell;
            var supportWeightedHeight = 0f;
            var supportWeight = 0f;
            var descentWeightedHeight = 0f;
            var descentWeight = 0f;
            var highestWaterBottom = (float)cellBottom;
            var lowestAmountTop = (float)cellCeiling;
            var hasWater = false;

            for (var offsetZ = -1; offsetZ <= 0; offsetZ++)
            for (var offsetX = -1; offsetX <= 0; offsetX++)
            {
                var cellX = key.X + offsetX;
                var cellZ = key.Z + offsetZ;
                if (!world.TryGetCell(cellX, key.Y, cellZ, out var cell))
                {
                    continue;
                }

                if (cell.HasWater)
                {
                    var direction = ResolveWaterDirection(
                        cellX,
                        key.Y,
                        cellZ,
                        cell);
                    var interval = ResolveWaterInterval(
                        cellX,
                        key.Y,
                        cellZ,
                        cell,
                        direction);
                    var stableWater =
                        cell.Water.Role == WaterRole.Source;
                    var connectsFromAbove = FallsWater(
                        cellX,
                        key.Y + 1,
                        cellZ);
                    var flowsDown =
                        (direction & FlowDirection.Down) != 0;
                    var downstreamCorner = flowsDown
                        && IsDownstreamCorner(
                            cellX,
                            cellZ,
                            key,
                            direction);
                    var supportsCorner = stableWater
                        || connectsFromAbove
                        || !downstreamCorner;
                    if (supportsCorner)
                    {
                        var supportHeight = connectsFromAbove
                            ? cellCeiling
                            : interval.TopUnits;
                        var weight = stableWater || connectsFromAbove
                            ? 10f
                            : 1f;
                        supportWeightedHeight += supportHeight * weight;
                        supportWeight += weight;
                    }
                    else
                    {
                        descentWeightedHeight += interval.BottomUnits;
                        descentWeight += 1f;
                    }

                    highestWaterBottom = Math.Max(
                        highestWaterBottom,
                        interval.BottomUnits);
                    lowestAmountTop = Math.Min(
                        lowestAmountTop,
                        interval.TopUnits);
                    hasWater = true;
                    continue;
                }

                if (!cell.HasTerrain
                    && IsDirectedIntoCornerSpace(key, cellX, cellZ))
                {
                    descentWeightedHeight += cellBottom;
                    descentWeight += 1f;
                }
            }

            var maximumHeight = Math.Max(
                highestWaterBottom,
                Math.Min(lowestAmountTop, cellCeiling));
            float height;
            if (!hasWater)
            {
                height = cellBottom;
            }
            else if (supportWeight > 0f)
            {
                height = Math.Clamp(
                    supportWeightedHeight / supportWeight,
                    highestWaterBottom,
                    maximumHeight);
            }
            else if (descentWeight > 0f)
            {
                height = Math.Clamp(
                    descentWeightedHeight / descentWeight,
                    highestWaterBottom,
                    maximumHeight);
            }
            else
            {
                height = highestWaterBottom;
            }

            if (CanCacheCorner(key))
            {
                waterCornerHeights[key] = height;
            }

            return height;
        }

        private bool CanCacheColumn(int x, int z) =>
            canCacheColumn == null || canCacheColumn(x, z);

        private bool CanCacheCorner(in WaterCornerKey key)
        {
            if (canCacheColumn == null)
            {
                return true;
            }

            for (var offsetZ = -1; offsetZ <= 0; offsetZ++)
            for (var offsetX = -1; offsetX <= 0; offsetX++)
            {
                var x = key.X + offsetX;
                var z = key.Z + offsetZ;
                if (world.ContainsColumn(x, z)
                    && canCacheColumn(x, z))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDirectedIntoCornerSpace(
            in WaterCornerKey key,
            int emptyX,
            int emptyZ)
        {
            for (var offsetZ = -1; offsetZ <= 0; offsetZ++)
            for (var offsetX = -1; offsetX <= 0; offsetX++)
            {
                var waterX = key.X + offsetX;
                var waterZ = key.Z + offsetZ;
                var deltaX = emptyX - waterX;
                var deltaZ = emptyZ - waterZ;
                if (Math.Abs(deltaX) + Math.Abs(deltaZ) != 1
                    || !world.TryGetCell(
                        waterX,
                        key.Y,
                        waterZ,
                        out var waterCell)
                    || !waterCell.HasWater)
                {
                    continue;
                }

                var direction = ResolveWaterDirection(
                    waterX,
                    key.Y,
                    waterZ,
                    waterCell);
                if (HasDirection(direction, deltaX, deltaZ))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDownstreamCorner(
            int cellX,
            int cellZ,
            in WaterCornerKey corner,
            FlowDirection direction)
        {
            var horizontal = direction
                & FlowDirection.Horizontal;
            if (horizontal == FlowDirection.None)
            {
                return true;
            }

            return ((horizontal & FlowDirection.East) != 0
                    && corner.X == cellX + 1)
                || ((horizontal & FlowDirection.West) != 0
                    && corner.X == cellX)
                || ((horizontal & FlowDirection.North) != 0
                    && corner.Z == cellZ + 1)
                || ((horizontal & FlowDirection.South) != 0
                    && corner.Z == cellZ);
        }

        private FlowDirection ResolveWaterDirection(
            int x,
            int y,
            int z,
            in CellData cell) =>
            flowState != null
                ? flowState.GetFlowDirection(x, y, z)
                : cell.Water.Flow;

        private HeightInterval ResolveWaterInterval(
            int x,
            int y,
            int z,
            in CellData cell,
            FlowDirection direction)
        {
            var logical = CellOccupancyResolver.GetWaterInterval(y, cell);
            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var ceiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            var fallingAbove = FallsWater(x, y + 1, z);
            if ((direction & FlowDirection.Down) != 0)
            {
                return new HeightInterval(
                    cellBottom,
                    fallingAbove ? ceiling : logical.TopUnits);
            }

            return fallingAbove
                ? new HeightInterval(logical.BottomUnits, ceiling)
                : logical;
        }

        private bool IsWaterTopExposed(
            int x,
            int y,
            int z,
            in HeightInterval interval)
        {
            var ceiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            if (interval.TopUnits < ceiling)
            {
                return true;
            }

            if (!world.TryGetCell(x, y + 1, z, out var above))
            {
                return true;
            }

            if (above.HasTerrain)
            {
                return false;
            }

            if (!above.HasWater)
            {
                return true;
            }

            var aboveDirection = ResolveWaterDirection(x, y + 1, z, above);
            var aboveInterval = ResolveWaterInterval(
                x,
                y + 1,
                z,
                above,
                aboveDirection);
            return aboveInterval.BottomUnits > interval.TopUnits;
        }

        private bool FallsWater(int x, int y, int z) =>
            world.TryGetCell(x, y, z, out var cell)
            && cell.HasWater
            && (ResolveWaterDirection(x, y, z, cell)
                & FlowDirection.Down) != 0;

        private bool ShouldPinSolidTopToWaterBoundary(int x, int y, int z)
        {
            if (!world.TryGetCell(x, y, z, out var cell))
            {
                return false;
            }

            if (cell.HasWater)
            {
                return true;
            }

            return cell.Terrain.SolidHeight == WorldGrid.HeightStepsPerCell
                && world.TryGetCell(x, y + 1, z, out var above)
                && above.HasWater
                && above.Terrain.SolidHeight == 0;
        }

        private bool TryGetConnectedSolidSurfaceHeight(
            int sourceX,
            int sourceZ,
            int targetX,
            int y,
            int targetZ,
            out int height)
        {
            if (!world.ContainsColumn(sourceX, sourceZ)
                || !world.ContainsColumn(targetX, targetZ))
            {
                height = 0;
                return false;
            }

            if (world.TryGetCell(targetX, y, targetZ, out var same)
                && same.HasTerrain)
            {
                height = ResolveContinuousSolidTop(targetX, y, targetZ, same);
                return true;
            }

            for (var scanY = y - 1; scanY >= 0; scanY--)
            {
                var source = world.GetCell(sourceX, scanY, sourceZ);
                var sourceCeiling = (scanY + 1)
                    * WorldGrid.HeightStepsPerCell;
                if (!source.HasTerrain
                    || GetSolidTop(scanY, source) != sourceCeiling)
                {
                    break;
                }

                var candidate = world.GetCell(targetX, scanY, targetZ);
                if (candidate.HasTerrain)
                {
                    height = GetSolidTop(scanY, candidate);
                    return true;
                }
            }

            height = 0;
            return false;
        }

        private int ResolveContinuousSolidTop(
            int x,
            int y,
            int z,
            in CellData cell)
        {
            var height = GetSolidTop(y, cell);
            var scanY = y;
            while (height == (scanY + 1) * WorldGrid.HeightStepsPerCell
                && world.TryGetCell(x, scanY + 1, z, out var above)
                && above.HasTerrain
                && (scanY + 1) * WorldGrid.HeightStepsPerCell == height)
            {
                scanY++;
                height = GetSolidTop(scanY, above);
            }

            return height;
        }

        private int GetSolidTop(int x, int y, int z)
        {
            var cell = world.GetCell(x, y, z);
            return GetSolidTop(y, cell);
        }

        private static int GetSolidTop(int y, in CellData cell) =>
            y * WorldGrid.HeightStepsPerCell + cell.Terrain.SolidHeight;

        private static SolidSurfaceProfile CreateFlatSolidProfile(int height)
        {
            var boundary = new SurfaceBoundaryProfile(
                height,
                height,
                height,
                height);
            return new SolidSurfaceProfile(
                height,
                boundary,
                boundary,
                boundary,
                boundary);
        }

        private static bool HasDirection(
            FlowDirection direction,
            int x,
            int z)
        {
            if (x > 0) return (direction & FlowDirection.East) != 0;
            if (x < 0) return (direction & FlowDirection.West) != 0;
            if (z > 0) return (direction & FlowDirection.North) != 0;
            if (z < 0) return (direction & FlowDirection.South) != 0;
            return false;
        }

        private static int RoundHeightUnit(float height) =>
            (int)Math.Round(height, MidpointRounding.AwayFromZero);

        private static float Lerp(float a, float b, float t) =>
            a + (b - a) * t;

        private static byte ExpandConnected(byte seedMask, byte availableMask)
        {
            var connected = (byte)(seedMask & availableMask);
            for (var pass = 0; pass < 4; pass++)
            {
                var expanded = connected;
                for (var quadrant = 0; quadrant < 4; quadrant++)
                {
                    if ((connected & (1 << quadrant)) == 0)
                    {
                        continue;
                    }

                    expanded |= quadrant switch
                    {
                        0 => 0b0110,
                        1 => 0b1001,
                        2 => 0b1001,
                        _ => 0b0110
                    };
                }

                expanded &= availableMask;
                if (expanded == connected)
                {
                    break;
                }

                connected = expanded;
            }

            return connected;
        }

        private static void GetWaterBoundaryCorners(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            out WaterCornerKey start,
            out WaterCornerKey end)
        {
            if (directionX != 0)
            {
                var gridX = directionX < 0 ? x : x + 1;
                start = new WaterCornerKey(gridX, y, z);
                end = new WaterCornerKey(gridX, y, z + 1);
                return;
            }

            var gridZ = directionZ < 0 ? z : z + 1;
            start = new WaterCornerKey(x, y, gridZ);
            end = new WaterCornerKey(x + 1, y, gridZ);
        }
    }
}
