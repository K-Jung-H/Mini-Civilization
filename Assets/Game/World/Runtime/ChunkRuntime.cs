using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    public enum ChunkState : byte
    {
        Unloaded,
        Preparing,
        Ready,
        Active
    }

    public sealed class ChunkRuntime
    {
        public ChunkCoordinate Coordinate { get; }
        public ChunkState State { get; private set; }
        public bool TerrainRenderingEnabled { get; private set; }
        public bool EntityRenderingEnabled { get; private set; }

        internal ChunkRuntime(ChunkCoordinate coordinate)
        {
            Coordinate = coordinate;
            State = ChunkState.Unloaded;
        }

        internal bool SetEntityRenderingEnabled(bool enabled)
        {
            if (EntityRenderingEnabled == enabled)
            {
                return false;
            }

            EntityRenderingEnabled = enabled;
            return true;
        }

        internal bool SetTerrainRenderingEnabled(bool enabled)
        {
            if (TerrainRenderingEnabled == enabled)
            {
                return false;
            }

            TerrainRenderingEnabled = enabled;
            return true;
        }

        internal bool SetState(ChunkState state)
        {
            if (!IsValidTransition(State, state))
            {
                throw new InvalidOperationException(
                    $"Invalid Chunk state transition {State} -> {state} at {Coordinate}.");
            }

            if (State == state)
            {
                return false;
            }

            State = state;
            return true;
        }

        private static bool IsValidTransition(
            ChunkState current,
            ChunkState next) =>
            (current == ChunkState.Unloaded
                && next == ChunkState.Preparing)
            || (current == ChunkState.Preparing
                && (next == ChunkState.Ready
                    || next == ChunkState.Unloaded))
            || (current == ChunkState.Ready
                && (next == ChunkState.Active
                    || next == ChunkState.Unloaded))
            || (current == ChunkState.Active
                && next == ChunkState.Ready);
    }
}
