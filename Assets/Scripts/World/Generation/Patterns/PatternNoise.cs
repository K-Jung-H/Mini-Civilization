using System;

namespace MiniCivilization.World.Generation.Patterns
{
    internal static class PatternNoise
    {
        public static int DeriveSeed(int worldSeed, string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            unchecked
            {
                var hash = (uint)worldSeed;
                for (var index = 0; index < path.Length; index++)
                {
                    hash ^= path[index];
                    hash *= 16777619u;
                }

                return (int)hash;
            }
        }

        public static uint Hash(long x, long z, int seed)
        {
            unchecked
            {
                var value = (ulong)(uint)seed ^ 0x9E3779B97F4A7C15UL;
                value = Mix(value ^ (ulong)x);
                value = Mix(value ^ RotateLeft((ulong)z, 32));
                return (uint)(value ^ value >> 32);
            }
        }

        public static uint Hash(long x, long y, long z, int seed)
        {
            unchecked
            {
                var value = (ulong)(uint)seed ^ 0xD6E8FEB86659FD93UL;
                value = Mix(value ^ (ulong)x);
                value = Mix(value ^ RotateLeft((ulong)y, 21));
                value = Mix(value ^ RotateLeft((ulong)z, 42));
                return (uint)(value ^ value >> 32);
            }
        }

        public static float Value01(long x, long z, int seed) =>
            (Hash(x, z, seed) & 0x00FFFFFFu) / 16777215f;

        public static float SignedValue01(long x, long z, int seed) =>
            Value01(x, z, seed) * 2f - 1f;

        public static float Sample(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed)
        {
            var value = field.Mode is PatternNoiseMode.Ridge
                or PatternNoiseMode.SignedRidge
                ? RidgedFractalNoise(
                    x * field.Scale,
                    z * field.Scale,
                    seed,
                    field.Layers,
                    field.FrequencySpacing,
                    field.Persistence,
                    field.OctaveSeedStride)
                : FractalNoise(
                    x * field.Scale,
                    z * field.Scale,
                    seed,
                    field.Layers,
                    field.FrequencySpacing,
                    field.Persistence,
                    field.OctaveSeedStride);
            return field.Mode is PatternNoiseMode.Signed
                or PatternNoiseMode.SignedRidge
                ? value * 2f - 1f
                : value;
        }

        public static float Normalize(float value, PatternNoiseMode mode) =>
            mode is PatternNoiseMode.Signed or PatternNoiseMode.SignedRidge
                ? Math.Clamp((value + 1f) * 0.5f, 0f, 1f)
                : Math.Clamp(value, 0f, 1f);

        public static float SampleNormalized(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => Normalize(Sample(x, z, field, seed), field.Mode);

        public static float SampleSigned(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => SampleNormalized(x, z, field, seed) * 2f - 1f;

        private static float FractalNoise(
            double x,
            double z,
            int seed,
            int layers,
            float frequencySpacing,
            float persistence,
            int octaveSeedStride)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var total = 0f;
            var totalAmplitude = 0f;
            for (var layer = 0; layer < layers; layer++)
            {
                total += ValueNoise(x * frequency, z * frequency,
                    unchecked(seed + layer * octaveSeedStride)) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= frequencySpacing;
            }

            return totalAmplitude > 0f ? total / totalAmplitude : 0f;
        }

        private static float RidgedFractalNoise(
            double x,
            double z,
            int seed,
            int layers,
            float frequencySpacing,
            float persistence,
            int octaveSeedStride)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var total = 0f;
            var totalAmplitude = 0f;
            for (var layer = 0; layer < layers; layer++)
            {
                var value = ValueNoise(x * frequency, z * frequency,
                    unchecked(seed + layer * octaveSeedStride)) * 2f - 1f;
                var ridge = 1f - MathF.Abs(value);
                total += ridge * ridge * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= frequencySpacing;
            }

            return totalAmplitude > 0f ? total / totalAmplitude : 0f;
        }

        private static float ValueNoise(double x, double z, int seed)
        {
            var x0 = checked((long)Math.Floor(x));
            var z0 = checked((long)Math.Floor(z));
            var tx = Smooth((float)(x - x0));
            var tz = Smooth((float)(z - z0));
            var lower = Lerp(
                Value01(x0, z0, seed),
                Value01(checked(x0 + 1), z0, seed),
                tx);
            var upper = Lerp(
                Value01(x0, checked(z0 + 1), seed),
                Value01(checked(x0 + 1), checked(z0 + 1), seed),
                tx);
            return Lerp(lower, upper, tz);
        }

        private static float Smooth(float value) =>
            value * value * (3f - 2f * value);

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;

        private static ulong RotateLeft(ulong value, int count) =>
            value << count | value >> (64 - count);

        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                return value ^ value >> 31;
            }
        }
    }
}
