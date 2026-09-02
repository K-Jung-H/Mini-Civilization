using System;

namespace MiniCivilization.World.Runtime
{
    public readonly struct WorldStreamingProgress :
        IEquatable<WorldStreamingProgress>
    {
        public WorldStreamingProgress(
            int completedChunkCount,
            int requestedChunkCount)
        {
            if (completedChunkCount < 0
                || requestedChunkCount < 0
                || completedChunkCount > requestedChunkCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(completedChunkCount));
            }

            CompletedChunkCount = completedChunkCount;
            RequestedChunkCount = requestedChunkCount;
        }

        public int CompletedChunkCount { get; }
        public int RequestedChunkCount { get; }
        public bool IsGenerating =>
            RequestedChunkCount > CompletedChunkCount;
        public int PercentComplete => RequestedChunkCount == 0
            ? 0
            : (int)MathF.Floor(
                CompletedChunkCount * 100f / RequestedChunkCount);

        public bool Equals(WorldStreamingProgress other) =>
            CompletedChunkCount == other.CompletedChunkCount
            && RequestedChunkCount == other.RequestedChunkCount;

        public override bool Equals(object obj) =>
            obj is WorldStreamingProgress other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            CompletedChunkCount,
            RequestedChunkCount);
    }
}
