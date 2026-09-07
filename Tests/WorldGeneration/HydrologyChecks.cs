using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.WaterFlow;

internal static class HydrologyChecks
{
    private static readonly (int X, int Z)[] Directions = { (-1, 0), (1, 0), (0, -1), (0, 1) };

    public static void Run(TerrainPatternSettings terrainAsset, HydrologyFeatureSettings asset)
    {
        var world = new WorldSettingsData(0, WorldType.Infinite, 1, 8, 10, 15, 10, 1, 5, 72,
            new WaterFlowRules(.01f, .05f, .05f));
        var settings = asset.CreateData(world);
        // Independent exhaustive integer objective verifies breakpoint minimization,
        // including the constrained low/high world-height cases.
        var factory = new WaterBrushFactory(settings);
        RiverPathChecks.Run(factory,settings, new Reader((x,z) => 140f),
            new Reader((x,z) => 140f + 65f * MathF.Sin(x * .06f) * MathF.Cos(z * .04f)));
        var chooseSurface = typeof(WaterBrushFactory).GetMethod("SelectBasinSurface", BindingFlags.NonPublic | BindingFlags.Instance);
        var rng = new Random(901);
        for (var fixture = 0; fixture < 30; fixture++)
        {
            var footprint = new Dictionary<long, TerrainPatternCell>();
            for (var z = 0; z < 3; z++)
            for (var x = 0; x < 4; x++)
                footprint.Add(Key(x, z), new TerrainPatternCell(TerrainPatternType.Smooth,
                    fixture == 0 ? 0 : fixture == 1 ? 1 : fixture == 2 ? 499 : 65 + (float)rng.NextDouble() * 70, 0, 0));
            var selected = (float)chooseSurface.Invoke(factory, new object[] { footprint });
            var minimum = 1;
            var maximum = Math.Min(499, (int)Math.Floor(footprint.Values.Average(v => (double)v.SurfaceHeight)));
            double Cost(int surface)
            {
                double cost = 0;
                foreach (var pair in footprint)
                {
                    var delta = pair.Value.SurfaceHeight - (double)surface;
                    cost += delta >= 0 ? delta * settings.Basins.CutCost : -delta * settings.Basins.FillCost;
                    var x = (int)(pair.Key >> 32); var z = (int)pair.Key;
                    if (x == 0 || x == 3 || z == 0 || z == 2) cost += Math.Abs(delta - 1) * settings.Basins.RimCost;
                }
                return cost;
            }
            var best = maximum < minimum ? 0
                : Enumerable.Range(minimum, maximum - minimum + 1).OrderBy(Cost).ThenBy(v => v).First();
            Check(selected == best, "basin surface objective minimum");
            Check(selected <= maximum, "basin original Terrain mean upper bound");
            var reversedFootprint = footprint.Reverse().ToDictionary(p => p.Key, p => p.Value);
            Check(selected == (float)chooseSurface.Invoke(factory,new object[] { reversedFootprint }),
                "surface independent of enumeration");
        }
        Console.WriteLine("PASS basin surface datum: 30 exhaustive objective comparisons including world bounds");
        var flat = new Reader((x, z) => 60f);
        var core = new PatternTileBounds(-8, -8, 16, 8);
        BasinWaterBrush Rect(int id, int left, int right, int bottom, int top, int surface)
        {
            var inside = new Dictionary<long, float>();
            var shore = new Dictionary<long, float>();
            for (var z = bottom - 5; z <= top + 5; z++)
            for (var x = left - 5; x <= right + 5; x++)
            {
                var distance = Math.Max(0, left - x) + Math.Max(0, x - right)
                    + Math.Max(0, bottom - z) + Math.Max(0, z - top);
                if (distance == 0)
                    inside[Key(x, z)] = Math.Min(Math.Min(x - left, right - x), Math.Min(z - bottom, top - z)) == 0 ? 0 : 1;
                else if (distance <= 5) shore[Key(x, z)] = settings.Basins.ShoreTransition.Evaluate(1 - (distance - 1f) / 4);
            }
            return new BasinWaterBrush(Feature(id), WaterType.Lake,
                new BasinDrawingGeometry(surface, inside, shore), 10, 0, settings.Basins);
        }
        var cases = new[]
        {
            new[] { Rect(1, -5, -1, -3, 3, 80) },
            new[] { Rect(1, -5, -1, -3, 3, 80), Rect(2, 1, 5, -3, 3, 120) },
            new[] { Rect(1, -5, -1, -3, 3, 80), Rect(2, 0, 5, -3, 3, 80) },
            new[] { Rect(1, -5, -1, -3, 3, 80), Rect(2, 0, 5, -3, 3, 120) },
            new[] { Rect(1, -5, 2, -3, 3, 80), Rect(2, 0, 6, -3, 3, 120), Rect(3, -1, 5, -1, 5, 100) },
            new[] { Rect(1, -2, -1, -2, -1, 80), Rect(2, 0, 1, 0, 1, 120) }
        };
        var checkedEdges = 0;
        foreach (var basins in cases)
        {
            var collector = new HydrologyContributionCollector(settings, flat);
            var map = collector.Resolve(core, basins, Array.Empty<RiverWaterBrush>(), default);
            var reversed = collector.Resolve(core, basins.Reverse().ToArray(), Array.Empty<RiverWaterBrush>(), default);
            Check(map.SequenceEqual(reversed), "basin permutation");
            foreach (var x0 in new[] { -8, 0, 8 })
            {
                var part = new PatternTileBounds(x0, -8, x0 + 8, 8);
                var read = HydrologyHeightSolver.ReadBounds(part);
                var localBrushes = basins.Where(b => b.Bounds.Value.Intersects(read)).ToArray();
                var split = collector.Resolve(part, localBrushes, Array.Empty<RiverWaterBrush>(), default);
                for (var z = -8; z < 8; z++)
                for (var x = x0; x < x0 + 8; x++)
                    Check(Equals(split[x - x0 + part.Width * (z + 8)], At(map, core, x, z)), "basin tile halo");
            }
            for (var z = -7; z < 7; z++)
            for (var x = -7; x < 15; x++)
            {
                var sample = At(map, core, x, z);
                var expectedWet = basins.Any(b => b.TrySample(x, z, flat.GetCell(x, z), out var target) && target.HasWater);
                Check((sample.HasValue && sample.Value.HasWater) == expectedWet, "basin footprint union preserved");
                if (!sample.HasValue || !sample.Value.HasWater) continue;
                var a = Heights(sample, 60);
                Check(sample.Value.ResolvedHeights && a.HasWater, "resolved basin wet mask");
                foreach (var direction in Directions)
                {
                    var other = At(map, core, x + direction.X, z + direction.Z);
                    var b = Heights(other, 60);
                    if (b.HasWater && a.WaterSurface == b.WaterSurface)
                    {
                        Check(Math.Max(a.Ground, b.Ground) < Math.Min(a.WaterSurface, b.WaterSurface), "connected face aperture");
                        var aperture = false;
                        for (var y = 0; y < Math.Max(a.UsedCellCount, b.UsedCellCount); y++)
                        {
                            var donor = Cell(a, y); var target = Cell(b, y);
                            if (donor.HasWater && target.HasWater && WaterFlowReachability.CanReachHorizontally(
                                new CellCoordinate(x, y, z), donor, donor.Water,
                                new CellCoordinate(x + direction.X, y, z + direction.Z), target, 1)) aperture = true;
                        }
                        Check(aperture, "real flow contact");
                    }
                    else if (!b.HasWater)
                    {
                        Check(b.Ground >= a.WaterSurface + 1, "exterior one-Filled barrier");
                        for (var y = 0; y < a.UsedCellCount; y++)
                        {
                            var donor = Cell(a, y); var target = Cell(b, y);
                            if (!donor.HasWater) continue;
                            Check(!WaterFlowReachability.CanReachHorizontally(
                                new CellCoordinate(x, y, z), donor, donor.Water,
                                new CellCoordinate(x + direction.X, y, z + direction.Z), target, 1), "no outgoing basin edge");
                        }
                    }
                    checkedEdges++;
                }
            }
            WaterSimulationChecks.VerifyGenerated((x,z) => core.Contains(x,z)
                ? Heights(At(map,core,x,z),60) : PatternColumnHeights.Dry(60), 30, 7);
        }
        Console.WriteLine($"PASS basin fixtures: separate/same/drop/triple/diagonal, {checkedEdges} actual-flow edges, order/tile halo");
        var triple = new HydrologyContributionCollector(settings, flat).Resolve(core, cases[4], Array.Empty<RiverWaterBrush>(), default);
        Check(At(triple, core, -5, 0).Value.WaterSurfaceHeight == 80
            && At(triple, core, 6, 0).Value.WaterSurfaceHeight == 120, "connected basins are not flattened globally");
        var overlapSurface = At(triple, core, 1, 0).Value.WaterSurfaceHeight;
        Check(overlapSurface == 80, "overlap preserves the receiving basin surface upper bound");

        // Production River Brush: no bank-induced surface lowering, finite-width boundary.
        var r = settings.River;
        var fixedRiver = new RiverFeatureSettingsData(r.CandidateLatticeSpacingCells, r.AnchorJitterCells,
            r.Occurrence, r.MinimumNodeCount, r.AverageNodeCount, r.MaximumNodeCount, r.NodeTurnDegrees,
            r.CurvatureField, r.TerrainHeightChangeReferenceCells, r.TerrainAvoidanceStrength,
            r.MaximumDescendantBranchCount, r.BranchOccurrencePerNode, r.BranchNodeCountRatio,
            r.MinimumBranchNodeCount, r.BranchOpeningAngleDegrees, r.BranchWidthRatio, r.BranchDepthRatio,
            r.WidthField, new TerrainRangeData(6, 6), r.CrossSection, new TerrainRangeData(10, 10),
            new TerrainRangeData(2, 2), r.RiverbedField, new TerrainRangeData(0, 0));
        var tree = new RiverBoneTree(new[] { new RiverBoneStroke(new[] {
            new RiverBoneNode(new WaterMapPoint(-8, 0), 0, new WaterMapPoint(1, 0)),
            new RiverBoneNode(new WaterMapPoint(8, 0), 16, new WaterMapPoint(1, 0)) }, 123, -1, -1, 1, 1) });
        foreach (var outer in new[] { 50f, 170f, 250f })
        {
            var terrain = new Reader((x, z) => z == 0 ? 170 : outer);
            var brush = new RiverWaterBrush(Feature(9, HydrologyFeatureKind.River), tree, fixedRiver, 60, terrain);
            Check(brush.TrySample(0, 0, terrain.GetCell(0, 0), out var center), "river center");
            Check(center.WaterSurfaceHeight == 168, "river surface independent of banks");
            Check(center.GroundHeight == 160, "river center depth");
            Check(brush.TrySample(0,3,terrain.GetCell(0,3),out var bank)
                && !bank.HasWater && bank.GroundHeight == 169, "river dry bank cut/fill");
            Check(brush.TrySample(0,7,terrain.GetCell(0,7),out var edge)
                && !edge.HasWater && edge.GroundHeight == outer, "river outer support returns to Terrain");
            Check(!brush.TrySample(0,8,terrain.GetCell(0,8),out _), "river finite support");
            Check(HydrologyHeightSolver.RiverGround(168, 8, 0) == 167, "water cavity edge depth");
            for (var z = -2; z <= 2; z++)
                if (brush.TrySample(0, z, terrain.GetCell(0, z), out var sample))
                {
                    var resolved = HydrologyHeightSolver.ResolveHeights(sample);
                    var heights = Heights(resolved, 0);
                    Check(resolved.HasWater == heights.HasWater, "integer river mask");
                }
            var riverCore = new PatternTileBounds(-12,-12,16,16);
            var riverMap = new HydrologyContributionCollector(settings,terrain).Resolve(riverCore,
                Array.Empty<BasinWaterBrush>(),new[] { brush },default);
            WaterSimulationChecks.VerifyGenerated((x,z) => Heights(At(riverMap,riverCore,x,z),
                PatternHeightQuantization.Round(terrain.GetCell(x,z).SurfaceHeight)), 55, 7);
        }
        Console.WriteLine("PASS production River brush: low/equal/high bank, center datum/depth, boundary support, integer mask");

        var branchTree = new RiverBoneTree(new[] {
            new RiverBoneStroke(new[] {
                new RiverBoneNode(new WaterMapPoint(-8,0),0,new WaterMapPoint(1,0)),
                new RiverBoneNode(new WaterMapPoint(0,0),8,new WaterMapPoint(1,0)),
                new RiverBoneNode(new WaterMapPoint(8,0),16,new WaterMapPoint(1,0)) },123,-1,-1,1,1),
            new RiverBoneStroke(new[] {
                new RiverBoneNode(new WaterMapPoint(0,0),0,new WaterMapPoint(0,1)),
                new RiverBoneNode(new WaterMapPoint(4,6),8,new WaterMapPoint(0,1)) },456,0,1,1,1)
        });
        var sloped = new Reader((x,z) => 170 - x * 5 - z * 2);
        var branched = new RiverWaterBrush(Feature(10,HydrologyFeatureKind.River),branchTree,fixedRiver,60,sloped);
        var branchBounds = new PatternTileBounds(-12,-12,16,16);
        var branchMap = new HydrologyContributionCollector(settings,sloped).Resolve(branchBounds,
            Array.Empty<BasinWaterBrush>(),new[] { branched },default);
        WaterSimulationChecks.VerifyGenerated((x,z) => Heights(At(branchMap,branchBounds,x,z),
            PatternHeightQuantization.Round(sloped.GetCell(x,z).SurfaceHeight)),55,7);
        Console.WriteLine("PASS generated Basin/River maps through actual Materializer and WaterFlowResolver: exterior exclusion and Dynamic drop paths, including sloped branch");

        // Actual factory + drawer + sea + collector + painter; requests on opposite sides of a tile edge.
        var keys = new[] { new PatternTileKey(-1, -1), new PatternTileKey(0, 0), new PatternTileKey(1, 0), new PatternTileKey(0, 1),
            new PatternTileKey(-16, -16), new PatternTileKey(16, 16), new PatternTileKey(16, -16), new PatternTileKey(-16, 16) };
        var terrainSettings = terrainAsset.CreateData(0);
        var grid = new PatternTileGridSettingsData(world, 1);
        var cache = new PatternMapStore();
        var terrainBuilder = new TerrainPatternTileBuilder(grid,terrainSettings);
        var cacheReader = new TerrainPatternMapReader(grid,cache,terrainBuilder);
        var cachedDrawer = new HydrologyPatternDrawer(grid,settings,cacheReader,cache.WaterBrushes);
        var cacheKey = keys[0];
        cache.GetOrBuildTerrain(cacheKey,terrainBuilder);
        var beforeEviction = cachedDrawer.Draw(cacheKey);
        cache.SealHydrology(beforeEviction);
        cache.Retain(new HashSet<PatternTileKey> { cacheKey });
        Check(cache.TryGetPair(cacheKey,out _),"demanded pair retained");
        cache.Retain(new HashSet<PatternTileKey>());
        Check(cache.TerrainTileCount == 0 && cache.HydrologyTileCount == 0,"idle cache release");
        cache.GetOrBuildTerrain(cacheKey,terrainBuilder);
        EqualTile(beforeEviction,cachedDrawer.Draw(cacheKey));
        Console.WriteLine("PASS cache demand retention, release and real map regeneration equality");
        HydrologyPatternDrawer Drawer() => new(grid, settings, new EvaluatorReader(terrainSettings), new WaterBrushCatalog());
        var sequential = Drawer();
        var reference = keys.Select(k => sequential.Draw(k)).ToArray();
        var reverseDrawer = Drawer();
        foreach (var index in Enumerable.Range(0, keys.Length).Reverse())
            EqualTile(reference[index], reverseDrawer.Draw(keys[index]));
        Parallel.For(0, keys.Length, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i => EqualTile(reference[i], Drawer().Draw(keys[i])));
        var largeDrawer = new HydrologyPatternDrawer(new PatternTileGridSettingsData(world, 2), settings,
            new EvaluatorReader(terrainSettings), new WaterBrushCatalog());
        foreach (var tile in reference)
        {
            var larger = largeDrawer.Draw(new PatternTileKey(WorldCoordinateUtility.FloorDivide(tile.Key.X, 2),
                WorldCoordinateUtility.FloorDivide(tile.Key.Z, 2)));
            for (var z = tile.Bounds.MinimumZ; z < tile.Bounds.MaximumZExclusive; z++)
            for (var x = tile.Bounds.MinimumX; x < tile.Bounds.MaximumXExclusive; x++)
            {
                var a = tile.GetCell(x, z); var b = larger.GetCell(x, z);
                Check(a.GroundHeight == b.GroundHeight && a.WaterSurfaceHeight == b.WaterSurfaceHeight
                    && a.HasWater == b.HasWater && a.WaterType == b.WaterType
                    && a.HasGroundOverride == b.HasGroundOverride, "hydrology partition heights");
                if (a.HasGroundOverride) Check(tile.GetFeature(a.FeatureIndex).Equals(larger.GetFeature(b.FeatureIndex)), "partition feature identity");
            }
        }
        Console.WriteLine("PASS actual Hydrology factory/drawer: 8 tiles, reverse/fresh/parallel and spans 1/2; pixel fields/feature identities");
    }

    private static void EqualTile(HydrologyPatternTile a, HydrologyPatternTile b)
    {
        Check(a.FeatureCount == b.FeatureCount, "feature count");
        for (var i = 0; i < a.FeatureCount; i++) Check(a.GetFeature(i).Equals(b.GetFeature(i)), "feature identity");
        for (var z = a.Bounds.MinimumZ; z < a.Bounds.MaximumZExclusive; z++)
        for (var x = a.Bounds.MinimumX; x < a.Bounds.MaximumXExclusive; x++)
            Check(a.GetCell(x, z).Equals(b.GetCell(x, z)), "Hydrology pixel determinism");
    }
    private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    private static HydrologyFeatureKey Feature(int id, HydrologyFeatureKind kind = HydrologyFeatureKind.Lake) =>
        HydrologyFeatureKey.FromIdentity(new WaterFeatureIdentity(kind, id, 0, 0, 0));
    private static HydrologyDrawingSample? At(HydrologyDrawingSample?[] samples, PatternTileBounds b, int x, int z) =>
        samples[x - b.MinimumX + b.Width * (z - b.MinimumZ)];
    private static PatternColumnHeights Heights(HydrologyDrawingSample? s, int terrain) => !s.HasValue
        ? PatternColumnHeights.Dry(terrain) : s.Value.HasWater
            ? PatternColumnHeights.Wet((int)s.Value.GroundHeight, (int)s.Value.WaterSurfaceHeight)
            : PatternColumnHeights.Dry((int)s.Value.GroundHeight);
    private static CellData Cell(PatternColumnHeights h, int y)
    {
        var solid = h.SolidAt(y);
        return new CellData { Terrain = new TerrainData { SolidHeight = solid },
            Water = new WaterData { Amount = WaterAmount.FromRenderFill(h.WaterAt(y), 5 - solid), Role = WaterRole.Source } };
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private sealed class Reader : ITerrainPatternMapReader
    {
        private readonly Func<int, int, float> height;
        public Reader(Func<int, int, float> height) { this.height = height; }
        public TerrainPatternCell GetCell(int x, int z) => new(TerrainPatternType.Smooth, height(x, z), 0, 0);
    }
    private sealed class EvaluatorReader : ITerrainPatternMapReader
    {
        private readonly TerrainPatternSettingsData settings;
        [ThreadStatic] private static TerrainPatternEvaluator evaluator;
        [ThreadStatic] private static TerrainPatternSettingsData activeSettings;
        public EvaluatorReader(TerrainPatternSettingsData settings) { this.settings = settings; }
        public TerrainPatternCell GetCell(int x, int z)
        {
            if (!ReferenceEquals(settings, activeSettings)) { evaluator = new TerrainPatternEvaluator(settings); activeSettings = settings; }
            return evaluator.ToCell(evaluator.EvaluateSample(x, z), 0);
        }
    }
}
