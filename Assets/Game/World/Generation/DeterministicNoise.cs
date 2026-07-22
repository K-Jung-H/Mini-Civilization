using System;

namespace MiniCivilization.World.Generation
{
    public static class DeterministicNoise
    {
        public static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                var value = (uint)seed;
                value ^= (uint)x * 0x9E3779B9u;
                value = (value << 13) | (value >> 19);
                value ^= (uint)z * 0x85EBCA6Bu;
                value *= 0xC2B2AE35u;
                value ^= value >> 16;
                return value;
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

        public static float Value01(int x, int z, int seed) => (Hash(x, z, seed) & 0x00FFFFFFu) / 16777215f;

        public static float ValueNoise(float x, float z, int seed)
        {
            var x0 = (int)MathF.Floor(x);
            var z0 = (int)MathF.Floor(z);
            var tx = Smooth(x - x0);
            var tz = Smooth(z - z0);
            var a = Lerp(Value01(x0, z0, seed), Value01(x0 + 1, z0, seed), tx);
            var b = Lerp(Value01(x0, z0 + 1, seed), Value01(x0 + 1, z0 + 1, seed), tx);
            return Lerp(a, b, tz);
        }

        public static float FractalNoise(float x, float z, int seed, int octaves, float lacunarity, float persistence)
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

        private static float Smooth(float value) => value * value * (3f - 2f * value);
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
