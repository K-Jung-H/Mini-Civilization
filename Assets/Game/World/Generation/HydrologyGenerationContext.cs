using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal readonly struct HydrologyTerrainSample
    {
        public HydrologyTerrainSample(
            in WorldFieldSample field,
            in WorldPatternResult terrain,
            in TerrainSurfaceSample surface)
        {
            Field = field;
            Terrain = terrain;
            Surface = surface;
        }

        public WorldFieldSample Field { get; }
        public WorldPatternResult Terrain { get; }
        public TerrainSurfaceSample Surface { get; }
    }

    internal sealed class HydrologyGenerationContext
    {
        private readonly HydrologyTerrainField terrainField;

        public HydrologyGenerationContext(WorldSettingsData settings)
        {
            Settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
            terrainField = new HydrologyTerrainField(settings);
        }

        public WorldSettingsData Settings { get; }

        public TerrainSurfaceSample SampleBaseTerrain(
            int worldX,
            int worldZ) => SampleBaseTerrainState(worldX, worldZ).Surface;

        public HydrologyTerrainSample SampleBaseTerrainState(
            int worldX,
            int worldZ) => terrainField.Sample(
                worldX,
                worldZ);

        internal IDisposable BeginTerrainLease() => terrainField.BeginLease();

        public void ValidateSettings(WorldSettingsData settings)
        {
            if (!ReferenceEquals(Settings, settings))
            {
                throw new InvalidOperationException(
                    "Hydrology context belongs to different World settings.");
            }
        }

        internal sealed class HydrologyTerrainField
        {
            private readonly WorldSettingsData settings;
            private readonly WorldNoiseRouter router;
            private readonly WorldDensityField density;
            internal readonly int tileSize;
            private readonly ConcurrentDictionary<long, TerrainTileEntry> tiles =
                new();
            internal readonly AsyncLocal<TerrainLease> activeLease = new();
            private readonly object tileGate = new();

            public HydrologyTerrainField(WorldSettingsData settings)
            {
                this.settings = settings;
                router = new WorldNoiseRouter(settings);
                density = new WorldDensityField(settings);
                tileSize = settings.Hydrology.Map.PlanningRegionSizeCells;
            }

            public HydrologyTerrainSample Sample(int worldX, int worldZ)
            {
                var lease = activeLease.Value;
                if (lease == null)
                {
                    return SampleUncached(worldX, worldZ);
                }

                return lease.Sample(worldX, worldZ);
            }

            public IDisposable BeginLease()
            {
                var lease = new TerrainLease(this, activeLease.Value);
                activeLease.Value = lease;
                return lease;
            }

            private HydrologyTerrainSample SampleUncached(int worldX, int worldZ)
            {
                var field = router.Sample(worldX, worldZ);
                var terrain = WorldPatternResolver.Resolve(
                    router,
                    worldX,
                    worldZ,
                    field,
                    settings,
                    out _);
                var surface = TerrainSurfaceSampler.SampleResolved(
                    density,
                    settings,
                    worldX,
                    worldZ,
                    field,
                    terrain);
                return new HydrologyTerrainSample(field, terrain, surface);
            }

            internal HydrologyTerrainSample SampleCached(
                TerrainTile tile,
                int index,
                int worldX,
                int worldZ)
            {
                if (!tile.TryBeginSample(index, out var cached))
                {
                    return cached;
                }

                try
                {
                    var field = router.Sample(worldX, worldZ);
                    var terrain = WorldPatternResolver.Resolve(
                        router,
                        worldX,
                        worldZ,
                        field,
                        settings,
                        out _);
                    var surface = TerrainSurfaceSampler.SampleResolved(
                        density,
                        settings,
                        worldX,
                        worldZ,
                        field,
                        terrain);
                    var sample = new HydrologyTerrainSample(
                        field,
                        terrain,
                        surface);
                    tile.CompleteSample(index, sample);
                    return sample;
                }
                catch
                {
                    tile.CancelSample(index);
                    throw;
                }
            }

            internal TerrainTileEntry AcquireTile(
                int worldX,
                int worldZ,
                out int index)
            {
                var tileX = FloorDivide(worldX, tileSize);
                var tileZ = FloorDivide(worldZ, tileSize);
                var originX = checked(tileX * tileSize);
                var originZ = checked(tileZ * tileSize);
                var localX = worldX - originX;
                var localZ = worldZ - originZ;
                index = localX + tileSize * localZ;
                lock (tileGate)
                {
                    var entry = tiles.GetOrAdd(
                        CoordinateKey(tileX, tileZ),
                        _ => new TerrainTileEntry(tileSize));
                    entry.LeaseCount++;
                    return entry;
                }
            }

            internal void ReleaseTile(long key, TerrainTileEntry entry)
            {
                lock (tileGate)
                {
                    entry.LeaseCount--;
                    if (entry.LeaseCount != 0)
                    {
                        return;
                    }

                    tiles.TryRemove(key, out _);
                }
            }

            internal static int FloorDivide(int value, int divisor)
            {
                var quotient = value / divisor;
                return value % divisor < 0 ? quotient - 1 : quotient;
            }

            internal static long CoordinateKey(int x, int z) =>
                ((long)x << 32) ^ (uint)z;
        }

        internal sealed class TerrainLease : IDisposable
        {
            private readonly HydrologyTerrainField owner;
            private readonly TerrainLease parent;
            private readonly Dictionary<long, TerrainTileEntry> entries = new();
            private bool disposed;

            public TerrainLease(
                HydrologyTerrainField owner,
                TerrainLease parent)
            {
                this.owner = owner;
                this.parent = parent;
            }

            public HydrologyTerrainSample Sample(int worldX, int worldZ)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(TerrainLease));
                }

                var tileX = HydrologyTerrainField.FloorDivide(
                    worldX,
                    owner.tileSize);
                var tileZ = HydrologyTerrainField.FloorDivide(
                    worldZ,
                    owner.tileSize);
                var key = HydrologyTerrainField.CoordinateKey(tileX, tileZ);
                if (!entries.TryGetValue(key, out var entry))
                {
                    entry = owner.AcquireTile(worldX, worldZ, out _);
                    entries.Add(key, entry);
                }

                var originX = checked(tileX * owner.tileSize);
                var originZ = checked(tileZ * owner.tileSize);
                var index = worldX - originX
                    + owner.tileSize * (worldZ - originZ);
                return owner.SampleCached(
                    entry.Tile,
                    index,
                    worldX,
                    worldZ);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                foreach (var entry in entries)
                {
                    owner.ReleaseTile(entry.Key, entry.Value);
                }

                entries.Clear();
                owner.activeLease.Value = parent;
            }
        }

        internal sealed class TerrainTileEntry
        {
            public TerrainTileEntry(int size)
            {
                Tile = new TerrainTile(size);
            }

            public TerrainTile Tile { get; }
            public int LeaseCount;
        }

        internal sealed class TerrainTile
        {
            private readonly HydrologyTerrainSample[] samples;
            private readonly int[] states;

            public TerrainTile(int size)
            {
                var count = checked(size * size);
                samples = new HydrologyTerrainSample[count];
                states = new int[count];
            }

            public bool TryBeginSample(
                int index,
                out HydrologyTerrainSample sample)
            {
                var spinner = new SpinWait();
                while (true)
                {
                    var state = Volatile.Read(ref states[index]);
                    if (state == 2)
                    {
                        sample = samples[index];
                        return false;
                    }

                    if (state == 0
                        && Interlocked.CompareExchange(
                            ref states[index],
                            1,
                            0) == 0)
                    {
                        sample = default;
                        return true;
                    }

                    spinner.SpinOnce();
                }
            }

            public void CompleteSample(
                int index,
                in HydrologyTerrainSample sample)
            {
                samples[index] = sample;
                Volatile.Write(ref states[index], 2);
            }

            public void CancelSample(int index) =>
                Volatile.Write(ref states[index], 0);
        }
    }
}
