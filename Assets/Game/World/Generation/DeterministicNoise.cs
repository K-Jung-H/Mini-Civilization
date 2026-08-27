using System;

namespace MiniCivilization.World.Generation
{
    public static class DeterministicNoise
    {
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

        public static int DeriveSeed(int worldSeed, string channel)
        {
            unchecked
            {
                var hash = (uint)worldSeed;
                for (var i = 0; i < channel.Length; i++)
                {
                    hash ^= channel[i];
                    hash *= 16777619u;
                }

                return (int)hash;
            }
        }

        public static float Value01(long x, long z, int seed) =>
            (Hash(x, z, seed) & 0x00FFFFFFu) / 16777215f;

        public static float ValueNoise(double x, double z, int seed)
        {
            var x0 = checked((long)Math.Floor(x));
            var z0 = checked((long)Math.Floor(z));
            var tx = Smooth((float)(x - x0));
            var tz = Smooth((float)(z - z0));
            var a = Lerp(Value01(x0, z0, seed), Value01(x0 + 1, z0, seed), tx);
            var b = Lerp(Value01(x0, z0 + 1, seed), Value01(x0 + 1, z0 + 1, seed), tx);
            return Lerp(a, b, tz);
        }

        public static float ValueNoise(double x, double y, double z, int seed)
        {
            var x0 = checked((long)Math.Floor(x));
            var y0 = checked((long)Math.Floor(y));
            var z0 = checked((long)Math.Floor(z));
            var tx = Smooth((float)(x - x0));
            var ty = Smooth((float)(y - y0));
            var tz = Smooth((float)(z - z0));

            var x00 = Lerp(Value01(x0, y0, z0, seed), Value01(x0 + 1, y0, z0, seed), tx);
            var x10 = Lerp(Value01(x0, y0 + 1, z0, seed), Value01(x0 + 1, y0 + 1, z0, seed), tx);
            var x01 = Lerp(Value01(x0, y0, z0 + 1, seed), Value01(x0 + 1, y0, z0 + 1, seed), tx);
            var x11 = Lerp(Value01(x0, y0 + 1, z0 + 1, seed), Value01(x0 + 1, y0 + 1, z0 + 1, seed), tx);
            return Lerp(Lerp(x00, x10, ty), Lerp(x01, x11, ty), tz);
        }

        public static float FractalNoise(
            double x,
            double z,
            int seed,
            int octaves,
            float lacunarity,
            float persistence)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var total = 0f;
            var weight = 0f;

            for (var octave = 0; octave < octaves; octave++)
            {
                total += ValueNoise(x * frequency, z * frequency, seed + octave * 1013) * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return weight > 0f ? total / weight : 0f;
        }

        public static float FractalNoise(
            double x,
            double y,
            double z,
            int seed,
            int octaves,
            float lacunarity,
            float persistence)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var total = 0f;
            var weight = 0f;

            for (var octave = 0; octave < octaves; octave++)
            {
                total += ValueNoise(
                    x * frequency,
                    y * frequency,
                    z * frequency,
                    seed + octave * 1013) * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return weight > 0f ? total / weight : 0f;
        }

        private static float Value01(long x, long y, long z, int seed) =>
            (Hash(x, y, z, seed) & 0x00FFFFFFu) / 16777215f;

        public static float RidgedFractalNoise(
            double x,
            double z,
            int seed,
            int octaves,
            float lacunarity,
            float persistence)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var total = 0f;
            var weight = 0f;

            for (var octave = 0; octave < octaves; octave++)
            {
                var sample = ValueNoise(
                    x * frequency,
                    z * frequency,
                    seed + octave * 1013) * 2f - 1f;
                var ridge = 1f - MathF.Abs(sample);
                total += ridge * ridge * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return weight > 0f ? total / weight : 0f;
        }

        public static float RidgedFractalNoise(
            double x,
            double y,
            double z,
            int seed,
            int octaves,
            float lacunarity,
            float persistence)
        {
            var amplitude = 1f;
            var frequency = 1f;
            var total = 0f;
            var weight = 0f;

            for (var octave = 0; octave < octaves; octave++)
            {
                var sample = ValueNoise(
                    x * frequency,
                    y * frequency,
                    z * frequency,
                    seed + octave * 1013) * 2f - 1f;
                var ridge = 1f - MathF.Abs(sample);
                total += ridge * ridge * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return weight > 0f ? total / weight : 0f;
        }

        private static float Smooth(float value) => value * value * (3f - 2f * value);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

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
