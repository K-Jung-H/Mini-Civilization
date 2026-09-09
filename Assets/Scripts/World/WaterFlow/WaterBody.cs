using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    public sealed class WaterBody
    {
        public int Id { get; }
        public int VolumeUnits { get; internal set; }
        public int SurfaceCellCount { get; internal set; }
        public bool TouchesWorldEdge { get; internal set; }
        public IReadOnlyList<CellCoordinate> Cells => (IReadOnlyList<CellCoordinate>)composedCells ?? cells;
        private PartTree composedCells;
        private static long nextPartKey;
        private readonly long partKey = System.Threading.Interlocked.Increment(ref nextPartKey);

        private readonly List<CellCoordinate> cells = new();

        internal WaterBody(int id)
        {
            Id = id;
        }

        internal void Add(CellCoordinate coordinate) => cells.Add(coordinate);

        // Used only while assembling an unpublished graph result.
        internal void AddPart(WaterBody part)
        {
            composedCells = PartTree.Set(composedCells, part.partKey, part);
            VolumeUnits += part.VolumeUnits;
            SurfaceCellCount += part.SurfaceCellCount;
            TouchesWorldEdge |= part.TouchesWorldEdge;
        }

        internal WaterBody CopyComposition() => new WaterBody(Id)
        {
            composedCells = composedCells,
            VolumeUnits = VolumeUnits,
            SurfaceCellCount = SurfaceCellCount,
            TouchesWorldEdge = TouchesWorldEdge
        };

        internal void RemovePart(WaterBody part)
        {
            composedCells = PartTree.Set(composedCells, part.partKey, null);
            VolumeUnits -= part.VolumeUnits;
            SurfaceCellCount -= part.SurfaceCellCount;
            TouchesWorldEdge = composedCells?.TouchesEdge ?? false;
        }

        internal WaterBody(int id, IReadOnlyList<WaterBody> parts) : this(id)
        {
            foreach (var part in parts)
            {
                AddPart(part);
            }
        }

        // Immutable AVL composition keeps published cells stable while a repair edits its copy.
        // Adding/removing a chunk component copies only a logarithmic path, not the whole ocean.
        private sealed class PartTree : IReadOnlyList<CellCoordinate>
        {
            private readonly long key;
            private readonly WaterBody part;
            private readonly PartTree left, right;
            private readonly int height;
            public int Count { get; }
            internal bool TouchesEdge { get; }
            private static int Height(PartTree node) => node?.height ?? 0;
            private PartTree(long key, WaterBody part, PartTree left, PartTree right)
            {
                this.key = key; this.part = part; this.left = left; this.right = right;
                height = 1 + System.Math.Max(Height(left), Height(right));
                Count = (left?.Count ?? 0) + part.Cells.Count + (right?.Count ?? 0);
                TouchesEdge = part.TouchesWorldEdge || (left?.TouchesEdge ?? false) || (right?.TouchesEdge ?? false);
            }
            internal static PartTree Set(PartTree node, long key, WaterBody part)
            {
                if (node == null) return part == null ? null : new PartTree(key, part, null, null);
                if (key < node.key) return Balance(new PartTree(node.key, node.part, Set(node.left, key, part), node.right));
                if (key > node.key) return Balance(new PartTree(node.key, node.part, node.left, Set(node.right, key, part)));
                if (part != null) return new PartTree(key, part, node.left, node.right);
                if (node.left == null) return node.right;
                if (node.right == null) return node.left;
                var successor = node.right;
                while (successor.left != null) successor = successor.left;
                return Balance(new PartTree(successor.key, successor.part, node.left, Set(node.right, successor.key, null)));
            }
            private static PartTree RotateLeft(PartTree n) => new PartTree(n.right.key, n.right.part,
                new PartTree(n.key, n.part, n.left, n.right.left), n.right.right);
            private static PartTree RotateRight(PartTree n) => new PartTree(n.left.key, n.left.part,
                n.left.left, new PartTree(n.key, n.part, n.left.right, n.right));
            private static PartTree Balance(PartTree n)
            {
                if (Height(n.left) - Height(n.right) > 1)
                {
                    if (Height(n.left.left) < Height(n.left.right))
                        n = new PartTree(n.key, n.part, RotateLeft(n.left), n.right);
                    return RotateRight(n);
                }
                if (Height(n.right) - Height(n.left) > 1)
                {
                    if (Height(n.right.right) < Height(n.right.left))
                        n = new PartTree(n.key, n.part, n.left, RotateRight(n.right));
                    return RotateLeft(n);
                }
                return n;
            }
            public CellCoordinate this[int index]
            {
                get
                {
                    if ((uint)index >= Count) throw new System.ArgumentOutOfRangeException(nameof(index));
                    var leftCount = left?.Count ?? 0;
                    if (index < leftCount) return left[index];
                    index -= leftCount;
                    return index < part.Cells.Count ? part.Cells[index] : right[index - part.Cells.Count];
                }
            }
            public IEnumerator<CellCoordinate> GetEnumerator()
            {
                if (left != null) foreach (var cell in left) yield return cell;
                foreach (var cell in part.Cells) yield return cell;
                if (right != null) foreach (var cell in right) yield return cell;
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
