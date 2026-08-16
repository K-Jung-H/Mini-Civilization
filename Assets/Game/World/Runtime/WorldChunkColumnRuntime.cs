using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    public enum WorldChunkColumnState : byte
    {
        Unloaded,
        Preparing,
        Rendered,
        Active
    }

    public sealed class WorldChunkColumnRuntime
    {
        public ChunkColumnCoordinate Coordinate { get; }
        public WorldChunkColumnState State { get; private set; }

        internal WorldChunkColumnRuntime(ChunkColumnCoordinate coordinate)
        {
            Coordinate = coordinate;
            State = WorldChunkColumnState.Unloaded;
        }

        internal bool SetState(WorldChunkColumnState state)
        {
            if (!IsValidTransition(State, state))
            {
                throw new InvalidOperationException(
                    $"Invalid Column state transition {State} -> {state} at {Coordinate}.");
            }

            if (State == state)
            {
                return false;
            }

            State = state;
            return true;
        }

        private static bool IsValidTransition(
            WorldChunkColumnState current,
            WorldChunkColumnState next) =>
            (current == WorldChunkColumnState.Unloaded
                && next == WorldChunkColumnState.Preparing)
            || (current == WorldChunkColumnState.Preparing
                && (next == WorldChunkColumnState.Rendered
                    || next == WorldChunkColumnState.Unloaded))
            || (current == WorldChunkColumnState.Rendered
                && (next == WorldChunkColumnState.Active
                    || next == WorldChunkColumnState.Unloaded))
            || (current == WorldChunkColumnState.Active
                && next == WorldChunkColumnState.Rendered);
    }
}
