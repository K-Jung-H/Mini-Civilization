using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Generation
{
    internal static class HydrologyMapBuilder
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static HydrologyMap Build(
            int size,
            int seaLevelUnits,
            IReadOnlyList<int> terrainHeights,
            IReadOnlyList<int> waterSurfaces)
        {
            if (waterSurfaces == null
                || waterSurfaces.Count != checked(size * size))
            {
                throw new ArgumentException(
                    "Water surfaces must cover the entire hydrology map.",
                    nameof(waterSurfaces));
            }

            var map = new HydrologyMap(size, terrainHeights);
            var seaMask = new bool[map.ColumnCount];
            for (var index = 0; index < map.ColumnCount; index++)
            {
                seaMask[index] = waterSurfaces[index] > 0;
            }

            BuildPriorityFlood(map, seaMask, Math.Max(0, seaLevelUnits));
            BuildSeaDistances(map, seaMask);
            BuildBasins(map, seaMask);
            return map;
        }

        private static void BuildPriorityFlood(
            HydrologyMap map,
            bool[] seaMask,
            int seaLevelUnits)
        {
            var discovered = new bool[map.ColumnCount];
            var visitOrder = new List<int>(map.ColumnCount);
            var queue = new HydrologyPriorityQueue(map.ColumnCount);
            for (var x = 0; x < map.Size; x++)
            {
                EnqueueBoundary(x, 0);
                EnqueueBoundary(x, map.Size - 1);
            }

            for (var z = 1; z < map.Size - 1; z++)
            {
                EnqueueBoundary(0, z);
                EnqueueBoundary(map.Size - 1, z);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                visitOrder.Add(current.Index);
                var currentX = current.Index % map.Size;
                var currentZ = current.Index / map.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var nextX = currentX + Directions[directionIndex].x;
                    var nextZ = currentZ + Directions[directionIndex].z;
                    if (!map.Contains(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = map.ToIndex(nextX, nextZ);
                    if (discovered[nextIndex])
                    {
                        continue;
                    }

                    discovered[nextIndex] = true;
                    var filledHeight = Math.Max(
                        map.GetTerrainHeightUnits(nextIndex),
                        current.HeightUnits);
                    map.SetFilledHeightUnits(nextIndex, filledHeight);
                    map.SetReceiverColumnIndex(nextIndex, current.Index);
                    map.SetSeaConnected(
                        nextIndex,
                        map.IsSeaConnected(current.Index));
                    queue.Enqueue(nextIndex, filledHeight);
                }
            }

            var accumulation = new int[map.ColumnCount];
            Array.Fill(accumulation, 1);
            for (var orderIndex = visitOrder.Count - 1;
                 orderIndex >= 0;
                 orderIndex--)
            {
                var index = visitOrder[orderIndex];
                var receiver = map.GetReceiverColumnIndex(index);
                if (receiver >= 0)
                {
                    accumulation[receiver] = accumulation[receiver]
                        > int.MaxValue - accumulation[index]
                            ? int.MaxValue
                            : accumulation[receiver]
                                + accumulation[index];
                }
            }

            for (var index = 0; index < accumulation.Length; index++)
            {
                map.SetFlowAccumulation(index, accumulation[index]);
            }

            void EnqueueBoundary(int x, int z)
            {
                var index = map.ToIndex(x, z);
                if (discovered[index])
                {
                    return;
                }

                discovered[index] = true;
                var height = seaMask[index]
                    ? Math.Max(
                        map.GetTerrainHeightUnits(index),
                        seaLevelUnits)
                    : map.GetTerrainHeightUnits(index);
                map.SetFilledHeightUnits(index, height);
                map.SetSeaConnected(index, seaMask[index]);
                queue.Enqueue(index, height);
            }
        }

        private static void BuildSeaDistances(
            HydrologyMap map,
            bool[] seaMask)
        {
            var queue = new Queue<int>();
            for (var index = 0; index < seaMask.Length; index++)
            {
                if (!seaMask[index])
                {
                    continue;
                }

                map.SetSeaDistance(index, 0);
                queue.Enqueue(index);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDistance = map.GetSeaDistance(current);
                var currentX = current % map.Size;
                var currentZ = current / map.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var nextX = currentX + Directions[directionIndex].x;
                    var nextZ = currentZ + Directions[directionIndex].z;
                    if (!map.Contains(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = map.ToIndex(nextX, nextZ);
                    if (map.GetSeaDistance(nextIndex) <= currentDistance + 1)
                    {
                        continue;
                    }

                    map.SetSeaDistance(nextIndex, currentDistance + 1);
                    queue.Enqueue(nextIndex);
                }
            }
        }

        private static void BuildBasins(
            HydrologyMap map,
            bool[] seaMask)
        {
            var basinMarkers = new int[map.ColumnCount];
            Array.Fill(basinMarkers, -1);
            var queue = new Queue<int>();
            var basinId = 0;
            for (var start = 0; start < map.ColumnCount; start++)
            {
                if (basinMarkers[start] >= 0
                    || seaMask[start]
                    || map.GetFilledHeightUnits(start)
                    <= map.GetTerrainHeightUnits(start))
                {
                    continue;
                }

                var columns = new List<int>();
                var basinSpillHeight =
                    map.GetFilledHeightUnits(start);
                queue.Enqueue(start);
                basinMarkers[start] = basinId;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    columns.Add(current);
                    var currentX = current % map.Size;
                    var currentZ = current / map.Size;
                    for (var directionIndex = 0;
                         directionIndex < Directions.Length;
                         directionIndex++)
                    {
                        var nextX = currentX + Directions[directionIndex].x;
                        var nextZ = currentZ + Directions[directionIndex].z;
                        if (!map.Contains(nextX, nextZ))
                        {
                            continue;
                        }

                        var nextIndex = map.ToIndex(nextX, nextZ);
                        if (basinMarkers[nextIndex] >= 0
                            || seaMask[nextIndex]
                            || map.GetFilledHeightUnits(nextIndex)
                                != basinSpillHeight
                            || map.GetFilledHeightUnits(nextIndex)
                            <= map.GetTerrainHeightUnits(nextIndex))
                        {
                            continue;
                        }

                        basinMarkers[nextIndex] = basinId;
                        queue.Enqueue(nextIndex);
                    }
                }

                var spillHeight = basinSpillHeight;
                var outletIndex = -1;
                var outletHeight = int.MaxValue;
                var minimumSeaDistance = int.MaxValue;
                var maximumDepth = 0;
                var accumulation = 1;
                for (var columnIndex = 0;
                     columnIndex < columns.Count;
                     columnIndex++)
                {
                    var index = columns[columnIndex];
                    minimumSeaDistance = Math.Min(
                        minimumSeaDistance,
                        map.GetSeaDistance(index));
                    maximumDepth = Math.Max(
                        maximumDepth,
                        map.GetFilledHeightUnits(index)
                        - map.GetTerrainHeightUnits(index));
                    accumulation = Math.Max(
                        accumulation,
                        map.GetFlowAccumulation(index));
                    var receiver = map.GetReceiverColumnIndex(index);
                    if (receiver < 0
                        || basinMarkers[receiver] == basinId)
                    {
                        continue;
                    }

                    var candidateHeight =
                        map.GetFilledHeightUnits(receiver);
                    if (candidateHeight < outletHeight
                        || (candidateHeight == outletHeight
                            && receiver < outletIndex))
                    {
                        outletHeight = candidateHeight;
                        outletIndex = receiver;
                    }
                }

                for (var columnIndex = 0;
                     columnIndex < columns.Count;
                     columnIndex++)
                {
                    map.SetBasin(
                        columns[columnIndex],
                        basinId,
                        spillHeight);
                }

                map.AddBasin(new HydrologyBasin(
                    basinId,
                    columns,
                    spillHeight,
                    outletIndex,
                    minimumSeaDistance,
                    maximumDepth,
                    accumulation));
                basinId++;
            }
        }

        private readonly struct QueueNode
        {
            public readonly int Index;
            public readonly int HeightUnits;

            public QueueNode(int index, int heightUnits)
            {
                Index = index;
                HeightUnits = heightUnits;
            }
        }

        private sealed class HydrologyPriorityQueue
        {
            private readonly List<QueueNode> heap;

            public int Count => heap.Count;

            public HydrologyPriorityQueue(int capacity)
            {
                heap = new List<QueueNode>(Math.Max(0, capacity));
            }

            public void Enqueue(int index, int heightUnits)
            {
                heap.Add(new QueueNode(index, heightUnits));
                var child = heap.Count - 1;
                while (child > 0)
                {
                    var parent = (child - 1) / 2;
                    if (Compare(heap[parent], heap[child]) <= 0)
                    {
                        break;
                    }

                    (heap[parent], heap[child]) =
                        (heap[child], heap[parent]);
                    child = parent;
                }
            }

            public QueueNode Dequeue()
            {
                if (heap.Count == 0)
                {
                    throw new InvalidOperationException(
                        "The hydrology priority queue is empty.");
                }

                var result = heap[0];
                var lastIndex = heap.Count - 1;
                heap[0] = heap[lastIndex];
                heap.RemoveAt(lastIndex);
                var parent = 0;
                while (true)
                {
                    var left = parent * 2 + 1;
                    if (left >= heap.Count)
                    {
                        break;
                    }

                    var right = left + 1;
                    var child = right < heap.Count
                        && Compare(heap[right], heap[left]) < 0
                            ? right
                            : left;
                    if (Compare(heap[parent], heap[child]) <= 0)
                    {
                        break;
                    }

                    (heap[parent], heap[child]) =
                        (heap[child], heap[parent]);
                    parent = child;
                }

                return result;
            }

            private static int Compare(QueueNode left, QueueNode right)
            {
                var heightComparison =
                    left.HeightUnits.CompareTo(right.HeightUnits);
                return heightComparison != 0
                    ? heightComparison
                    : left.Index.CompareTo(right.Index);
            }
        }
    }
}
