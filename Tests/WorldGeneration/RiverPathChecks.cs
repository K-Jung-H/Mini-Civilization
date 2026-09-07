using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Generation.Patterns;

internal static class RiverPathChecks
{
    public static void Run(WaterBrushFactory factory, HydrologyFeatureSettingsData settings,
        params ITerrainPatternMapReader[] terrains)
    {
        var field = typeof(RiverWaterBrush).GetField("boneTree",BindingFlags.NonPublic | BindingFlags.Instance);
        var total = 0;
        var curved = 0;
        var minReach = double.PositiveInfinity;
        var broadBends = 0;
        var revisits = 0;
        double largestLobe = 0;
        foreach (var terrain in terrains)
        for (var seed = 0; seed < 100; seed++)
        {
            var x = seed % 10 - 5; var z = seed / 10 - 5;
            RiverBoneTree Make() => (RiverBoneTree)field.GetValue(factory.CreateRiver(x,z,terrain));
            var tree = Make();
            Check(tree.Strokes.Count <= settings.River.MaximumDescendantBranchCount + 1,"root descendant budget");
            Check(tree.Strokes[0].Nodes.Length >= settings.River.MinimumNodeCount
                && tree.Strokes[0].Nodes.Length <= settings.River.MaximumNodeCount,"root length range");
            foreach (var stroke in tree.Strokes)
            {
                total++;
                var nodes = stroke.Nodes;
                var dx = nodes[^1].Point.X - nodes[0].Point.X;
                var dz = nodes[^1].Point.Z - nodes[0].Point.Z;
                var reach = Math.Sqrt(dx*dx + dz*dz) / stroke.TotalDistance;
                minReach = Math.Min(minReach,reach);
                var visited = new HashSet<(float,float)>();
                var headings = new HashSet<(float,float)>();
                foreach (var node in nodes)
                {
                    if (!visited.Add((node.Point.X,node.Point.Z))) revisits++;
                    headings.Add((node.Direction.X,node.Direction.Z));
                }
                if (headings.Count > 2) curved++;
                Check(nodes[0].DistanceFromStart == 0,"branch distance starts at branch point");
                if (stroke.HasParent)
                    Check(nodes[0].Point.Equals(tree.Strokes[stroke.ParentStrokeIndex].Nodes[stroke.ParentNodeIndex].Point),
                        "continuous branch anchor remains on parent");
                double accumulatedTurn = 0;
                var sign = 0;
                var broad = false;
                for (var i = 1; i < nodes.Length; i++)
                {
                    var previous = nodes[i-1]; var next = nodes[i];
                    var turn = Math.Atan2(previous.Direction.X * next.Direction.Z - previous.Direction.Z * next.Direction.X,
                        previous.Direction.X * next.Direction.X + previous.Direction.Z * next.Direction.Z);
                    Check(Math.Abs(turn) < .35,"continuous bend without abrupt node turns");
                    if (Math.Sign(turn) != 0 && Math.Sign(turn) != sign)
                    {
                        accumulatedTurn = 0;
                        sign = Math.Sign(turn);
                    }
                    accumulatedTurn += Math.Abs(turn);
                    largestLobe = Math.Max(largestLobe,accumulatedTurn);
                    if (accumulatedTurn > Math.PI / 3) broad = true;
                    var reference = i == 1 && stroke.HasParent
                        ? tree.Strokes[stroke.ParentStrokeIndex].Nodes[stroke.ParentNodeIndex].Direction
                        : previous.Direction;
                    var mx = next.Point.X - previous.Point.X;
                    var mz = next.Point.Z - previous.Point.Z;
                    Check(Math.Abs(mx) <= 1 && Math.Abs(mz) <= 1 && Math.Abs(mx)+Math.Abs(mz)>0,"raster step");
                    Check(Math.Abs(Math.Sqrt(mx*mx+mz*mz)-1) < .0002,"one Cell continuous step");
                    Check(Math.Abs(reference.X) >= Math.Abs(reference.Z)
                        ? reference.X * mx >= 0 && reference.X * next.Direction.X >= 0
                        : reference.Z * mz >= 0 && reference.Z * next.Direction.Z >= 0,"previous dominant axis including branch first step");
                }
                if (broad) broadBends++;
            }
            Parallel.For(0,2,_ => {
                var other = Make();
                Check(other.Strokes.Count == tree.Strokes.Count,"parallel branch count");
                for (var i = 0; i < tree.Strokes.Count; i++)
                    Check(tree.Strokes[i].Nodes.SequenceEqual(other.Strokes[i].Nodes),"parallel path determinism");
            });
        }
        Check(curved > total/2,"continuous bends retained");
        Check(broadBends > total/4,"large accumulated bends retained");
        Check(largestLobe < Math.PI + .2,"curvature sign must change before coiling");
        Console.WriteLine($"PASS River bends: 200 roots, {total} strokes, {broadBends} broad bends, largest lobe={largestLobe*180/Math.PI:F1} degrees, revisits={revisits}, displacement/path minimum={minReach:F3}; branches and parallel determinism");
    }
    private static void Check(bool ok,string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }
}
