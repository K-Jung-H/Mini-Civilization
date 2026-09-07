using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Globalization;
using System.Reflection;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Persistence;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
WaterSimulationChecks.Run();
var overlapCases = new List<HydrologyContribution>();
foreach (var kind in new[] { HydrologyContributionKind.Sea, HydrologyContributionKind.Basin, HydrologyContributionKind.River })
foreach (var wet in new[] { false, true })
foreach (var owner in new[] { 1, 2 })
{
    var featureKind = kind == HydrologyContributionKind.Sea ? HydrologyFeatureKind.Sea
        : kind == HydrologyContributionKind.River ? HydrologyFeatureKind.River : HydrologyFeatureKind.Lake;
    var key = HydrologyFeatureKey.FromIdentity(new WaterFeatureIdentity(featureKind, owner, 0, 0, 0));
    overlapCases.Add(new HydrologyContribution(kind, new HydrologyDrawingSample(key,
        wet ? WaterType.Lake : WaterType.None, owner, 10, 1, 0, wet)));
}
// Test unique-key subsets (one sample per feature/location), against the old sorted policy.
var random = new Random(812);
for (var iteration = 0; iteration < 500; iteration++)
{
    var subset = overlapCases.OrderBy(_ => random.Next()).GroupBy(c => c.Sample.Key)
        .Select(g => g.First()).Where(_ => random.Next(2) == 0).ToArray();
    var expected = HydrologyOverlapResolver.Resolve(subset);
    Require(Equals(expected, HydrologyOverlapResolver.Resolve(subset.Reverse().ToArray())), "overlap reversed");
    Parallel.For(0, 2, _ => Require(Equals(expected, HydrologyOverlapResolver.Resolve(subset)), "overlap parallel"));
}
var painter = new WaterMapPainter();
var output = painter.Paint(new PatternTileKey(0, 0), new PatternTileBounds(0, 0, 2, 1),
    new HydrologyDrawingSample?[] { null, overlapCases.First(c => c.Sample.HasWater).Sample }, default);
Require(!output.GetCell(0, 0).HasGroundOverride && output.GetCell(1, 0).HasWater, "record resolved samples");
Console.WriteLine("PASS 500 overlap subsets: reverse/parallel, resolved recording");
for (var ground = 0; ground <= 30; ground++)
for (var top = ground; top <= 35; top++)
{
    var heights = top == ground ? PatternColumnHeights.Dry(ground)
        : PatternColumnHeights.Wet(ground, top);
    heights.ValidateWorldHeight(7);
    int solidSum = 0, waterSum = 0;
    for (var y = 0; y <= heights.UsedCellCount; y++)
    {
        var solid = heights.SolidAt(y);
        var water = heights.WaterAt(y);
        Require(solid + water <= 5, "column capacity");
        solidSum += solid; waterSum += water;
        Require(WaterAmount.ToRenderFill(WaterAmount.FromRenderFill(water, 5 - solid),
            5 - solid) == water, "Filled Amount roundtrip");
    }
    Require(solidSum == ground && waterSum == top - ground, "column volume reconstruction");
    var resolved = HydrologyPatternCell.CreateResolved(heights,
        heights.HasWater ? WaterType.Lake : WaterType.None, 0, 1, 0);
    var combined = new CombinedPatternCell(new TerrainPatternCell(TerrainPatternType.Smooth, 0, 0, 0), resolved);
    var restored = PatternHeightQuantization.FromCurrentPattern(combined);
    Require(restored.Ground == heights.Ground && restored.WaterSurface == heights.WaterSurface,
        "resolved output survives map/materializer boundary");
}
void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    catch (InvalidOperationException) { return; }
    catch (OverflowException) { return; }
    throw new Exception("invalid height accepted");
}
Reject(() => PatternColumnHeights.Wet(5, 5));
Reject(() => PatternColumnHeights.Wet(5, 4));
Reject(() => PatternColumnHeights.Dry(-1));
Reject(() => PatternColumnHeights.Wet(34, 36).ValidateWorldHeight(7));
Reject(() => PatternHeightQuantization.Round(float.NaN));
Reject(() => PatternHeightQuantization.Round(float.PositiveInfinity));
Reject(() => PatternHeightQuantization.Round(float.MaxValue));
var shallow = new CombinedPatternCell(new TerrainPatternCell(TerrainPatternType.Smooth, 0, 0, 0),
    HydrologyPatternCell.CreateWater(WaterType.Lake, 0, 160.1f, 160.4f, 1, 0));
Require(!PatternHeightQuantization.FromCurrentPattern(shallow).HasWater,
    "current adapter does not silently inflate shallow water");
Require(PatternHeightQuantization.Round(1.5f) == 2 && PatternHeightQuantization.Round(-1.5f) == -2,
    "rounding compatibility");
Console.WriteLine("PASS integer column contract, map roundtrip, capacity/volume, Amount, invalid and shallow heights");
var asset = LoadSettings<TerrainPatternSettings>(Path.Combine(root, "Assets/Game/World/Settings/TerrainPatternSettings.asset"));
HydrologyChecks.Run(asset, LoadSettings<HydrologyFeatureSettings>(Path.Combine(root, "Assets/Game/World/Settings/HydrologyFeatureSettings.asset")));
foreach (var seed in new[] { 0, 1, -1 })
{
    var settings = asset.CreateData(seed);
    var evaluator = new TerrainPatternEvaluator(settings);
    var samples = new Dictionary<(int, int), TerrainPatternSample>();
    for (var z = -256; z < 256; z += 4)
    for (var x = -256; x < 256; x += 4)
        samples[(x, z)] = evaluator.EvaluateSample(x, z);
    var reverse = new TerrainPatternEvaluator(settings);
    foreach (var pair in samples.Reverse())
        Equal(pair.Value, reverse.EvaluateSample(pair.Key.Item1, pair.Key.Item2), "reverse");
    Parallel.ForEach(samples.Chunk(256), new ParallelOptions { MaxDegreeOfParallelism = 2 }, batch =>
    {
        var worker = new TerrainPatternEvaluator(settings);
        foreach (var pair in batch)
            Equal(pair.Value, worker.EvaluateSample(pair.Key.Item1, pair.Key.Item2), "parallel/fresh cache");
    });

    var world = new WorldSettingsData(seed, WorldType.Infinite, 1, 8, 10, 15, 10, 1, 5, 72,
        new WaterFlowRules(.01f, .05f, .05f));
    foreach (var span in new[] { 1, 2 })
    {
        var tileSettings = new TerrainPatternSettingsData(seed, span, settings.TerrainBaseHeight,
            settings.NoiseRouter, settings.Region, settings.BaseSurface, settings.Smooth,
            settings.Rugged, settings.Mountain, settings.Canyon);
        var grid = new PatternTileGridSettingsData(world, span);
        var builder = new TerrainPatternTileBuilder(grid, tileSettings);
        foreach (var coordinate in new[] { (-129, 127), (-1, -1), (0, 0), (7, 8), (127, -128) })
        {
            var tile = builder.Build(grid.GetKeyForCell(coordinate.Item1, coordinate.Item2));
            var cell = tile.GetCell(coordinate.Item1, coordinate.Item2);
            var expected = evaluator.EvaluateSample(coordinate.Item1, coordinate.Item2);
            Require(cell.BaseSurfaceHeight == expected.BaseSurfaceHeight
                && cell.DetailSurfaceHeight == expected.DetailSurfaceHeight, "tile partition");
            var slope = TerrainPatternEvaluator.CalculateSlope(
                evaluator.EvaluateSample(coordinate.Item1 - 1, coordinate.Item2).SurfaceHeight,
                evaluator.EvaluateSample(coordinate.Item1 + 1, coordinate.Item2).SurfaceHeight,
                evaluator.EvaluateSample(coordinate.Item1, coordinate.Item2 - 1).SurfaceHeight,
                evaluator.EvaluateSample(coordinate.Item1, coordinate.Item2 + 1).SurfaceHeight);
            Require(cell.Slope == slope, "tile halo slope");
        }
    }
    Console.WriteLine($"PASS seed={seed}: {samples.Count} samples, reverse/parallel/cache, tile spans 1/2");
}

// Unwarped lattice vertices have four equidistant regions. Test real evaluator limits,
// including candidate-window transitions and settings beyond the former 3x3 assumption.
foreach (var jitter in new[] { 0f, .35f, 1.5f })
foreach (var width in new[] { 10f, 200f })
{
    var s = asset.CreateData(0);
    var r = s.Region;
    var region = new TerrainRegionData(r.SizeCells, jitter, r.WarpField, 0, width,
        r.InteriorReachRatio, r.SmoothShare, r.RuggedShare, r.MountainShare, r.CanyonShare, r.SeaShare);
    var settings = new TerrainPatternSettingsData(0, 1, s.TerrainBaseHeight, s.NoiseRouter,
        region, s.BaseSurface, s.Smooth, s.Rugged, s.Mountain, s.Canyon);
    var evaluator = new TerrainPatternEvaluator(settings);
    double maximumJump = 0;
    for (var gz = -2; gz <= 2; gz++)
    for (var gx = -2; gx <= 2; gx++)
    {
        var x = gx * r.SizeCells;
        var z = gz * r.SizeCells;
        var left = evaluator.EvaluateSample(x - 0.00001, z - 0.00001).SurfaceHeight;
        var right = evaluator.EvaluateSample(x + 0.00001, z + 0.00001).SurfaceHeight;
        maximumJump = Math.Max(maximumJump, Math.Abs(right - left));
        Require(Math.Abs(right - left) < .001f, $"lattice vertex continuity {gx},{gz}");
    }
    Console.WriteLine($"PASS continuity jitter={jitter} width={width}: max two-sided delta={maximumJump:R}");
}

// Independent bounded-vs-wide search comparison of the actual region blend.
// Inspect centers and contributions, but independently compute distances/support and normalized sums.
var probeSettings = asset.CreateData(1);
var flags = BindingFlags.NonPublic | BindingFlags.Instance;
var create = typeof(TerrainPatternEvaluator).GetMethod("CreateCandidate", flags)!;
var contributionMethod = typeof(TerrainPatternEvaluator).GetMethod("SampleContribution", flags)!;
var regionSeed = PatternNoise.DeriveSeed(1, "world-router-pattern-region");
var reg = probeSettings.Region;
for (var z = -128; z <= 128; z += 32)
for (var x = -128; x <= 128; x += 32)
{
    // Disable warp for an independently known search center.
    var plainRegion = new TerrainRegionData(reg.SizeCells, reg.CenterJitter, reg.WarpField, 0,
        reg.BoundaryBlendCells, reg.InteriorReachRatio, reg.SmoothShare, reg.RuggedShare,
        reg.MountainShare, reg.CanyonShare, reg.SeaShare);
    var plain = new TerrainPatternEvaluator(new TerrainPatternSettingsData(1, 1,
        probeSettings.TerrainBaseHeight, probeSettings.NoiseRouter, plainRegion,
        probeSettings.BaseSurface, probeSettings.Smooth, probeSettings.Rugged,
        probeSettings.Mountain, probeSettings.Canyon));
    var points = new List<(long X, long Z, double D)>();
    var cx = (long)Math.Floor(x / (double)reg.SizeCells);
    var cz = (long)Math.Floor(z / (double)reg.SizeCells);
    for (var dz = -8; dz <= 8; dz++)
    for (var dx = -8; dx <= 8; dx++)
    {
        var px = cx + dx; var pz = cz + dz;
        var sx = (px + .5) * reg.SizeCells + PatternNoise.SignedValue01(px, pz,
            unchecked(regionSeed + 101)) * reg.CenterJitter * reg.SizeCells;
        var sz = (pz + .5) * reg.SizeCells + PatternNoise.SignedValue01(px, pz,
            unchecked(regionSeed + 211)) * reg.CenterJitter * reg.SizeCells;
        points.Add((px, pz, Math.Sqrt((x - sx) * (x - sx) + (z - sz) * (z - sz))));
    }
    var nearest = points.Min(p => p.D);
    double total = 0, expectedBase = 0, expectedDetail = 0;
    foreach (var point in points)
    {
        var t = Math.Clamp(1 - (point.D - nearest) / (2 * reg.BoundaryBlendCells), 0, 1);
        var w = t * t * t * (10 - 15 * t + 6 * t * t);
        if (w == 0) continue;
        var candidate = create.Invoke(plain, new object[] { point.X, point.Z, 0f, 0f });
        var c = contributionMethod.Invoke(plain, new object[] { candidate!, (double)x, (double)z })!;
        expectedBase += (float)c.GetType().GetProperty("BaseHeight")!.GetValue(c)! * w;
        expectedDetail += (float)c.GetType().GetProperty("DetailHeight")!.GetValue(c)! * w;
        total += w;
    }
    plain.EvaluateSample(x, z);
    var blended = typeof(TerrainPatternEvaluator).GetMethod("SampleBlendedContribution", flags)!
        .Invoke(plain, new object[] { (double)x, (double)z })!;
    Require(Math.Abs((float)blended.GetType().GetProperty("BaseHeight")!.GetValue(blended)!
        - expectedBase / total) < .0001, "wide search base");
    Require(Math.Abs((float)blended.GetType().GetProperty("DetailHeight")!.GetValue(blended)!
        - expectedDetail / total) < .0001, "wide search detail");
}
Console.WriteLine("PASS independent wide search (81 positions)");

// Locate a genuine secondary-region switch in the project's seed/settings, then bisect it.
var seamEvaluator = new TerrainPatternEvaluator(asset.CreateData(0));
var foundSeam = false;
for (var z = -256; z < 256 && !foundSeam; z += 8)
{
    var previous = seamEvaluator.EvaluateSample(-256, z);
    for (double x = -255.5; x < 256 && !foundSeam; x += .5)
    {
        var next = seamEvaluator.EvaluateSample(x, z);
        if (previous.SeaRegionKey == next.SeaRegionKey
            && previous.SecondarySeaRegionKey != next.SecondarySeaRegionKey)
        {
            double lo = x - .5, hi = x;
            for (var i = 0; i < 40; i++)
            {
                var mid = (lo + hi) / 2;
                if (seamEvaluator.EvaluateSample(mid, z).SecondarySeaRegionKey == previous.SecondarySeaRegionKey)
                    lo = mid;
                else hi = mid;
            }
            var a = seamEvaluator.EvaluateSample(lo - .00001, z);
            var b = seamEvaluator.EvaluateSample(hi + .00001, z);
            double OldBlend(TerrainPatternSample s) => s.SecondaryTerrainSurfaceHeight
                + (s.PrimaryTerrainSurfaceHeight - s.SecondaryTerrainSurfaceHeight) * s.PrimaryInfluence;
            var oldJump = Math.Abs(OldBlend(a) - OldBlend(b));
            if (oldJump > .1)
            {
                var newJump = Math.Abs(a.SurfaceHeight - b.SurfaceHeight);
                Require(newJump < .001, "actual secondary-switch continuity");
                Console.WriteLine($"PASS actual seam seed=0 x={(lo + hi) / 2:R} z={z}: old two-region delta={oldJump:R}, new delta={newJump:R}");
                foundSeam = true;
            }
        }
        previous = next;
    }
}
Require(foundSeam, "actual seam fixture discovery");

using (var stream = new MemoryStream())
{
    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
    WorldGenerationSaveHeader.Write(writer);
    writer.Write(123456); stream.Position = 0;
    using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
    WorldGenerationSaveHeader.Read(reader);
    Require(reader.ReadInt32() == 123456, "header payload alignment");
    var valid = stream.ToArray();
    foreach (var bytes in new[] { BitConverter.GetBytes(0x57534433u),
        valid[..4].Concat(BitConverter.GetBytes(0)).ToArray(),
        valid[..4].Concat(BitConverter.GetBytes(1)).ToArray(),
        valid[..4].Concat(BitConverter.GetBytes(WorldGenerationSaveHeader.CurrentGenerationVersion - 1)).ToArray(),
        valid[..4].Concat(BitConverter.GetBytes(WorldGenerationSaveHeader.CurrentGenerationVersion + 1)).ToArray(), new byte[4], valid[..6] })
    {
        using var invalidStream = new MemoryStream(bytes);
        using var invalidReader = new BinaryReader(invalidStream);
        try { WorldGenerationSaveHeader.Read(invalidReader); throw new Exception("invalid header accepted"); }
        catch (IOException) { }
        catch (InvalidDataException) { }
    }
}
Console.WriteLine("PASS save headers: current/legacy/older/newer/corrupt/truncated");

static void Require(bool condition, string label)
{
    if (!condition) throw new Exception(label);
}
static void Equal(TerrainPatternSample a, TerrainPatternSample b, string label)
{
    foreach (var property in typeof(TerrainPatternSample).GetProperties())
        Require(Equals(property.GetValue(a), property.GetValue(b)), label + ": " + property.Name);
}
static T LoadSettings<T>(string path)
{
    // This asset contains nested scalar fields only; reject missing/unsupported authoring data.
    var entries = new Dictionary<string, string>();
    var stack = new List<(int Indent, string Key)>();
    foreach (var line in File.ReadLines(path))
    {
        var indent = line.TakeWhile(c => c == ' ').Count();
        if (indent < 2 || !line.Contains(':')) continue;
        var parts = line.Trim().Split(':', 2);
        if (parts[0].StartsWith("m_")) continue;
        while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
        if (string.IsNullOrWhiteSpace(parts[1])) { stack.Add((indent, parts[0])); continue; }
        entries[string.Join('.', stack.Select(s => s.Key).Append(parts[0]))] = parts[1].Trim();
    }
    object Read(Type type, string prefix)
    {
        if (type.IsEnum) return Enum.ToObject(type, int.Parse(entries[prefix], CultureInfo.InvariantCulture));
        if (type == typeof(float)) return float.Parse(entries[prefix], CultureInfo.InvariantCulture);
        if (type == typeof(int)) return int.Parse(entries[prefix], CultureInfo.InvariantCulture);
        var result = Activator.CreateInstance(type)!;
        foreach (var field in type.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            field.SetValue(result, Read(field.FieldType, prefix.Length == 0 ? field.Name : prefix + "." + field.Name));
        return result;
    }
    return (T)Read(typeof(T), "");
}
